namespace MCPWebApp.Models;

public sealed class ChatResponse
{
    public bool Success { get; set; }

    public string? Content { get; set; }

    public string? Error { get; set; }
}
