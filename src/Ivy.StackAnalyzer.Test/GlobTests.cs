using Ivy.StackAnalyzer.Detection;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class GlobTests
{
    [Theory]
    [InlineData("App.xaml", "*.xaml", true)]
    [InlineData("src/App.xaml", "*.xaml", false)]           // * does not cross /
    [InlineData("x.cs", "**/*.cs", true)]
    [InlineData("src/a/b.cs", "**/*.cs", true)]
    [InlineData("src/b.ts", "src/**/*.ts", true)]
    [InlineData("src/a/b.ts", "src/**/*.ts", true)]
    [InlineData("lib/a.ts", "src/**/*.ts", false)]
    [InlineData("a/foo/b/c", "**/foo/**", true)]
    [InlineData("foo/x", "**/foo/**", true)]
    [InlineData("x/fooo/y", "**/foo/**", false)]
    [InlineData("abc", "a?c", true)]
    [InlineData("a/c", "a?c", false)]                       // ? does not cross /
    [InlineData("App.XAML", "*.xaml", true)]                // case-insensitive
    public void Matches(string path, string glob, bool expected)
        => Assert.Equal(expected, Glob.IsMatch(path, glob));
}
