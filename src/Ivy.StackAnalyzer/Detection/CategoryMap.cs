namespace Ivy.StackAnalyzer.Detection;

/// <summary>Maps the lowercase category strings used in data files to <see cref="TechCategory"/>.</summary>
public static class CategoryMap
{
    public static TechCategory Parse(string category) => category.ToLowerInvariant() switch
    {
        "language" => TechCategory.Language,
        "framework" => TechCategory.Framework,
        "library" => TechCategory.Library,
        "runtime" => TechCategory.Runtime,
        "database" or "db" => TechCategory.Database,
        "orm" => TechCategory.Orm,
        "styling" or "ui" => TechCategory.Styling,
        "build" or "builder" or "automation" or "linter" or "tool" => TechCategory.Build,
        "ci" => TechCategory.Ci,
        "cloud" => TechCategory.Cloud,
        "messaging" => TechCategory.Messaging,
        "testing" or "test" => TechCategory.Testing,
        "packagemanager" or "package_manager" => TechCategory.PackageManager,
        "ai" => TechCategory.Ai,
        "hosting" => TechCategory.Hosting,
        "analytics" => TechCategory.Analytics,
        "auth" => TechCategory.Auth,
        "cms" => TechCategory.Cms,
        "monitoring" => TechCategory.Monitoring,
        "security" => TechCategory.Security,
        "storage" => TechCategory.Storage,
        "payment" => TechCategory.Payment,
        "queue" => TechCategory.Queue,
        "saas" => TechCategory.Saas,
        "iac" => TechCategory.Iac,
        "documentation" => TechCategory.Documentation,
        _ => TechCategory.Library,
    };

    public static Confidence ParseConfidence(string c) => c.ToLowerInvariant() switch
    {
        "low" => Confidence.Low,
        "medium" => Confidence.Medium,
        _ => Confidence.High,
    };
}
