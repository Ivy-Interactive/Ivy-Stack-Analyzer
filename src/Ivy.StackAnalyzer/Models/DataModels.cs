namespace Ivy.StackAnalyzer.Models;

/// <summary>A language definition as loaded from <c>languages.yml</c>.</summary>
public sealed class LanguageDef
{
    public string Type { get; set; } = "programming";
    public List<string> Extensions { get; set; } = [];
    public List<string> Filenames { get; set; } = [];
    public List<string> Interpreters { get; set; } = [];
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

/// <summary>A build-property match (e.g. MSBuild <c>UseWindowsForms=true</c>).</summary>
public sealed class PropertyRef
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
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

    /// <summary>MSBuild build-property matches (name + expected value).</summary>
    public List<PropertyRef> Properties { get; set; } = [];

    /// <summary>Regexes matched against run-script command strings (e.g. <c>package.json</c>
    /// <c>scripts</c> values). Detects tools invoked only via a script, such as <c>bun test</c>.</summary>
    public List<string> ScriptsRegex { get; set; } = [];
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

/// <summary>
/// An infrastructure signal definition from <c>infra.yml</c>. A file matches when
/// every <em>specified</em> constraint category holds (AND across categories):
/// the name category (any of <see cref="Files"/> / <see cref="NamePrefix"/> /
/// <see cref="NameSuffix"/>), <see cref="Extensions"/>, <see cref="PathContains"/>,
/// and the optional <see cref="RequiresContent"/> sniff. Order in the file is the
/// match precedence — the first matching signal claims a file.
/// </summary>
public sealed class InfraSignalDef
{
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "library";
    public List<string> Files { get; set; } = [];        // exact file name
    public List<string> NamePrefix { get; set; } = [];   // file name starts with
    public List<string> NameSuffix { get; set; } = [];   // file name ends with
    public List<string> Extensions { get; set; } = [];   // file extension (with dot)
    public List<string> PathContains { get; set; } = []; // relative path contains
    public string? RequiresContent { get; set; }         // content sniff key, e.g. "k8s"
    public bool ScanComposeImages { get; set; }          // also extract `image:` values
}

/// <summary>A container image -> reported technology mapping from <c>infra.yml</c>.</summary>
public sealed class InfraImageDef
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "database";
}

/// <summary>The contents of <c>infra.yml</c>.</summary>
public sealed class InfraData
{
    public List<InfraSignalDef> Signals { get; set; } = [];
    public Dictionary<string, InfraImageDef> Images { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
