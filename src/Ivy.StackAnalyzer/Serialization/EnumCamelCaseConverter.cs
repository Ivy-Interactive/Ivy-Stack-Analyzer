using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Ivy.StackAnalyzer.Serialization;

/// <summary>Serializes any enum as a camelCase string (and parses it back).</summary>
public sealed class EnumCamelCaseConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type.IsEnum;

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        var scalar = parser.Consume<Scalar>();
        return Enum.Parse(type, scalar.Value, ignoreCase: true);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        var name = value?.ToString() ?? "";
        emitter.Emit(new Scalar(ToCamelCase(name)));
    }

    internal static string ToCamelCase(string name)
        => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
