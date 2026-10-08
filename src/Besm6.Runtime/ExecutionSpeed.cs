using System.Text.Json;
using System.Text.Json.Serialization;

namespace Besm6.Runtime;

/// <summary>Host pacing only; both modes execute the same BESM-6 instructions.</summary>
[JsonConverter(typeof(ExecutionSpeedJsonConverter))]
public enum ExecutionSpeed
{
    Max,
    Original,
}

public sealed class ExecutionSpeedJsonConverter : JsonConverter<ExecutionSpeed>
{
    public override ExecutionSpeed Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && ExecutionSpeedNames.TryParse(reader.GetString(), out var speed))
            return speed;
        throw new JsonException("speed must be 'original' or 'max'.");
    }

    public override void Write(Utf8JsonWriter writer, ExecutionSpeed value, JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value)) throw new JsonException("Invalid execution speed.");
        writer.WriteStringValue(value == ExecutionSpeed.Max ? "max" : "original");
    }
}

internal static class ExecutionSpeedNames
{
    internal static bool TryParse(string? text, out ExecutionSpeed speed)
    {
        speed = ExecutionSpeed.Max;
        if (string.Equals(text, "max", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.Equals(text, "original", StringComparison.OrdinalIgnoreCase)) return false;
        speed = ExecutionSpeed.Original;
        return true;
    }
}
