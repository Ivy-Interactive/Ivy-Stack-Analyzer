using System.Text;
using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Scanning;

/// <summary>
/// A pragmatic <c>.gitignore</c> matcher. Supports the common subset: comments,
/// negation (<c>!</c>), directory-only rules (trailing <c>/</c>), anchored rules
/// (leading <c>/</c>), <c>**</c>, <c>*</c>, <c>?</c>, and basename matching.
/// Patterns are resolved relative to the directory of the <c>.gitignore</c> that
/// declared them. Last matching rule wins (git semantics).
/// </summary>
public sealed class GitignoreMatcher
{
    private readonly List<Rule> _rules = [];

    private sealed record Rule(Regex Regex, bool Negated, bool DirOnly);

    /// <summary>Add the patterns from one <c>.gitignore</c> located at <paramref name="baseDir"/> (relative to repo root, forward slashes, no trailing slash; "" for root).</summary>
    public void AddFile(string baseDir, string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var negated = line.StartsWith('!');
            if (negated) line = line[1..];
            if (line.Length == 0) continue;

            var dirOnly = line.EndsWith('/');
            if (dirOnly) line = line[..^1];

            var anchored = line.StartsWith('/') || line.Contains('/');
            line = line.TrimStart('/');
            if (line.Length == 0) continue;

            var regex = BuildRegex(baseDir, line, anchored);
            _rules.Add(new Rule(regex, negated, dirOnly));
        }
    }

    /// <summary>Whether the given repo-relative path (forward slashes) is ignored.</summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        bool ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirOnly && !isDirectory) continue;
            if (rule.Regex.IsMatch(relativePath))
                ignored = !rule.Negated;
        }
        return ignored;
    }

    public bool HasRules => _rules.Count > 0;

    private static Regex BuildRegex(string baseDir, string pattern, bool anchored)
    {
        var prefix = baseDir.Length == 0 ? "" : baseDir + "/";
        var sb = new StringBuilder();
        sb.Append('^');
        sb.Append(Regex.Escape(prefix).Replace("\\/", "/"));

        if (!anchored)
            sb.Append("(?:.*/)?"); // match in any subdirectory

        sb.Append(GlobToRegex(pattern));
        // match the path itself or anything beneath it
        sb.Append("(?:/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;
                        if (i + 1 < glob.Length && glob[i + 1] == '/') { i++; sb.Append("(?:.*/)?"); }
                        else sb.Append(".*");
                    }
                    else sb.Append("[^/]*");
                    break;
                case '?': sb.Append("[^/]"); break;
                case '.': case '(': case ')': case '+': case '|':
                case '^': case '$': case '{': case '}': case '[': case ']': case '\\':
                    sb.Append('\\').Append(c); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
