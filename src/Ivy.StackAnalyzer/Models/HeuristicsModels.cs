namespace Ivy.StackAnalyzer.Models;

// Load-time DTOs mirroring github-linguist's heuristics.yml (MIT). The polymorphic
// fields (language / pattern / named_pattern / negative_pattern) are `string` OR a
// list in linguist's YAML, so they are typed as `object?` and normalized by the
// Scanning.Heuristics engine. See THIRD-PARTY-NOTICES.md.

/// <summary>The parsed contents of <c>heuristics.yml</c>.</summary>
public sealed class HeuristicsFile
{
    public List<DisambiguationDef> Disambiguations { get; set; } = [];

    /// <summary>Reusable patterns referenced by rules via <c>named_pattern</c>.
    /// Each value is a string or a list of strings (OR-combined).</summary>
    public Dictionary<string, object?> NamedPatterns { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One extension group: which file extensions it covers and the ordered
/// rules to try (first matching rule wins).</summary>
public sealed class DisambiguationDef
{
    public object? Extensions { get; set; }          // string or list of strings
    public List<HeuristicRuleDef> Rules { get; set; } = [];
}

/// <summary>A single disambiguation rule. A rule matches when every populated facet
/// is satisfied: <see cref="Pattern"/>/<see cref="NamedPattern"/> (each a value or
/// list, OR-combined) must match, <see cref="NegativePattern"/> must NOT match, and
/// every sub-rule in <see cref="And"/> must match. A rule with no facets always
/// matches (the catch-all default).</summary>
public sealed class HeuristicRuleDef
{
    public object? Language { get; set; }            // string or list of strings
    public object? Pattern { get; set; }             // string or list
    public object? NamedPattern { get; set; }        // string or list (keys into NamedPatterns)
    public object? NegativePattern { get; set; }     // string or list
    public List<HeuristicRuleDef>? And { get; set; }
}
