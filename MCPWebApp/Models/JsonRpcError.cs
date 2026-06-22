using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCPWebApp.Models;

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }
}
