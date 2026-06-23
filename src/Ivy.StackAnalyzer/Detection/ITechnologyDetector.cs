using Ivy.StackAnalyzer.Components;

namespace Ivy.StackAnalyzer.Detection;

/// <summary>
/// Code escape hatch for detections that data rules cannot express. Implementations
/// are registered with the <see cref="Pipeline"/> and run alongside the data-driven
/// <see cref="RuleEngine"/>. See PLAN.md §7c.
/// </summary>
public interface ITechnologyDetector
{
    IEnumerable<DetectedTechnology> Detect(ComponentContext ctx);
}
