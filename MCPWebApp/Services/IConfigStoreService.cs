using MCPWebApp.Models;

namespace MCPWebApp.Services;

public interface IConfigStoreService
{
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);

    Task<List<ConfigSettingDto>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task<ConfigSettingDto> UpsertOverrideAsync(
        ConfigUpdateRequest request,
        string? updatedBy,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, string>> GetEffectiveEnvironmentAsync(
        CancellationToken cancellationToken = default);
}
