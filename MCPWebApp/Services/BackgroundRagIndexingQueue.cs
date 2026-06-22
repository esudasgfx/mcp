using System.Threading.Channels;
using MCPWebApp.Models;
using Microsoft.Extensions.Options;

namespace MCPWebApp.Services;

public sealed class BackgroundRagIndexingQueue : IBackgroundRagIndexingQueue
{
    private readonly Channel<MemoryIndexJob> _queue;

    public BackgroundRagIndexingQueue(IOptions<RagOptions> options)
    {
        var capacity = Math.Clamp(options.Value.IndexQueueCapacity, 10, 100_000);
        _queue = Channel.CreateBounded<MemoryIndexJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueAsync(
        MemoryIndexJob job,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(job.Content) ||
            string.IsNullOrWhiteSpace(job.SourceType) ||
            job.SourceId == Guid.Empty)
        {
            return ValueTask.CompletedTask;
        }

        return _queue.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<MemoryIndexJob> DequeueAllAsync(
        CancellationToken cancellationToken = default)
    {
        return _queue.Reader.ReadAllAsync(cancellationToken);
    }
}
