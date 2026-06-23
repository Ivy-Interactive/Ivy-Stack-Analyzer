using Ivy.StackAnalyzer.Console.Commands;
using Spectre.Console.Cli;

namespace Ivy.StackAnalyzer.Console;

/// <summary>Builds the configured Spectre <see cref="CommandApp"/>. Shared by the
/// entry point and the in-process end-to-end test so both exercise identical wiring.</summary>
public static class CliApp
{
    public static CommandApp Build()
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("ivy-stack");
            config.AddCommand<AnalyzeCommand>("analyze")
                .WithDescription("Analyze a repository and emit a deterministic stack report.")
                .WithExample("analyze", "./my-repo", "--output", "yaml");
        });
        return app;
    }
}
