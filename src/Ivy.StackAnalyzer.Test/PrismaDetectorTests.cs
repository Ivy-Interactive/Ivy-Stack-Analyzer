using Ivy.StackAnalyzer.Detection;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class PrismaDetectorTests
{
    [Theory]
    [InlineData("postgresql", "PostgreSQL")]
    [InlineData("mysql", "MySQL")]
    [InlineData("sqlite", "SQLite")]
    [InlineData("mongodb", "MongoDB")]
    public void Maps_datasource_provider_to_database(string provider, string expected)
    {
        var schema = $$"""
            datasource db {
              provider = "{{provider}}"
              url      = env("DATABASE_URL")
            }
            """;
        Assert.Equal(expected, PrismaDetector.DatabaseFor(schema));
    }

    [Fact]
    public void Ignores_generator_provider_and_dynamic_provider()
    {
        // A generator block's provider ("prisma-client-js") is not a database.
        Assert.Null(PrismaDetector.DatabaseFor("""generator client { provider = "prisma-client-js" }"""));
        // A provider read from an env var is not a literal engine.
        Assert.Null(PrismaDetector.DatabaseFor("""datasource db { provider = env("DB_PROVIDER") }"""));
    }
}
