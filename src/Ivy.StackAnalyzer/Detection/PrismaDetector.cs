using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Components;

namespace Ivy.StackAnalyzer.Detection;

/// <summary>
/// Surfaces the database engine declared in a Prisma schema's <c>datasource</c>
/// block (<c>provider = "postgresql"</c>). Common Prisma stacks have no raw driver
/// dependency and keep the connection string in an env var, so the DB is knowable
/// only from <c>schema.prisma</c> — which data rules cannot read into.
/// </summary>
public sealed partial class PrismaDetector : ITechnologyDetector
{
    public IEnumerable<DetectedTechnology> Detect(ComponentContext ctx)
    {
        foreach (var f in ctx.Files)
        {
            if (!string.Equals(f.File.FileName, "schema.prisma", StringComparison.OrdinalIgnoreCase))
                continue;
            string content;
            try { content = File.ReadAllText(f.File.FullPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var db = DatabaseFor(content);
            if (db is not null)
                yield return new DetectedTechnology(
                    db, TechCategory.Database,
                    $"prisma datasource provider in {f.File.RelativePath}",
                    Confidence.High, ctx.RelativePath);
        }
    }

    /// <summary>Maps the <c>provider</c> of a Prisma <c>datasource</c> block to a
    /// canonical database name, or null if absent / dynamic / unknown.</summary>
    public static string? DatabaseFor(string schemaPrismaContent)
    {
        // Scope to the datasource block — `generator` blocks also have a `provider`
        // (e.g. "prisma-client-js") that must not be mistaken for a database.
        var ds = DatasourceBlockRegex().Match(schemaPrismaContent);
        if (!ds.Success) return null;
        var prov = ProviderRegex().Match(ds.Groups["body"].Value);
        if (!prov.Success) return null;
        return prov.Groups["p"].Value.ToLowerInvariant() switch
        {
            "postgresql" => "PostgreSQL",
            "mysql" => "MySQL",
            "sqlite" => "SQLite",
            "sqlserver" => "Microsoft SQL Server",
            "mongodb" => "MongoDB",
            "cockroachdb" => "CockroachDB",
            _ => null,
        };
    }

    [GeneratedRegex(@"datasource\s+[A-Za-z0-9_]+\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex DatasourceBlockRegex();

    [GeneratedRegex(@"provider\s*=\s*""(?<p>[^""]+)""")]
    private static partial Regex ProviderRegex();
}
