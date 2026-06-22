using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCPWebApp.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MCPWebApp.Services;

public sealed class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _memoryCache;
    private readonly RagOptions _options;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly string? _apiKey;
    private static DateTimeOffset? _cooldownUntil;

    public GeminiEmbeddingService(
        HttpClient httpClient,
        IMemoryCache memoryCache,
        IOptions<RagOptions> options,
        ILogger<GeminiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _memoryCache = memoryCache;
        _options = options.Value;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    }

    public bool IsConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_apiKey) &&
        (_cooldownUntil is null || DateTimeOffset.UtcNow >= _cooldownUntil.Value);

    public async Task<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "RAG embeddings require GEMINI_API_KEY, RAG:Enabled=true, and no active embedding failure cooldown.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var cacheKey = $"embedding:{_options.EmbeddingModel}:{ComputeSha256(text)}";
        if (_memoryCache.TryGetValue(cacheKey, out float[]? cachedEmbedding) &&
            cachedEmbedding is not null)
        {
            return cachedEmbedding;
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
            _cooldownUntil = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(_options.EmbeddingFailureCooldownSeconds, 1, 3600));
            throw new InvalidOperationException(
                $"Gemini embedding request failed with status {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(responseText);
        if (!document.RootElement.TryGetProperty("embedding", out var embedding) ||
            !embedding.TryGetProperty("values", out var values))
        {
            throw new InvalidOperationException("Gemini embedding response did not contain embedding.values.");
        }

        var embeddingValues = values.EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        _memoryCache.Set(
            cacheKey,
            embeddingValues,
            TimeSpan.FromMinutes(Math.Clamp(_options.EmbeddingCacheMinutes, 1, 1440)));

        return embeddingValues;
    }

    private static string ComputeSha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
