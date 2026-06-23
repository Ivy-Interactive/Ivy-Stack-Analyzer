using Ivy.StackAnalyzer.Console;
using YamlDotNet.Serialization;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

/// <summary>Drives the Console <c>analyze</c> command in-process and checks the YAML round-trips.</summary>
public class ConsoleE2ETests
{
    private static string FixturePath(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Analyze_command_emits_parseable_yaml()
    {
        var app = CliApp.Build(); // exact same configuration as the real entry point

        var originalOut = System.Console.Out;
        var buffer = new StringWriter();
        System.Console.SetOut(buffer);
        int exit;
        try
        {
            // Intentional synchronous in-process CLI invocation (no token needed).
#pragma warning disable xUnit1051
            exit = app.Run(["analyze", FixturePath("go-service"), "--output", "yaml"]);
#pragma warning restore xUnit1051
        }
        finally
        {
            System.Console.SetOut(originalOut);
        }

        Assert.Equal(0, exit);
        var yaml = buffer.ToString();

        var deserializer = new DeserializerBuilder().Build();
        var model = deserializer.Deserialize<Dictionary<string, object>>(yaml);

        Assert.NotNull(model);
        Assert.True(model!.ContainsKey("summary"));
        Assert.True(model.ContainsKey("languages"));
        Assert.True(model.ContainsKey("components"));
        Assert.True(model.ContainsKey("technologies"));
        Assert.Contains("Gin", yaml); // the gin-gonic framework detector
        Assert.Contains("Docker", yaml); // Dockerfile infra signal
    }
}
