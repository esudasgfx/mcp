using System.ComponentModel.DataAnnotations;

namespace MCPWebApp.Models;

public sealed class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Title { get; set; } = "New chat";

    [MaxLength(128)]
    public string? UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChatMessage> Messages { get; set; } = [];
}
