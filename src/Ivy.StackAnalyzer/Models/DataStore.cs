using System.Reflection;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.StackAnalyzer.Models;

/// <summary>
/// Loads and indexes the data files (languages, vendor patterns, detector
/// rules, infrastructure signals) from embedded resources plus any user-supplied
/// rule directories.
/// This is the single source of "what does the tool know" — adding a language
/// or framework is purely a data change, never a code change.
/// </summary>
public sealed class DataStore
{
    public IReadOnlyDictionary<string, LanguageDef> Languages { get; }
    public IReadOnlyList<Regex> VendorPatterns { get; }

    /// <summary>
    /// Documentation / example / sample path patterns (mirrors github-linguist
    /// <c>documentation.yml</c>). Matching files are flagged but NOT pruned from
    /// the walk; they are excluded only from language statistics.
    /// </summary>
    public IReadOnlyList<Regex> DocumentationPatterns { get; }
    public IReadOnlyList<RuleDef> Rules { get; }
    public InfraData Infra { get; }

    // Indexes built from Languages for fast per-file classification.
    public IReadOnlyDictionary<string, List<string>> ByExtension { get; }
    public IReadOnlyDictionary<string, string> ByFilename { get; }
    public IReadOnlyDictionary<string, string> ByInterpreter { get; }

    public int LanguageDefsLoaded => Languages.Count;
    public int RulesLoaded => Rules.Count;

    private DataStore(
        Dictionary<string, LanguageDef> languages,
        List<Regex> vendorPatterns,
        List<Regex> documentationPatterns,
        List<RuleDef> rules,
        InfraData infra)
    {
        Languages = languages;
        VendorPatterns = vendorPatterns;
        DocumentationPatterns = documentationPatterns;
        Rules = rules;
        Infra = infra;

        var byExt = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byInterp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, def) in languages)
        {
            foreach (var ext in def.Extensions)
            {
                var key = ext.StartsWith('.') ? ext : "." + ext;
                (byExt.TryGetValue(key, out var list) ? list : byExt[key] = []).Add(name);
            }
            foreach (var fn in def.Filenames) byFile.TryAdd(fn, name);
            foreach (var ip in def.Interpreters) byInterp.TryAdd(ip, name);
        }
        ByExtension = byExt;
        ByFilename = byFile;
        ByInterpreter = byInterp;
    }

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>Load the built-in data, optionally overlaid with user directories.</summary>
    public static DataStore Load(IReadOnlyList<string>? additionalDirectories = null)
    {
        var asm = typeof(DataStore).Assembly;

        var languages = LoadLanguages(asm);
        var vendor = LoadVendor(asm);
        var documentation = LoadDocumentation(asm);
        var rules = LoadRules(asm);
        var infra = LoadInfra(asm);

        // Overlay user-supplied directories (later wins / appends). A malformed
        // user file is skipped rather than aborting the whole run.
        foreach (var dir in additionalDirectories ?? [])
        {
            if (!Directory.Exists(dir)) continue;

            var langFile = Path.Combine(dir, "languages.yml");
            if (File.Exists(langFile))
                foreach (var (k, v) in TryDeserialize<Dictionary<string, LanguageDef>>(langFile) ?? [])
                    languages[k] = v;

            var vendorFile = Path.Combine(dir, "vendor.yml");
            if (File.Exists(vendorFile))
                foreach (var p in (TryDeserialize<VendorFile>(vendorFile)?.Patterns ?? []))
                {
                    try { vendor.Add(new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant)); }
                    catch (ArgumentException) { /* skip malformed user pattern */ }
                }

            var documentationFile = Path.Combine(dir, "documentation.yml");
            if (File.Exists(documentationFile))
                foreach (var p in (TryDeserialize<VendorFile>(documentationFile)?.Patterns ?? []))
                {
                    try { documentation.Add(new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant)); }
                    catch (ArgumentException) { /* skip malformed user pattern */ }
                }

            var detectorsDir = Path.Combine(dir, "detectors");
            if (Directory.Exists(detectorsDir))
                foreach (var f in Directory.EnumerateFiles(detectorsDir, "*.yml"))
                    rules.AddRange(TryDeserialize<List<RuleDef>>(f) ?? []);

            var infraFile = Path.Combine(dir, "infra.yml");
            if (File.Exists(infraFile))
            {
                var userInfra = TryDeserialize<InfraData>(infraFile);
                if (userInfra is not null)
                {
                    infra.Signals.AddRange(userInfra.Signals);                   // append: built-ins keep precedence
                    foreach (var (k, v) in userInfra.Images) infra.Images[k] = v; // image mappings: user wins
                }
            }
        }

        return new DataStore(languages, vendor, documentation, rules, infra);
    }

    private static T? TryDeserialize<T>(string path) where T : class
    {
        try { return Yaml.Deserialize<T>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static InfraData LoadInfra(Assembly asm)
    {
        var text = ReadResource(asm, ".data.infra.yml");
        var data = Yaml.Deserialize<InfraData>(text) ?? new InfraData();
        // YamlDotNet replaces the dictionary, dropping the case-insensitive comparer.
        data.Images = new Dictionary<string, InfraImageDef>(data.Images, StringComparer.OrdinalIgnoreCase);
        return data;
    }

    private static Dictionary<string, LanguageDef> LoadLanguages(Assembly asm)
    {
        var text = ReadResource(asm, ".data.languages.yml");
        return Yaml.Deserialize<Dictionary<string, LanguageDef>>(text)
            ?? new Dictionary<string, LanguageDef>();
    }

    private static List<Regex> LoadVendor(Assembly asm)
    {
        var text = ReadResource(asm, ".data.vendor.yml");
        var file = Yaml.Deserialize<VendorFile>(text);
        var list = new List<Regex>();
        foreach (var p in file?.Patterns ?? [])
        {
            try { list.Add(new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant)); }
            catch (ArgumentException) { /* skip malformed pattern */ }
        }
        return list;
    }

    private static List<Regex> LoadDocumentation(Assembly asm)
    {
        var text = ReadResource(asm, ".data.documentation.yml");
        var file = Yaml.Deserialize<VendorFile>(text);
        var list = new List<Regex>();
        foreach (var p in file?.Patterns ?? [])
        {
            try { list.Add(new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant)); }
            catch (ArgumentException) { /* skip malformed pattern */ }
        }
        return list;
    }

    private static List<RuleDef> LoadRules(Assembly asm)
    {
        var rules = new List<RuleDef>();
        foreach (var resName in asm.GetManifestResourceNames())
        {
            if (!resName.Contains(".data.detectors.") || !resName.EndsWith(".yml")) continue;
            var text = ReadResourceByName(asm, resName);
            var parsed = Yaml.Deserialize<List<RuleDef>>(text);
            if (parsed != null) rules.AddRange(parsed);
        }
        return rules;
    }

    private static string ReadResource(Assembly asm, string suffix)
    {
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix))
            ?? throw new InvalidOperationException($"Embedded resource ending with '{suffix}' not found.");
        return ReadResourceByName(asm, name);
    }

    private static string ReadResourceByName(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource '{name}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
