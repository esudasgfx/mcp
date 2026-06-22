using System.Net.Http.Json;
using System.Text.Json;
using MCPWebApp.Models;
using Microsoft.Extensions.Options;

namespace MCPWebApp.Services;

public sealed class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly RagOptions _options;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly string? _apiKey;

    public GeminiEmbeddingService(
        HttpClient httpClient,
        IOptions<RagOptions> options,
        ILogger<GeminiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }

    public bool IsConfigured =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "RAG embeddings require GEMINI_API_KEY and RAG:Enabled=true.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var endpoint = $"{_options.GeminiApiBaseUrl.TrimEnd('/')}/v1beta/models/{_options.EmbeddingModel}:embedContent?key={_apiKey}";
        var payload = new
        {
            model = $"models/{_options.EmbeddingModel}",
            content = new
            {
                parts = new[]
                {
                    new { text }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            endpoint,
            payload,
            cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Gemini embedding request failed with status {StatusCode}: {Body}",
                response.StatusCode,
                responseText);
            throw new InvalidOperationException(
                $"Gemini embedding request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("embedding", out var embedding) ||
            !embedding.TryGetProperty("values", out var values))
        {
            throw new InvalidOperationException("Gemini embedding response did not contain embedding.values.");
        }

        return values.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
    }
}
