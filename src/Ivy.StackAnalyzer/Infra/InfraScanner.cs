using System.Text.RegularExpressions;
using Ivy.StackAnalyzer.Scanning;

namespace Ivy.StackAnalyzer.Infra;

/// <summary>
/// Surfaces deployment / operational signals that live outside dependency
/// manifests: containers, orchestration, IaC, CI, and databases declared in
/// compose files. Pure facts — files and evidence, no judgement.
/// </summary>
public sealed partial class InfraScanner
{
    private sealed record DbImage(string Name, TechCategory Category);

    // docker image name (prefix before ':') -> reported technology
    private static readonly Dictionary<string, DbImage> KnownImages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["postgres"] = new("Postgres", TechCategory.Database),
        ["postgis/postgis"] = new("PostGIS", TechCategory.Database),
        ["mysql"] = new("MySQL", TechCategory.Database),
        ["mariadb"] = new("MariaDB", TechCategory.Database),
        ["mongo"] = new("MongoDB", TechCategory.Database),
        ["redis"] = new("Redis", TechCategory.Database),
        ["memcached"] = new("Memcached", TechCategory.Database),
        ["cassandra"] = new("Cassandra", TechCategory.Database),
        ["clickhouse/clickhouse-server"] = new("ClickHouse", TechCategory.Database),
        ["cockroachdb/cockroach"] = new("CockroachDB", TechCategory.Database),
        ["neo4j"] = new("Neo4j", TechCategory.Database),
        ["elasticsearch"] = new("Elasticsearch", TechCategory.Database),
        ["docker.elastic.co/elasticsearch/elasticsearch"] = new("Elasticsearch", TechCategory.Database),
        ["rabbitmq"] = new("RabbitMQ", TechCategory.Queue),
        ["confluentinc/cp-kafka"] = new("Kafka", TechCategory.Messaging),
        ["apache/kafka"] = new("Kafka", TechCategory.Messaging),
        ["nats"] = new("NATS", TechCategory.Messaging),
        ["minio/minio"] = new("MinIO", TechCategory.Storage),
    };

    public IReadOnlyList<InfraSignal> Scan(IReadOnlyList<ClassifiedFile> files)
    {
        var signals = new List<InfraSignal>();

        var docker = new List<string>();
        var compose = new List<string>();
        var k8s = new List<string>();
        var helm = new List<string>();
        var terraform = new List<string>();
        var ghActions = new List<string>();
        var gitlabCi = new List<string>();
        var azurePipelines = new List<string>();
        var jenkins = new List<string>();
        var circleci = new List<string>();
        var dbSignals = new Dictionary<string, (TechCategory Cat, SortedSet<string> Files)>(StringComparer.OrdinalIgnoreCase);

        foreach (var cf in files)
        {
            if (cf.File.IsVendored) continue;
            var path = cf.File.RelativePath;
            var name = cf.File.FileName;

            if (IsDockerfile(name)) docker.Add(path);
            else if (IsCompose(name)) { compose.Add(path); ScanCompose(cf.File.FullPath, path, dbSignals); }
            else if (path.EndsWith(".tf", StringComparison.OrdinalIgnoreCase)) terraform.Add(path);
            else if (string.Equals(name, "Chart.yaml", StringComparison.OrdinalIgnoreCase)) helm.Add(path);
            else if (path.Contains(".github/workflows/", StringComparison.OrdinalIgnoreCase)
                     && (name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)))
                ghActions.Add(path);
            else if (string.Equals(name, ".gitlab-ci.yml", StringComparison.OrdinalIgnoreCase)) gitlabCi.Add(path);
            else if (string.Equals(name, "azure-pipelines.yml", StringComparison.OrdinalIgnoreCase)) azurePipelines.Add(path);
            else if (string.Equals(name, "Jenkinsfile", StringComparison.OrdinalIgnoreCase)) jenkins.Add(path);
            else if (path.Contains(".circleci/", StringComparison.OrdinalIgnoreCase) && name.StartsWith("config", StringComparison.OrdinalIgnoreCase)) circleci.Add(path);
            else if (IsKubernetesManifest(cf)) k8s.Add(path);
        }

        AddSignal(signals, "Docker", TechCategory.Build, docker);
        AddSignal(signals, "Docker Compose", TechCategory.Build, compose);
        AddSignal(signals, "Kubernetes", TechCategory.Cloud, k8s);
        AddSignal(signals, "Helm", TechCategory.Cloud, helm);
        AddSignal(signals, "Terraform", TechCategory.Iac, terraform);
        AddSignal(signals, "GitHub Actions", TechCategory.Ci, ghActions);
        AddSignal(signals, "GitLab CI", TechCategory.Ci, gitlabCi);
        AddSignal(signals, "Azure Pipelines", TechCategory.Ci, azurePipelines);
        AddSignal(signals, "Jenkins", TechCategory.Ci, jenkins);
        AddSignal(signals, "CircleCI", TechCategory.Ci, circleci);

        foreach (var (tech, info) in dbSignals.OrderBy(k => k.Key, StringComparer.Ordinal))
            signals.Add(new InfraSignal(tech, info.Cat, info.Files.ToList(),
                $"docker-compose image '{tech}'"));

        return signals;
    }

    private static void AddSignal(List<InfraSignal> signals, string kind, TechCategory cat, List<string> files)
    {
        if (files.Count == 0) return;
        files.Sort(StringComparer.Ordinal);
        signals.Add(new InfraSignal(kind, cat, files, null));
    }

    private void ScanCompose(string fullPath, string relPath,
        Dictionary<string, (TechCategory, SortedSet<string>)> dbSignals)
    {
        string content;
        try { content = File.ReadAllText(fullPath); }
        catch (IOException) { return; }

        foreach (Match m in ImageRegex().Matches(content))
        {
            var image = m.Groups["img"].Value.Trim();
            var imageName = image.Split(':')[0];
            if (!KnownImages.TryGetValue(imageName, out var db))
            {
                // try last path segment (e.g. "bitnami/postgresql" -> "postgresql")
                var leaf = imageName.Split('/').Last();
                if (!KnownImages.TryGetValue(leaf, out db)) continue;
            }
            if (!dbSignals.TryGetValue(db.Name, out var entry))
                entry = dbSignals[db.Name] = (db.Category, new SortedSet<string>(StringComparer.Ordinal));
            entry.Item2.Add(relPath);
        }
    }

    private static bool IsDockerfile(string name)
        => string.Equals(name, "Dockerfile", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Containerfile", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".Dockerfile", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Dockerfile.", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompose(string name)
        => string.Equals(name, "docker-compose.yml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "docker-compose.yaml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "compose.yml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "compose.yaml", StringComparison.OrdinalIgnoreCase);

    private static bool IsKubernetesManifest(ClassifiedFile cf)
    {
        var name = cf.File.FileName;
        if (!name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!cf.File.RelativePath.Contains("k8s", StringComparison.OrdinalIgnoreCase)
            && !cf.File.RelativePath.Contains("kubernetes", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var head = File.ReadLines(cf.File.FullPath).Take(40);
            return head.Any(l => l.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
                && head.Any(l => l.StartsWith("apiVersion:", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return false; }
    }

    [GeneratedRegex(@"image:\s*[""']?(?<img>[\w.\-\/]+(?::[\w.\-]+)?)[""']?")]
    private static partial Regex ImageRegex();
}
