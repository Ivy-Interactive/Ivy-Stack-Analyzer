namespace Ivy.StackAnalyzer.Data;

/// <summary>A language definition as loaded from <c>languages.yml</c>.</summary>
public sealed class LanguageDef
{
    public string Type { get; set; } = "programming";
    public List<string> Extensions { get; set; } = [];
    public List<string> Filenames { get; set; } = [];
    public List<string> Interpreters { get; set; } = [];
    public string? Color { get; set; }
}

/// <summary>A typed dependency reference within a <see cref="MatchSpec"/>.</summary>
public sealed class DepRef
{
    public string Ecosystem { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class DepRegexRef
{
    public string Ecosystem { get; set; } = "";
    public string Pattern { get; set; } = "";
}

public sealed class DepPrefixRef
{
    public string Ecosystem { get; set; } = "";
    public string Prefix { get; set; } = "";
}

/// <summary>
/// The superset matcher. Any populated facet contributes; a rule matches when
/// <em>any</em> of its facets matches the component (logical OR across facets).
/// </summary>
public sealed class MatchSpec
{
    public List<DepRef> Deps { get; set; } = [];
    public List<DepRegexRef> DepsRegex { get; set; } = [];
    public List<DepPrefixRef> DepPrefix { get; set; } = [];
    public List<string> Files { get; set; } = [];
    public List<string> FilesRegex { get; set; } = [];
    public List<string> Extensions { get; set; } = [];
    public List<string> Dotenv { get; set; } = [];
    public List<string> Sdk { get; set; } = [];
    public List<string> PathGlobs { get; set; } = [];
}

/// <summary>A technology detection rule as loaded from <c>detectors/*.yml</c>.</summary>
public sealed class RuleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "library";
    public MatchSpec Match { get; set; } = new();
    public List<string> Supersedes { get; set; } = [];
    public string Confidence { get; set; } = "high";
}
