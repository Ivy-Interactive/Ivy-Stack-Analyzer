using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Models;

namespace Ivy.StackAnalyzer.Detection;

/// <summary>
/// Matches the loaded <see cref="RuleDef"/>s against a component and produces
/// <see cref="DetectedTechnology"/> facts with human-readable evidence. Applies
/// <c>supersedes</c> ranking so a dominant framework hides the one it subsumes
/// (e.g. Next.js over bare React).
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<RuleDef> _rules;
    private readonly Dictionary<string, Regex> _regexCache = [];

    public RuleEngine(DataStore data) : this(data.Rules) { }

    public RuleEngine(IReadOnlyList<RuleDef> rules) => _rules = rules;

    private static bool IsHashSlot(TechCategory c)
        => c is TechCategory.Framework or TechCategory.Database or TechCategory.Orm;

    public IReadOnlyList<DetectedTechnology> Detect(ComponentContext ctx)
    {
        var matched = new List<(RuleDef Rule, string Evidence, bool Strong, bool Direct)>();
        foreach (var rule in _rules)
        {
            var m = Match(rule, ctx);
            if (m is not null) matched.Add((rule, m.Value.Evidence, m.Value.Strong, m.Value.Direct));
        }

        // Apply supersedes: drop any tech that a present tech supersedes.
        var presentIds = matched.Select(m => m.Rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var superseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rule, _, _, _) in matched)
            foreach (var s in rule.Supersedes)
                if (presentIds.Contains(s)) superseded.Add(s);

        var result = new List<DetectedTechnology>();
        foreach (var (rule, evidence, strong, direct) in matched)
        {
            if (superseded.Contains(rule.Id)) continue;
            var category = CategoryMap.Parse(rule.Category);
            // A dotenv-only match (env-var names are scaffolding, not proof of use)
            // is downgraded to Low so hash/digest consumers can drop the noise.
            var confidence = strong ? CategoryMap.ParseConfidence(rule.Confidence) : Confidence.Low;
            // A hash-slot tech (framework/db/orm) supported only by transitive deps
            // (e.g. FastAPI pulled in by a CLI's pip-compile lockfile) is not a real
            // stack choice — drop it to Low so it never enters a hash slot.
            if (!direct && IsHashSlot(category)) confidence = Confidence.Low;
            result.Add(new DetectedTechnology(
                rule.Name,
                category,
                evidence,
                confidence,
                ctx.RelativePath));
        }

        return result
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Returns evidence text, whether a <em>strong</em> facet matched (anything other
    /// than dotenv), and whether any strong facet was <em>direct</em> (a non-dep facet,
    /// or a dep that is not transitive); or null if the rule doesn't match.
    /// </summary>
    private (string Evidence, bool Strong, bool Direct)? Match(RuleDef rule, ComponentContext ctx)
    {
        var m = rule.Match;
        var evidence = new List<string>();
        bool direct = false; // a non-transitive dep, or any non-dep facet, matched

        // Dependencies (exact)
        foreach (var d in m.Deps)
        {
            var hit = ctx.Dependencies.FirstOrDefault(x =>
                Eco(x.Ecosystem, d.Ecosystem) &&
                string.Equals(x.Dependency.Name, d.Name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                evidence.Add($"{d.Ecosystem} dep '{d.Name}'");
                if (hit.Dependency.Scope != DependencyScope.Transitive) direct = true;
            }
        }

        // Dependency prefixes
        foreach (var p in m.DepPrefix)
        {
            var hit = ctx.Dependencies.FirstOrDefault(x =>
                Eco(x.Ecosystem, p.Ecosystem) &&
                x.Dependency.Name.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                evidence.Add($"{p.Ecosystem} dep '{hit.Dependency.Name}'");
                if (hit.Dependency.Scope != DependencyScope.Transitive) direct = true;
            }
        }

        // Dependency regex
        foreach (var r in m.DepsRegex)
        {
            var rx = GetRegex(r.Pattern);
            var hit = ctx.Dependencies.FirstOrDefault(x =>
                Eco(x.Ecosystem, r.Ecosystem) && rx.IsMatch(x.Dependency.Name));
            if (hit is not null)
            {
                evidence.Add($"{r.Ecosystem} dep '{hit.Dependency.Name}'");
                if (hit.Dependency.Scope != DependencyScope.Transitive) direct = true;
            }
        }

        // Non-dependency facets below (sdk/files/paths/extensions/scripts) are all
        // direct, first-party evidence — a present config file or SDK is never
        // "transitive". If any matches, the rule is directly supported.
        int beforeNonDep = evidence.Count;

        // SDK attribute
        foreach (var sdk in m.Sdk)
            if (ctx.Sdks.Any(s => string.Equals(s, sdk, StringComparison.OrdinalIgnoreCase)))
                evidence.Add($"Sdk={sdk}");

        // Build properties (e.g. MSBuild UseWindowsForms=true)
        foreach (var p in m.Properties)
            if (ctx.Properties.TryGetValue(p.Name, out var v)
                && string.Equals(v, p.Value, StringComparison.OrdinalIgnoreCase))
                evidence.Add($"{p.Name}={v}");

        // Files (exact name)
        foreach (var f in m.Files)
            if (ctx.FileNames.Contains(f)) evidence.Add(f);

        // Files regex (over full relative paths)
        foreach (var fr in m.FilesRegex)
        {
            var rx = GetRegex(fr);
            var hit = ctx.FilePaths.FirstOrDefault(p => rx.IsMatch(p));
            if (hit is not null) evidence.Add(hit);
        }

        // Path globs
        foreach (var g in m.PathGlobs)
        {
            var hit = ctx.FilePaths.FirstOrDefault(p => Glob.IsMatch(p, g));
            if (hit is not null) evidence.Add(hit);
        }

        // Extensions
        foreach (var e in m.Extensions)
        {
            var ext = e.StartsWith('.') ? e : "." + e;
            if (ctx.Extensions.Contains(ext)) evidence.Add($"*{ext}");
        }

        // Run-script commands (e.g. package.json scripts.test = "bun test")
        foreach (var sr in m.ScriptsRegex)
        {
            var rx = GetRegex(sr);
            var hit = ctx.Scripts.FirstOrDefault(s => rx.IsMatch(s));
            if (hit is not null) evidence.Add($"script '{hit}'");
        }

        if (evidence.Count > beforeNonDep) direct = true;

        // Every facet above is a strong signal; dotenv (below) is weak.
        bool strong = evidence.Count > 0;

        // Dotenv prefixes — env-var names are scaffolding (e.g. .env.example), not proof of use.
        foreach (var prefix in m.Dotenv)
            if (ctx.EnvVarNames.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                evidence.Add($"env {prefix}*");

        if (evidence.Count == 0) return null;
        return (string.Join("; ", evidence.Distinct().Take(4)), strong, direct);
    }

    private static bool Eco(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private Regex GetRegex(string pattern)
    {
        if (!_regexCache.TryGetValue(pattern, out var rx))
        {
            try { rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { rx = new Regex("(?!)"); } // never matches
            _regexCache[pattern] = rx;
        }
        return rx;
    }
}
