using MCPWebApp.Models;

namespace MCPWebApp.Services;

public interface IChatHistoryService
{
    Task<ChatSession> GetOrCreateSessionAsync(
        Guid? sessionId,
        string? title,
        CancellationToken cancellationToken = default);

    Task<ChatMessage> AddMessageAsync(
        Guid sessionId,
        string role,
        string? messageText,
        string? toolName = null,
        string? parametersJson = null,
        string? responseText = null,
        string? rawJsonRpcResponse = null,
        string? eventType = null,
        bool success = true,
        string? errorMessage = null,
        long? durationMs = null,
        CancellationToken cancellationToken = default);

    Task<List<ChatHistoryDto>> GetRecentSessionsAsync(
        int take,
        CancellationToken cancellationToken = default);

    Task<ChatHistoryDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
