using MCPWebApp.Data;
using MCPWebApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace MCPWebApp.Services;

public sealed class DatabaseInitializerHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseInitializerHostedService> _logger;

    public DatabaseInitializerHostedService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DatabaseInitializerHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var initializeDatabase = _configuration.GetValue("Database:InitializeOnStartup", true);
        if (!initializeDatabase)
        {
            _logger.LogInformation("Database initialization on startup is disabled.");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configStore = scope.ServiceProvider.GetRequiredService<IConfigStoreService>();
        var ragMemory = scope.ServiceProvider.GetRequiredService<IRagMemoryService>();
        var ragIndexingQueue = scope.ServiceProvider.GetRequiredService<IBackgroundRagIndexingQueue>();

        // For this starter app we create the schema if it does not exist, then
        // seed defaults into ConfigSettings. Enterprise deployments can replace
        // this with EF migrations in their release pipeline.
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await configStore.SeedDefaultsAsync(cancellationToken);
        await ragMemory.InitializeAsync(cancellationToken);

        var settings = await configStore.GetSettingsAsync(cancellationToken);
        foreach (var setting in settings)
        {
            await ragIndexingQueue.QueueAsync(
                new MemoryIndexJob
                {
                    SourceType = "config_setting",
                    SourceId = setting.Id,
                    Content = BuildConfigMemoryContent(setting),
                    MetadataJson = JsonSerializer.Serialize(setting)
                },
                cancellationToken);
        }

        _logger.LogInformation("Database schema checked, default config settings seeded, and semantic memory indexing queued.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private static string BuildConfigMemoryContent(ConfigSettingDto setting)
    {
        return string.Join(
            Environment.NewLine,
            $"Configuration setting: {setting.Category}:{setting.Key}",
            $"Effective value/reference: {setting.EffectiveValue}",
            $"Description: {setting.Description}");
    }
}
