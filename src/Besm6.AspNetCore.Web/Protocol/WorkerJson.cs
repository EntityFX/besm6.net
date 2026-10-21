using System.Text.Json;

namespace Besm6.AspNetCore.Web.Protocol;

public static class WorkerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Options);
}
