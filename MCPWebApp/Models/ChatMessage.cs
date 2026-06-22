using System.ComponentModel.DataAnnotations;

namespace MCPWebApp.Models;

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChatSessionId { get; set; }

    public ChatSession? ChatSession { get; set; }

    [MaxLength(32)]
    public string Role { get; set; } = "user";

    [MaxLength(128)]
    public string? ToolName { get; set; }

    public string? MessageText { get; set; }

    public string? ParametersJson { get; set; }

    public string? ResponseText { get; set; }

    public string? RawJsonRpcResponse { get; set; }

    [MaxLength(128)]
    public string? EventType { get; set; }

    public bool Success { get; set; } = true;

    public string? ErrorMessage { get; set; }

    public long? DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
