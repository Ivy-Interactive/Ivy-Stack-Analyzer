using Ivy.StackAnalyzer.Scanning;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class GitignoreMatcherTests
{
    private static GitignoreMatcher Root(string content)
    {
        var m = new GitignoreMatcher();
        m.AddFile("", content);
        return m;
    }

    [Fact]
    public void Directory_rule_matches_directory()
    {
        var m = Root("node_modules/\n");
        Assert.True(m.IsIgnored("node_modules", isDirectory: true));
        Assert.True(m.IsIgnored("packages/x/node_modules", isDirectory: true)); // unanchored
    }

    [Fact]
    public void Directory_only_rule_does_not_match_a_file()
    {
        var m = Root("build/\n");
        Assert.False(m.IsIgnored("build", isDirectory: false));
    }

    [Fact]
    public void File_glob_matches()
    {
        var m = Root("*.log\n");
        Assert.True(m.IsIgnored("debug.log", isDirectory: false));
        Assert.True(m.IsIgnored("logs/app.log", isDirectory: false)); // unanchored, any depth
        Assert.False(m.IsIgnored("debug.txt", isDirectory: false));
    }

    [Fact]
    public void Negation_reincludes()
    {
        var m = Root("*.log\n!keep.log\n");
        Assert.True(m.IsIgnored("a.log", isDirectory: false));
        Assert.False(m.IsIgnored("keep.log", isDirectory: false));
    }

    [Fact]
    public void Anchored_rule_only_matches_at_base()
    {
        var m = Root("/build\n");
        Assert.True(m.IsIgnored("build", isDirectory: true));
        Assert.False(m.IsIgnored("src/build", isDirectory: true));
    }

    [Fact]
    public void Comments_and_blanks_ignored()
    {
        var m = Root("# a comment\n\n   \n*.tmp\n");
        Assert.True(m.IsIgnored("x.tmp", isDirectory: false));
        Assert.False(m.HasRules == false); // sanity: it did parse a rule
    }

    [Fact]
    public void Nested_gitignore_scoped_to_its_directory()
    {
        var m = new GitignoreMatcher();
        m.AddFile("packages/app", "dist/\n");
        Assert.True(m.IsIgnored("packages/app/dist", isDirectory: true));
        Assert.False(m.IsIgnored("dist", isDirectory: true));
    }

    [Fact]
    public void Nested_basename_rule_does_not_leak_to_siblings()
    {
        var m = new GitignoreMatcher();
        m.AddFile("a", "*.log\n");                                   // non-anchored, in subtree "a"
        Assert.True(m.IsIgnored("a/x.log", isDirectory: false));
        Assert.True(m.IsIgnored("a/deep/x.log", isDirectory: false)); // applies within its subtree
        Assert.False(m.IsIgnored("b/x.log", isDirectory: false));     // sibling unaffected
    }
}
