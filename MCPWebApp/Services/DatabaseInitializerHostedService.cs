using MCPWebApp.Data;
using Microsoft.EntityFrameworkCore;

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

        // For this starter app we create the schema if it does not exist, then
        // seed defaults into ConfigSettings. Enterprise deployments can replace
        // this with EF migrations in their release pipeline.
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await configStore.SeedDefaultsAsync(cancellationToken);
        _logger.LogInformation("Database schema checked and default config settings seeded.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
