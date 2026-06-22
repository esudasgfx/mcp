using MCPWebApp.Models;

namespace MCPWebApp.Services;

public interface IRagMemoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task UpsertMemoryAsync(
        string sourceType,
        Guid sourceId,
        string content,
        Guid? chatSessionId = null,
        string? toolName = null,
        string? metadataJson = null,
        CancellationToken cancellationToken = default);

    Task<List<MemorySearchResultDto>> SearchAsync(
        string query,
        int? topK = null,
        double? minSimilarity = null,
        CancellationToken cancellationToken = default);

    Task IndexConfigSettingAsync(
        ConfigSettingDto setting,
        CancellationToken cancellationToken = default);
}
