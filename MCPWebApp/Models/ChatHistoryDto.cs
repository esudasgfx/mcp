namespace MCPWebApp.Models;

public sealed class ChatHistoryDto
{
    public Guid SessionId { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ChatMessageDto> Messages { get; set; } = [];
}

public sealed class ChatMessageDto
{
    public Guid Id { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? ToolName { get; set; }

    public string? MessageText { get; set; }

    public string? ParametersJson { get; set; }

    public string? ResponseText { get; set; }

    public string? EventType { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public long? DurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
