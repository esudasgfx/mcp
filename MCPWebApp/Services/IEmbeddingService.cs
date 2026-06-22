namespace MCPWebApp.Services;

public interface IEmbeddingService
{
    bool IsConfigured { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
