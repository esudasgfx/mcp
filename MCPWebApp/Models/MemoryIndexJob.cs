namespace MCPWebApp.Models;

public sealed class MemoryIndexJob
{
    public string SourceType { get; set; } = string.Empty;

    public Guid SourceId { get; set; }

    public Guid? ChatSessionId { get; set; }

    public string? ToolName { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }
}
