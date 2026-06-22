using MCPWebApp.Data;
using MCPWebApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;

namespace MCPWebApp.Services;

public sealed class ConfigStoreService : IConfigStoreService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly MCPOptions _mcpOptions;

    public ConfigStoreService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IOptions<MCPOptions> mcpOptions)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _mcpOptions = mcpOptions.Value;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var seed in BuildDefaultSettings())
        {
            var existing = await _dbContext.ConfigSettings
                .SingleOrDefaultAsync(
                    setting => setting.Category == seed.Category && setting.Key == seed.Key,
                    cancellationToken);

            if (existing is null)
            {
                _dbContext.ConfigSettings.Add(seed);
                continue;
            }

            existing.DefaultValue = seed.DefaultValue;
            existing.Description = seed.Description;
            existing.IsSecret = seed.IsSecret;
            existing.SecretReference ??= seed.SecretReference;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ConfigSettingDto>> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ConfigSettings
            .OrderBy(setting => setting.Category)
            .ThenBy(setting => setting.Key)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return settings.Select(ToDto).ToList();
    }

    public async Task<ConfigSettingDto> UpsertOverrideAsync(
        ConfigUpdateRequest request,
        string? updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Category))
        {
            throw new ArgumentException("Category is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Key))
        {
            throw new ArgumentException("Key is required.", nameof(request));
        }

        var category = request.Category.Trim();
        var key = request.Key.Trim();
        var setting = await _dbContext.ConfigSettings
            .SingleOrDefaultAsync(
                item => item.Category == category && item.Key == key,
                cancellationToken);

        if (setting is null)
        {
            setting = new ConfigSetting
            {
                Category = category,
                Key = key,
                Description = request.Description,
                IsSecret = request.IsSecret,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _dbContext.ConfigSettings.Add(setting);
        }

        setting.Description = request.Description ?? setting.Description;
        setting.IsSecret = request.IsSecret || setting.IsSecret;
        setting.SecretReference = request.SecretReference ?? setting.SecretReference;
        setting.OverrideValue = setting.IsSecret
            ? null
            : string.IsNullOrWhiteSpace(request.OverrideValue)
                ? null
                : request.OverrideValue.Trim();
        setting.UpdatedBy = updatedBy;
        setting.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(setting);
    }

    public async Task<Dictionary<string, string>> GetEffectiveEnvironmentAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _dbContext.ConfigSettings
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            if (setting.IsSecret || string.IsNullOrWhiteSpace(setting.EffectiveValue))
            {
                continue;
            }

            var envName = ToEnvironmentVariableName(setting.Category, setting.Key);
            environment[envName] = setting.EffectiveValue;
        }

        return environment;
    }

    private IEnumerable<ConfigSetting> BuildDefaultSettings()
    {
        yield return Build("MCP", "PythonPath", _mcpOptions.PythonPath, "Python executable used to start the MCP server.");
        yield return Build("MCP", "ScriptPath", _mcpOptions.ScriptPath, "Path to the standalone Python MCP server main.py.");
        yield return Build("MCP", "RequestTimeoutSeconds", _mcpOptions.RequestTimeoutSeconds.ToString(), "Timeout for each JSON-RPC MCP request.");
        yield return Build("Gemini", "Model", Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-2.5-flash", "Gemini model used by the Python MCP server.");
        yield return Build("Gemini", "ApiKey", "GEMINI_API_KEY", "Environment variable containing the Gemini API key.", isSecret: true, secretReference: "GEMINI_API_KEY");
        yield return Build("RAG", "Enabled", _configuration["RAG:Enabled"] ?? "true", "Enables Gemini embeddings and pgvector semantic memory.");
        yield return Build("RAG", "EmbeddingModel", _configuration["RAG:EmbeddingModel"] ?? "text-embedding-004", "Gemini embedding model used for semantic memory.");
        yield return Build("RAG", "TopK", _configuration["RAG:TopK"] ?? "5", "Number of semantic memories retrieved per user query.");
        yield return Build("RAG", "MinSimilarity", _configuration["RAG:MinSimilarity"] ?? "0.72", "Minimum cosine similarity required for retrieved memories.");
        yield return Build("RAG", "MaxContextChars", _configuration["RAG:MaxContextChars"] ?? "6000", "Maximum retrieved memory characters injected into the active query.");
        yield return Build("P6", "BaseUrl", _configuration["P6:BaseUrl"] ?? "https://your-p6-host/p6ws/restapi", "Primavera P6 EPPM REST base URL.");
        yield return Build("P6", "Username", "P6_USERNAME", "Environment variable containing the P6 username.", isSecret: true, secretReference: "P6_USERNAME");
        yield return Build("P6", "Password", "P6_PASSWORD", "Environment variable containing the P6 password.", isSecret: true, secretReference: "P6_PASSWORD");
        yield return Build("ACC", "BaseUrl", _configuration["ACC:BaseUrl"] ?? "https://developer.api.autodesk.com", "Autodesk Platform Services base URL.");
        yield return Build("ACC", "ClientId", "ACC_CLIENT_ID", "Environment variable containing the APS client id.", isSecret: true, secretReference: "ACC_CLIENT_ID");
        yield return Build("ACC", "ClientSecret", "ACC_CLIENT_SECRET", "Environment variable containing the APS client secret.", isSecret: true, secretReference: "ACC_CLIENT_SECRET");
        yield return Build("Unifier", "BaseUrl", _configuration["Unifier:BaseUrl"] ?? "https://your-unifier-host/ws/rest/service/v1", "Oracle Primavera Unifier REST base URL.");
        yield return Build("Unifier", "Username", "UNIFIER_USERNAME", "Environment variable containing the Unifier username.", isSecret: true, secretReference: "UNIFIER_USERNAME");
        yield return Build("Unifier", "Password", "UNIFIER_PASSWORD", "Environment variable containing the Unifier password.", isSecret: true, secretReference: "UNIFIER_PASSWORD");
        yield return Build("SAP", "BaseUrl", _configuration["SAP:BaseUrl"] ?? "https://your-sap-host/sap/opu/odata/sap", "SAP OData service base URL.");
        yield return Build("EAM", "BaseUrl", _configuration["EAM:BaseUrl"] ?? "https://your-eam-host/api", "Enterprise Asset Management API base URL.");
    }

    private static ConfigSetting Build(
        string category,
        string key,
        string? defaultValue,
        string description,
        bool isSecret = false,
        string? secretReference = null)
    {
        return new ConfigSetting
        {
            Category = category,
            Key = key,
            DefaultValue = defaultValue,
            Description = description,
            IsSecret = isSecret,
            SecretReference = secretReference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ConfigSettingDto ToDto(ConfigSetting setting)
    {
        return new ConfigSettingDto
        {
            Id = setting.Id,
            Category = setting.Category,
            Key = setting.Key,
            DefaultValue = setting.IsSecret ? null : setting.DefaultValue,
            OverrideValue = setting.IsSecret ? null : setting.OverrideValue,
            EffectiveValue = setting.IsSecret ? setting.SecretReference : setting.EffectiveValue,
            Description = setting.Description,
            IsSecret = setting.IsSecret,
            SecretReference = setting.SecretReference,
            UpdatedAt = setting.UpdatedAt
        };
    }

    private static string ToEnvironmentVariableName(string category, string key)
    {
        return $"{category}_{ToSnakeCase(key)}".Replace(':', '_').ToUpperInvariant();
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0 && value[index - 1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
