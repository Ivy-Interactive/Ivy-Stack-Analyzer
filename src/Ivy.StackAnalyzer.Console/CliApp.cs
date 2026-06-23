using Ivy.StackAnalyzer.Console.Commands;
using Spectre.Console.Cli;

namespace Ivy.StackAnalyzer.Console;

/// <summary>Builds the configured Spectre app. <see cref="AnalyzeCommand"/> is the
/// default command, so usage is <c>ivy-stack-analyzer &lt;path&gt; [options]</c>
/// (no verb). Shared by the entry point and the in-process end-to-end test so both
/// exercise identical wiring.</summary>
public static class CliApp
{
    public static CommandApp<AnalyzeCommand> Build()
    {
        var app = new CommandApp<AnalyzeCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("ivy-stack-analyzer");
            config.AddExample("./my-repo", "--output", "yaml");
        });
        return app;
    }
}
