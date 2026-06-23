namespace Ivy.StackAnalyzer.Scanning;

/// <summary>Aggregates classified files into <see cref="LanguageStat"/> lists.</summary>
public static class LanguageAggregator
{
    /// <summary>
    /// Build language statistics over the given classified files. Vendored,
    /// documentation/example, and unclassified files are excluded; prose and data
    /// languages are excluded entirely (rows and denominator) for github-linguist
    /// parity, so percent is the share of programming + markup bytes.
    /// Result is ordered by bytes descending, then name.
    /// </summary>
    public static IReadOnlyList<LanguageStat> Aggregate(IEnumerable<ClassifiedFile> files)
    {
        var byLang = new Dictionary<string, (LanguageType Type, int Files, long Bytes)>();
        long totalBytes = 0;

        foreach (var f in files)
        {
            if (f.Language is null || f.Type is null || f.File.IsVendored || f.File.IsDocumentation) continue;
            // github-linguist parity: prose (Markdown/Text) and data (JSON/XML/YAML/TOML)
            // do not count toward language statistics. Programming + markup only.
            if (f.Type is LanguageType.Prose or LanguageType.Data) continue;
            var cur = byLang.TryGetValue(f.Language, out var v) ? v : (f.Type.Value, 0, 0L);
            byLang[f.Language] = (f.Type.Value, cur.Item2 + 1, cur.Item3 + f.File.Length);
            totalBytes += f.File.Length;
        }

        return byLang
            .Select(kv => new LanguageStat(
                kv.Key, kv.Value.Type, kv.Value.Files, kv.Value.Bytes,
                totalBytes == 0 ? 0 : Math.Round(kv.Value.Bytes * 100.0 / totalBytes, 1)))
            .OrderByDescending(s => s.Bytes)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Top programming languages by byte share, for the summary.</summary>
    public static IReadOnlyList<string> PrimaryLanguages(IReadOnlyList<LanguageStat> stats, int max = 4)
        => stats
            .Where(s => s.Type == LanguageType.Programming)
            .Take(max)
            .Select(s => s.Name)
            .ToList();

    /// <summary>
    /// The single dominant language of a component / the repo, used for the
    /// language slot consumed by the hash. github-linguist parity: the dominant
    /// pick is restricted to <see cref="LanguageType.Programming"/> so a data
    /// (JSON) or prose (Text) language can never out-rank the real code language.
    /// Returns <c>null</c> when no programming language is present.
    /// </summary>
    public static string? DominantProgrammingLanguage(IReadOnlyList<LanguageStat> stats)
        => stats
            .Where(s => s.Type == LanguageType.Programming)
            .OrderByDescending(s => s.Bytes)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => s.Name)
            .FirstOrDefault();
}
