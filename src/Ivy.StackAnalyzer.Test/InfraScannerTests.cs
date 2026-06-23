using Ivy.StackAnalyzer.Infra;
using Xunit;

namespace Ivy.StackAnalyzer.Test;

public class InfraScannerTests
{
    private static IReadOnlyList<InfraSignal> Scan(TempRepo repo)
        => new InfraScanner(Harness.Data.Infra).Scan(Harness.Classify(repo.Root));

    [Fact]
    public void Detects_docker_compose_terraform_and_compose_images()
    {
        using var repo = new TempRepo();
        repo.Write("Dockerfile", "FROM alpine\n")
            .Write("main.tf", "resource \"null_resource\" \"x\" {}\n")
            .Write("compose.yml", """
                services:
                  db:
                    image: postgres:16
                  admin:
                    image: adminer
                """);

        var signals = Scan(repo);

        Assert.Contains(signals, s => s.Kind == "Docker" && s.Category == TechCategory.Build);
        Assert.Contains(signals, s => s.Kind == "Docker Compose");
        Assert.Contains(signals, s => s.Kind == "Terraform" && s.Category == TechCategory.Iac);
        Assert.Contains(signals, s => s.Kind == "Postgres" && s.Category == TechCategory.Database);
        // category routed via Enum.TryParse, NOT CategoryMap (which maps tool -> Build)
        Assert.Contains(signals, s => s.Kind == "Adminer" && s.Category == TechCategory.Tool);
    }

    [Fact]
    public void Detects_kubernetes_only_with_manifest_content()
    {
        using var repo = new TempRepo();
        repo.Write("k8s/deploy.yaml", "apiVersion: apps/v1\nkind: Deployment\n")
            .Write("k8s/notes.yaml", "just: some yaml\n");   // no kind/apiVersion -> not k8s

        var signals = Scan(repo);

        var k8s = Assert.Single(signals, s => s.Kind == "Kubernetes");
        Assert.Contains("k8s/deploy.yaml", k8s.Files);
        Assert.DoesNotContain("k8s/notes.yaml", k8s.Files);
    }

    [Fact]
    public void Compose_variants_are_recognized()
    {
        using var repo = new TempRepo();
        repo.Write("compose.traefik.yml", "services:\n  proxy:\n    image: traefik:3.0\n");

        var signals = Scan(repo);

        Assert.Contains(signals, s => s.Kind == "Docker Compose" && s.Files.Contains("compose.traefik.yml"));
        Assert.Contains(signals, s => s.Kind == "Traefik" && s.Category == TechCategory.Hosting);
    }

    [Fact]
    public void No_infra_yields_empty()
    {
        using var repo = new TempRepo();
        repo.Write("src/app.cs", "class A {}\n");
        Assert.Empty(Scan(repo));
    }
}
