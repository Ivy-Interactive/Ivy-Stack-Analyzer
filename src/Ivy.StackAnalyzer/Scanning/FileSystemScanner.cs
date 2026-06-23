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
        // .gitattributes linguist overrides (the repo's own language-stat hints):
        // `linguist-vendored`/`-generated` exclude like vendored; `-documentation`
        // flags as docs. Reuses the gitignore glob engine (same pattern syntax).
        var attrVendored = new GitignoreMatcher();
        var attrDocumentation = new GitignoreMatcher();
        int total = 0;

        Walk(repoRoot, repoRoot, gitignore, attrVendored, attrDocumentation, files, ignoredDirs, ref total, ct);

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
        GitignoreMatcher attrVendored, GitignoreMatcher attrDocumentation,
        List<ScannedFile> files, SortedSet<string> ignoredDirs,
        ref int total, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var dirRel = ToRelative(root, dir);
        if (dirRel == ".") dirRel = "";

        // Load this directory's .gitignore before descending. At the repo root
        // ToRelative yields "." — normalize to "" so patterns aren't prefixed "./".
        if (_options.RespectGitignore)
        {
            var giPath = Path.Combine(dir, ".gitignore");
            if (File.Exists(giPath))
            {
                try { gitignore.AddFile(dirRel, File.ReadAllText(giPath)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* skip unreadable .gitignore */ }
            }
        }

        // .gitattributes is independent of .gitignore (read regardless).
        var gaPath = Path.Combine(dir, ".gitattributes");
        if (File.Exists(gaPath))
        {
            try { AddGitAttributes(dirRel, File.ReadAllText(gaPath), attrVendored, attrDocumentation); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* skip unreadable .gitattributes */ }
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

            // vendored: vendor.yml patterns OR a .gitattributes linguist-vendored/-generated override.
            bool vendored = IsVendored(rel) || attrVendored.IsIgnored(rel, isDirectory: false);
            if (vendored && !_options.IncludeVendored)
            {
                // counted as total, kept (flagged) so downstream can exclude from stats
            }

            // Documentation / example files are flagged but NEVER pruned from the
            // walk: example dirs must still surface as components. The flag only
            // excludes them from language statistics downstream.
            bool documentation = IsDocumentation(rel) || attrDocumentation.IsIgnored(rel, isDirectory: false);

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

            Walk(sub, root, gitignore, attrVendored, attrDocumentation, files, ignoredDirs, ref total, ct);
        }
    }

    // Parse a .gitattributes file: `pattern attr1 attr2 ...`. Positive linguist
    // overrides feed the matchers; negated/`=false` forms are ignored (we never
    // force-exclude on a negative). Pattern syntax matches .gitignore globs.
    private static void AddGitAttributes(
        string baseDir, string content, GitignoreMatcher vendored, GitignoreMatcher documentation)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var sp = line.IndexOfAny([' ', '\t']);
            if (sp < 0) continue;
            var pattern = line[..sp];
            var attrs = line[sp..];
            if (HasAttr(attrs, "linguist-vendored") || HasAttr(attrs, "linguist-generated"))
                vendored.AddFile(baseDir, pattern);
            if (HasAttr(attrs, "linguist-documentation"))
                documentation.AddFile(baseDir, pattern);
        }
    }

    private static bool HasAttr(string attrs, string name)
    {
        foreach (var tok in attrs.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (tok == name || tok == name + "=true") return true; // positive only
        return false;
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
