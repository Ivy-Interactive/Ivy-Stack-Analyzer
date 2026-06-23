using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Components;
using Ivy.StackAnalyzer.Data;

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

    public IReadOnlyList<DetectedTechnology> Detect(ComponentContext ctx)
    {
        var matched = new List<(RuleDef Rule, string Evidence)>();
        foreach (var rule in _rules)
        {
            var evidence = Match(rule, ctx);
            if (evidence is not null) matched.Add((rule, evidence));
        }

        // Apply supersedes: drop any tech that a present tech supersedes.
        var presentIds = matched.Select(m => m.Rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var superseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rule, _) in matched)
            foreach (var s in rule.Supersedes)
                if (presentIds.Contains(s)) superseded.Add(s);

        var result = new List<DetectedTechnology>();
        foreach (var (rule, evidence) in matched)
        {
            if (superseded.Contains(rule.Id)) continue;
            result.Add(new DetectedTechnology(
                rule.Name,
                CategoryMap.Parse(rule.Category),
                evidence,
                CategoryMap.ParseConfidence(rule.Confidence),
                ctx.RelativePath));
        }

        return result
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Returns evidence text if the rule matches, otherwise null.</summary>
    private string? Match(RuleDef rule, ComponentContext ctx)
    {
        var m = rule.Match;
        var evidence = new List<string>();

        // Dependencies (exact)
        foreach (var d in m.Deps)
        {
            if (ctx.Dependencies.Any(x =>
                Eco(x.Ecosystem, d.Ecosystem) &&
                string.Equals(x.Dependency.Name, d.Name, StringComparison.OrdinalIgnoreCase)))
                evidence.Add($"{d.Ecosystem} dep '{d.Name}'");
        }

        // Dependency prefixes
        foreach (var p in m.DepPrefix)
        {
            var hit = ctx.Dependencies.FirstOrDefault(x =>
                Eco(x.Ecosystem, p.Ecosystem) &&
                x.Dependency.Name.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) evidence.Add($"{p.Ecosystem} dep '{hit.Dependency.Name}'");
        }

        // Dependency regex
        foreach (var r in m.DepsRegex)
        {
            var rx = GetRegex(r.Pattern);
            var hit = ctx.Dependencies.FirstOrDefault(x =>
                Eco(x.Ecosystem, r.Ecosystem) && rx.IsMatch(x.Dependency.Name));
            if (hit is not null) evidence.Add($"{r.Ecosystem} dep '{hit.Dependency.Name}'");
        }

        // SDK attribute
        foreach (var sdk in m.Sdk)
            if (ctx.Sdks.Any(s => string.Equals(s, sdk, StringComparison.OrdinalIgnoreCase)))
                evidence.Add($"Sdk={sdk}");

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

        // Dotenv prefixes
        foreach (var prefix in m.Dotenv)
            if (ctx.EnvVarNames.Any(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                evidence.Add($"env {prefix}*");

        if (evidence.Count == 0) return null;
        return string.Join("; ", evidence.Distinct().Take(4));
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
