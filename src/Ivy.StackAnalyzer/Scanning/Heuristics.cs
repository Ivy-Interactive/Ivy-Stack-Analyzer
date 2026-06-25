using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Models;

namespace Ivy.StackAnalyzer.Scanning;

/// <summary>
/// Content-based language disambiguation for extensions claimed by more than one
/// language, ported from github-linguist's <c>Heuristics</c> (MIT). Faithful to
/// linguist's algorithm: rules for a matching extension are tried in order and the
/// first matching rule's language wins. Within a rule, <c>pattern</c>/
/// <c>named_pattern</c> values (a string or an OR-combined list) must match,
/// <c>negative_pattern</c> must not match, and every <c>and</c> sub-rule must match;
/// a rule with no patterns always matches (the catch-all). Regexes use Ruby
/// <c>Regexp.new</c> semantics — case-sensitive with line-anchored <c>^</c>/<c>$</c>
/// (.NET <see cref="RegexOptions.Multiline"/>), and <c>.</c> not crossing newlines.
/// </summary>
public sealed class Heuristics
{
    private sealed record Rule(
        IReadOnlyList<string> Languages,
        IReadOnlyList<string> Positive,
        IReadOnlyList<string> Negative,
        IReadOnlyList<Rule> And);

    private sealed record Group(IReadOnlyList<string> Extensions, IReadOnlyList<Rule> Rules);

    private readonly List<Group> _groups = [];
    private readonly Dictionary<string, Regex> _cache = new(StringComparer.Ordinal);

    public Heuristics(HeuristicsFile file)
    {
        var named = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in file.NamedPatterns) named[key] = AsStrings(value);

        foreach (var d in file.Disambiguations)
        {
            var exts = AsStrings(d.Extensions).Select(Normalize).ToList();
            var rules = d.Rules.Select(r => BuildRule(r, named)).ToList();
            _groups.Add(new Group(exts, rules));
        }
    }

    private static Rule BuildRule(HeuristicRuleDef def, Dictionary<string, List<string>> named)
    {
        var positive = new List<string>(AsStrings(def.Pattern));
        foreach (var name in AsStrings(def.NamedPattern))
            if (named.TryGetValue(name, out var pats)) positive.AddRange(pats);
        var and = (def.And ?? []).Select(a => BuildRule(a, named)).ToList();
        return new Rule(AsStrings(def.Language), positive, AsStrings(def.NegativePattern), and);
    }

    /// <summary>
    /// Returns the languages of the first rule that matches the file content for the
    /// given extension, or <c>null</c> when the extension has no heuristics or no rule
    /// matched (the caller keeps its own fallback). Languages are returned as-is, in
    /// rule order — the caller picks the first it recognises.
    /// </summary>
    public IReadOnlyList<string>? Disambiguate(string extension, string content)
    {
        var ext = Normalize(extension);
        var group = _groups.FirstOrDefault(
            g => g.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)));
        if (group is null) return null;

        foreach (var rule in group.Rules)
            if (Matches(rule, content))
                return rule.Languages.Count > 0 ? rule.Languages : null;
        return null;
    }

    private bool Matches(Rule rule, string content)
    {
        foreach (var sub in rule.And)
            if (!Matches(sub, content)) return false;
        // Positive facet: any one of the (OR-combined) patterns must match.
        if (rule.Positive.Count > 0 && !rule.Positive.Any(p => Rx(p).IsMatch(content)))
            return false;
        // Negative facet: none of the patterns may match.
        if (rule.Negative.Count > 0 && rule.Negative.Any(p => Rx(p).IsMatch(content)))
            return false;
        return true; // no facets => always-match (linguist's AlwaysMatch)
    }

    private Regex Rx(string pattern)
    {
        if (!_cache.TryGetValue(pattern, out var rx))
        {
            try { rx = new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { rx = new Regex("(?!)"); } // unsupported pattern -> never matches
            _cache[pattern] = rx;
        }
        return rx;
    }

    private static string Normalize(string ext) => ext.StartsWith('.') ? ext : "." + ext;

    // linguist's polymorphic scalar-or-sequence fields, normalized to a list of
    // strings. Only scalar items are kept — a non-scalar item (e.g. a mapping from a
    // malformed user overlay) is skipped rather than stringified into a junk pattern.
    private static List<string> AsStrings(object? value) => value switch
    {
        null => [],
        string s => [s],
        IEnumerable<object> seq => seq.OfType<string>().ToList(),
        _ => [value.ToString()!],
    };
}
