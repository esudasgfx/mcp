using MCPWebApp.Models;

namespace MCPWebApp.Services;

public interface IBackgroundRagIndexingQueue
{
    ValueTask QueueAsync(
        MemoryIndexJob job,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<MemoryIndexJob> DequeueAllAsync(
        CancellationToken cancellationToken = default);
}
