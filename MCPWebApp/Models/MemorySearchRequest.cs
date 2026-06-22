namespace MCPWebApp.Models;

public sealed class MemorySearchRequest
{
    public string Query { get; set; } = string.Empty;

    public int? TopK { get; set; }

    public double? MinSimilarity { get; set; }
}
