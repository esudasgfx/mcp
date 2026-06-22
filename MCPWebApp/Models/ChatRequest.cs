using System.Text.Json;

namespace MCPWebApp.Models;

public sealed class ChatRequest
{
    public Guid? SessionId { get; set; }

    public string ToolName { get; set; } = string.Empty;

    public JsonElement Parameters { get; set; }
}
