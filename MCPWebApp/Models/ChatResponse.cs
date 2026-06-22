namespace MCPWebApp.Models;

public sealed class ChatResponse
{
    public Guid? SessionId { get; set; }

    public Guid? MessageId { get; set; }

    public bool Success { get; set; }

    public string? Content { get; set; }

    public string? Error { get; set; }
}
