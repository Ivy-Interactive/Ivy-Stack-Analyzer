using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ivy.StackAnalyzer.Serialization;

/// <summary>Serializes a <see cref="StackDetection"/> to YAML (default) or JSON.</summary>
public static class StackSerializer
{
    private static readonly ISerializer Yaml = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new EnumCamelCaseConverter())
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        // Records use value equality; without this YamlDotNet would emit YAML
        // anchors/aliases for value-equal stats (e.g. two identical LanguageStat).
        .DisableAliases()
        .Build();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string ToYaml(StackDetection detection) => Yaml.Serialize(detection);

    public static string ToJson(StackDetection detection) => JsonSerializer.Serialize(detection, Json);

    public static string Serialize(StackDetection detection, OutputFormat format)
        => format == OutputFormat.Json ? ToJson(detection) : ToYaml(detection);
}

public enum OutputFormat { Yaml, Json }
