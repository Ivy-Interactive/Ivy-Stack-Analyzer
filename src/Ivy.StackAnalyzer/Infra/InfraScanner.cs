using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Models;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Infra;

/// <summary>
/// Surfaces deployment / operational signals that live outside dependency
/// manifests: containers, orchestration, IaC, CI, and databases declared in
/// compose files. The match tables come from <c>infra.yml</c> (<see cref="InfraData"/>);
/// this class only provides the mechanics (walking files, parsing compose
/// <c>image:</c> values, sniffing Kubernetes manifests). Pure facts — no judgement.
/// </summary>
public sealed partial class InfraScanner
{
    private readonly InfraData _data;

    public InfraScanner(InfraData data) => _data = data;

    public IReadOnlyList<InfraSignal> Scan(IReadOnlyList<ClassifiedFile> files)
    {
        // One bucket per signal, kept in definition order = match precedence.
        var buckets = _data.Signals.Select(def => (Def: def, Paths: new List<string>())).ToList();
        var images = new Dictionary<string, (TechCategory Cat, SortedSet<string> Files)>(StringComparer.OrdinalIgnoreCase);

        foreach (var cf in files)
        {
            if (cf.File.IsVendored) continue;
            foreach (var (def, paths) in buckets)
            {
                if (!Matches(def, cf)) continue;
                paths.Add(cf.File.RelativePath);
                if (def.ScanComposeImages) ScanComposeImages(cf.File.FullPath, cf.File.RelativePath, images);
                break; // first matching signal claims the file
            }
        }

        var signals = new List<InfraSignal>();
        foreach (var (def, paths) in buckets)
        {
            if (paths.Count == 0) continue;
            paths.Sort(StringComparer.Ordinal);
            signals.Add(new InfraSignal(def.Kind, ParseCategory(def.Category), paths, null));
        }

        foreach (var (tech, info) in images.OrderBy(k => k.Key, StringComparer.Ordinal))
            signals.Add(new InfraSignal(tech, info.Cat, info.Files.ToList(), $"docker-compose image '{tech}'"));

        return signals;
    }

    private static bool Matches(InfraSignalDef def, ClassifiedFile cf)
    {
        var name = cf.File.FileName;
        var ext = cf.File.Extension;
        var path = cf.File.RelativePath;

        // name category — OR of exact / prefix / suffix, only enforced when specified
        if (def.Files.Count > 0 || def.NamePrefix.Count > 0 || def.NameSuffix.Count > 0)
        {
            bool nameOk = def.Files.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase))
                || def.NamePrefix.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || def.NameSuffix.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (!nameOk) return false;
        }

        if (def.Extensions.Count > 0
            && !def.Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (def.PathContains.Count > 0
            && !def.PathContains.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (!string.IsNullOrEmpty(def.RequiresContent) && !ContentMatches(def.RequiresContent, cf.File.FullPath))
            return false;

        return true;
    }

    private static bool ContentMatches(string key, string fullPath) => key.ToLowerInvariant() switch
    {
        "k8s" => IsKubernetesContent(fullPath),
        _ => true, // unknown sniff key: don't block on content we can't verify
    };

    private static bool IsKubernetesContent(string fullPath)
    {
        try
        {
            var head = File.ReadLines(fullPath).Take(40).ToList();
            return head.Any(l => l.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
                && head.Any(l => l.StartsWith("apiVersion:", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return false; }
    }

    private void ScanComposeImages(string fullPath, string relPath,
        Dictionary<string, (TechCategory Cat, SortedSet<string> Files)> images)
    {
        string content;
        try { content = File.ReadAllText(fullPath); }
        catch (IOException) { return; }

        foreach (Match m in ImageRegex().Matches(content))
        {
            var imageName = m.Groups["img"].Value.Trim().Split(':')[0];
            if (!_data.Images.TryGetValue(imageName, out var def))
            {
                // try last path segment (e.g. "bitnami/postgresql" -> "postgresql")
                var leaf = imageName.Split('/').Last();
                if (!_data.Images.TryGetValue(leaf, out def)) continue;
            }
            var cat = ParseCategory(def.Category);
            if (!images.TryGetValue(def.Name, out var entry))
                entry = images[def.Name] = (cat, new SortedSet<string>(StringComparer.Ordinal));
            entry.Files.Add(relPath);
        }
    }

    // infra.yml categories are written to match TechCategory names directly, so a
    // plain enum parse is correct here (and avoids CategoryMap's specfy-oriented
    // bucketing, e.g. "tool" -> Build).
    private static TechCategory ParseCategory(string category)
        => Enum.TryParse<TechCategory>(category, ignoreCase: true, out var c) ? c : TechCategory.Library;

    [GeneratedRegex(@"image:\s*[""']?(?<img>[\w.\-\/]+(?::[\w.\-]+)?)[""']?")]
    private static partial Regex ImageRegex();
}
