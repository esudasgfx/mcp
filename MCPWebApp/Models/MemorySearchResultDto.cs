namespace MCPWebApp.Models;

public sealed class MemorySearchResultDto
{
    public Guid Id { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public Guid SourceId { get; set; }

    public Guid? ChatSessionId { get; set; }

    public string? ToolName { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public double Similarity { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
