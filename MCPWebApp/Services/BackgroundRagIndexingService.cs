using MCPWebApp.Models;

namespace MCPWebApp.Services;

public sealed class BackgroundRagIndexingService : BackgroundService
{
    private readonly IBackgroundRagIndexingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundRagIndexingService> _logger;

    public BackgroundRagIndexingService(
        IBackgroundRagIndexingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundRagIndexingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessJobAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(
        MemoryIndexJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ragMemoryService = scope.ServiceProvider.GetRequiredService<IRagMemoryService>();
            await ragMemoryService.UpsertMemoryAsync(
                job.SourceType,
                job.SourceId,
                job.Content,
                job.ChatSessionId,
                job.ToolName,
                job.MetadataJson,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Background RAG indexing failed for {SourceType}:{SourceId}.",
                job.SourceType,
                job.SourceId);
        }
    }
}
