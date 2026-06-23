namespace Ivy.StackAnalyzer.Models;

/// <summary>YAML wrapper for <c>vendor.yml</c>.</summary>
public sealed class VendorFile
{
    public List<string> Patterns { get; set; } = [];
}
