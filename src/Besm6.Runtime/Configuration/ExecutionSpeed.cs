using System.Text.Json;
using System.Text.Json.Serialization;

namespace Besm6.Runtime;

/// <summary>Host pacing and hosted guest-clock selection; instruction semantics remain common.</summary>
[JsonConverter(typeof(ExecutionSpeedJsonConverter))]
public enum ExecutionSpeed
{
    Max,
    Original,
    Virtual,
}

public sealed class ExecutionSpeedJsonConverter : JsonConverter<ExecutionSpeed>
{
    public override ExecutionSpeed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ExecutionSpeedNames.TryParse(reader.GetString(), out var speed))
            return speed;
        throw new JsonException("speed must be 'original' or 'max' or 'virtual'.");
    }

    public override void Write(Utf8JsonWriter writer, ExecutionSpeed value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Invalid execution speed.");
        writer.WriteStringValue(value switch
        {
            ExecutionSpeed.Max => "max",
            ExecutionSpeed.Original => "original",
            ExecutionSpeed.Virtual => "virtual",
            _ => throw new JsonException("Invalid execution speed."),
        });
    }
}

internal static class ExecutionSpeedNames
{
    internal static bool TryParse(string? text, out ExecutionSpeed speed)
    {
        speed = ExecutionSpeed.Max;
        if (string.Equals(text, "max", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(text, "virtual", StringComparison.OrdinalIgnoreCase))
        { speed = ExecutionSpeed.Virtual; return true; }
        if (!string.Equals(text, "original", StringComparison.OrdinalIgnoreCase)) return false;
        speed = ExecutionSpeed.Original;
        return true;
    }
}
