using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Besm6.AspNetCore.Web.Hosting;

public sealed record JobOptions(string Format, string? MachineModel = null, string? Speed = null,
    string? Mode = null, long? InstructionLimit = null, bool? GuestClock = null,
    bool? Statistics = null, bool? AutoRefresh = null, bool? DecodeMemory = null);
public sealed record ResolvedWebJob(byte[] Content, JobOptions Options);

/// <summary>Resolves catalog references once, before starting a worker. No execution hooks.</summary>
public sealed class WebJobPreset
{
    public string Kind { get; set; } = "";
    public string Example { get; set; } = "";
    public string MachineModel { get; set; } = "dubna";
    public string Speed { get; set; } = "max";
    public string Mode { get; set; } = "full";
    public long InstructionLimit { get; set; } = 1_000_000_000;
    public bool GuestClock { get; set; } = true;
    public bool Statistics { get; set; } = true;
    public bool AutoRefresh { get; set; }
    public bool DecodeMemory { get; set; }

    public static ResolvedWebJob Resolve(byte[] content, bool json, ExampleCatalog catalog)
    {
        if (!json) return new(content, new("dub"));
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(content).TrimStart('\uFEFF'));
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("JSON must contain an object.");
        if (!document.RootElement.TryGetProperty("kind", out var kind))
        {
            string? model = document.RootElement.TryGetProperty("executionModel", out var execution)
                && execution.ValueKind == JsonValueKind.String ? execution.GetString() switch {
                    "hardware" => "supervisor-hardware", "functional" => "supervisor-functional", _ => null } : null;
            return new(content, new("json", model));
        }
        if (kind.ValueKind != JsonValueKind.String || kind.GetString() != "web-job") throw new ArgumentException("Unknown job preset kind.");
        var preset = JsonSerializer.Deserialize<WebJobPreset>(document.RootElement.GetRawText(), new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })!;
        if (preset.MachineModel is not ("dubna" or "buffered") || preset.Speed is not ("max" or "original" or "virtual")
            || preset.Mode is not ("full" or "debug") || preset.InstructionLimit is < 1 or > 1_000_000_000)
            throw new ArgumentException("Invalid preset machine, speed, mode or instruction limit.");
        if (!catalog.TryGetPath(preset.Example, out string path) || Path.GetExtension(path) != ".dub")
            throw new ArgumentException("Preset example must reference a bundled .dub program.");
        return new(File.ReadAllBytes(path), new("dub", preset.MachineModel, preset.Speed, preset.Mode,
            preset.InstructionLimit, preset.GuestClock, preset.Statistics, preset.AutoRefresh, preset.DecodeMemory));
    }
}
