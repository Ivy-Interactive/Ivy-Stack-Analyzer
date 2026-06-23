using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Ivy.StackAnalyzer.Detection;

/// <summary>Minimal glob matcher supporting <c>**</c>, <c>*</c>, and <c>?</c> over forward-slash paths.</summary>
public static class Glob
{
    // Process-wide cache shared across concurrent Analyzer runs; must be thread-safe.
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    public static bool IsMatch(string path, string glob)
        => Cache.GetOrAdd(glob, g => new Regex(Compile(g), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .IsMatch(path);

    private static string Compile(string glob)
    {
        var sb = new StringBuilder("^");
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
                case '.': case '(': case ')': case '+': case '|': case '^':
                case '$': case '{': case '}': case '[': case ']': case '\\':
                    sb.Append('\\').Append(c); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
