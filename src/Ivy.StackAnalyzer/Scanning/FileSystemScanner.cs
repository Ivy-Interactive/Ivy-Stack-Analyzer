using Ivy.StackAnalyzer.Models;

namespace Ivy.StackAnalyzer.Scanning;

/// <summary>
/// Walks a repository depth-first, pruning vendored / generated directories
/// (from <c>vendor.yml</c>) and, optionally, <c>.gitignore</c>d paths. The walk
/// is deterministic: entries are visited in ordinal name order.
/// </summary>
public sealed class FileSystemScanner
{
    private readonly DataStore _data;
    private readonly AnalyzerOptions _options;

    public FileSystemScanner(DataStore data, AnalyzerOptions options)
    {
        _data = data;
        _options = options;
    }

    public ScanResult Scan(string repoRoot, CancellationToken ct = default)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        var files = new List<ScannedFile>();
        var ignoredDirs = new SortedSet<string>(StringComparer.Ordinal);
        var gitignore = new GitignoreMatcher();
        int total = 0;

        Walk(repoRoot, repoRoot, gitignore, files, ignoredDirs, ref total, ct);

        files.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return new ScanResult
        {
            Files = files,
            TotalFiles = total,
            IgnoredDirectories = ignoredDirs.ToList(),
        };
    }

    private void Walk(
        string dir, string root, GitignoreMatcher gitignore,
        List<ScannedFile> files, SortedSet<string> ignoredDirs,
        ref int total, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Load this directory's .gitignore before descending. At the repo root
        // ToRelative yields "." — normalize to "" so patterns aren't prefixed "./".
        if (_options.RespectGitignore)
        {
            var giPath = Path.Combine(dir, ".gitignore");
            if (File.Exists(giPath))
            {
                var baseDir = ToRelative(root, dir);
                if (baseDir == ".") baseDir = "";
                try { gitignore.AddFile(baseDir, File.ReadAllText(giPath)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* skip unreadable .gitignore */ }
            }
        }

        string[] subDirs, dirFiles;
        try
        {
            subDirs = Directory.GetDirectories(dir);
            dirFiles = Directory.GetFiles(dir);
        }
        // One unreadable directory must never abort the whole scan.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return; }

        Array.Sort(subDirs, StringComparer.Ordinal);
        Array.Sort(dirFiles, StringComparer.Ordinal);

        foreach (var file in dirFiles)
        {
            ct.ThrowIfCancellationRequested();
            var rel = ToRelative(root, file);
            if (_options.RespectGitignore && gitignore.IsIgnored(rel, isDirectory: false)) continue;

            bool vendored = IsVendored(rel);
            if (vendored && !_options.IncludeVendored)
            {
                // counted as total, kept (flagged) so downstream can exclude from stats
            }

            // Documentation / example files are flagged but NEVER pruned from the
            // walk: example dirs must still surface as components. The flag only
            // excludes them from language statistics downstream.
            bool documentation = IsDocumentation(rel);

            long len;
            try { len = new FileInfo(file).Length; } catch { len = 0; }

            files.Add(new ScannedFile
            {
                RelativePath = rel,
                FullPath = file,
                Length = len,
                IsVendored = vendored,
                IsDocumentation = documentation,
            });
            total++;
        }

        foreach (var sub in subDirs)
        {
            var rel = ToRelative(root, sub);
            var name = Path.GetFileName(sub);

            if (name == ".git") { ignoredDirs.Add(rel); continue; }

            // Skip symlinks / junctions (reparse points) to avoid cycles -> stack overflow.
            try
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) { ignoredDirs.Add(rel); continue; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            if (!_options.IncludeVendored && IsVendored(rel + "/"))
            {
                ignoredDirs.Add(rel);
                continue;
            }
            if (_options.RespectGitignore && gitignore.IsIgnored(rel, isDirectory: true))
            {
                ignoredDirs.Add(rel);
                continue;
            }

            Walk(sub, root, gitignore, files, ignoredDirs, ref total, ct);
        }
    }

    private bool IsVendored(string relativePath)
    {
        foreach (var rx in _data.VendorPatterns)
            if (rx.IsMatch(relativePath)) return true;
        return false;
    }

    private bool IsDocumentation(string relativePath)
    {
        foreach (var rx in _data.DocumentationPatterns)
            if (rx.IsMatch(relativePath)) return true;
        return false;
    }

    private static string ToRelative(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Replace('\\', '/');
    }
}
