namespace MCPWebApp.Models;

public sealed class RagOptions
{
    public bool Enabled { get; set; } = true;

    public string EmbeddingModel { get; set; } = "text-embedding-004";

    public int EmbeddingDimensions { get; set; } = 768;

    public int TopK { get; set; } = 5;

    public double MinSimilarity { get; set; } = 0.72;

    public int MaxContextChars { get; set; } = 6000;

    public int SearchTimeoutMs { get; set; } = 1500;

    public int EmbeddingCacheMinutes { get; set; } = 30;

    public int EmbeddingFailureCooldownSeconds { get; set; } = 60;

    public int IndexQueueCapacity { get; set; } = 1000;

    public string GeminiApiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com";
}
