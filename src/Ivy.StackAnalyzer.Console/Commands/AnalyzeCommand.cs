using System.ComponentModel;
using Ivy.StackAnalyzer;
using Ivy.StackAnalyzer.Serialization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.StackAnalyzer.Console.Commands;

/// <summary>
/// <c>ivy-stack-analyzer &lt;path&gt;</c> — produce a deterministic stack report
/// of a repository as YAML (default) or JSON.
/// </summary>
public sealed class AnalyzeCommand : AsyncCommand<AnalyzeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Path to the repository to analyze.")]
        public string Path { get; init; } = ".";

        [CommandOption("-o|--output <FORMAT>")]
        [Description("Output format: yaml or json.")]
        [DefaultValue("yaml")]
        public string Output { get; init; } = "yaml";

        [CommandOption("--out <FILE>")]
        [Description("Write output to a file instead of stdout.")]
        public string? OutFile { get; init; }

        [CommandOption("--rules <DIR>")]
        [Description("Extra directory of detector / language definitions (repeatable).")]
        public string[] Rules { get; init; } = [];

        [CommandOption("--include-vendored")]
        [Description("Include vendored / generated directories in the report.")]
        public bool IncludeVendored { get; init; }

        [CommandOption("--no-gitignore")]
        [Description("Do not honor .gitignore rules while walking.")]
        public bool NoGitignore { get; init; }

        public override Spectre.Console.ValidationResult Validate()
        {
            if (!Directory.Exists(Path))
                return Spectre.Console.ValidationResult.Error($"Path not found: {Path}");
            if (!string.Equals(Output, "yaml", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Output, "json", StringComparison.OrdinalIgnoreCase))
                return Spectre.Console.ValidationResult.Error("--output must be 'yaml' or 'json'.");
            return Spectre.Console.ValidationResult.Success();
        }
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var options = new AnalyzerOptions
        {
            IncludeVendored = settings.IncludeVendored,
            RespectGitignore = !settings.NoGitignore,
            AdditionalRuleDirectories = settings.Rules,
        };

        var format = string.Equals(settings.Output, "json", StringComparison.OrdinalIgnoreCase)
            ? OutputFormat.Json
            : OutputFormat.Yaml;

        try
        {
            var analyzer = new Analyzer(options);
            var detection = await analyzer.AnalyzeAsync(settings.Path, cancellationToken);
            var text = StackSerializer.Serialize(detection, format);

            if (settings.OutFile is { Length: > 0 } file)
            {
                await File.WriteAllTextAsync(file, text);
                AnsiConsole.MarkupLineInterpolated(
                    $"[green]Wrote[/] {detection.Components.Count} component(s), {detection.Technologies.Count} technolog(ies) to [blue]{file}[/]");
            }
            else
            {
                // Raw stdout so the output stays machine-parseable.
                System.Console.Out.Write(text);
                if (!text.EndsWith('\n')) System.Console.Out.Write('\n');
            }
            return 0;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or ArgumentException)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {ex.Message}");
            return 1;
        }
    }
}
