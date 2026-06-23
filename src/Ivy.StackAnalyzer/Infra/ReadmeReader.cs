using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Infra;

/// <summary>Captures a bounded excerpt of the repository's top-level README.</summary>
public static class ReadmeReader
{
    private static readonly string[] Candidates =
        ["README.md", "README.markdown", "README.rst", "README.txt", "README"];

    public static ReadmeExcerpt? Read(IReadOnlyList<ClassifiedFile> files, int maxLines)
    {
        // Prefer a README at the shallowest path, matching the preferred name order.
        ScannedFile? best = null;
        int bestRank = int.MaxValue, bestDepth = int.MaxValue;
        foreach (var cf in files)
        {
            var name = cf.File.FileName;
            int rank = Array.FindIndex(Candidates, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (rank < 0) continue;
            int depth = cf.File.RelativePath.Count(ch => ch == '/');
            if (depth < bestDepth || (depth == bestDepth && rank < bestRank))
            {
                best = cf.File; bestDepth = depth; bestRank = rank;
            }
        }
        if (best is null) return null;

        try
        {
            var lines = File.ReadLines(best.FullPath).Take(maxLines).ToList();
            return new ReadmeExcerpt(best.RelativePath, string.Join("\n", lines).TrimEnd());
        }
        catch (IOException) { return null; }
    }
}
