using MCPWebApp.Data;
using MCPWebApp.Models;
using Microsoft.EntityFrameworkCore;

namespace MCPWebApp.Services;

public sealed class ChatHistoryService : IChatHistoryService
{
    private readonly AppDbContext _dbContext;

    public ChatHistoryService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ChatSession> GetOrCreateSessionAsync(
        Guid? sessionId,
        string? title,
        CancellationToken cancellationToken = default)
    {
        if (sessionId.HasValue)
        {
            var existing = await _dbContext.ChatSessions
                .SingleOrDefaultAsync(session => session.Id == sessionId.Value, cancellationToken);

            if (existing is not null)
            {
                return existing;
            }
        }

        var session = new ChatSession
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Enterprise MCP chat" : title.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.ChatSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ChatMessage> AddMessageAsync(
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
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var message = new ChatMessage
        {
            ChatSessionId = sessionId,
            Role = role,
            ToolName = toolName,
            MessageText = messageText,
            ParametersJson = parametersJson,
            ResponseText = responseText,
            RawJsonRpcResponse = rawJsonRpcResponse,
            EventType = eventType,
            Success = success,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
            CreatedAt = now
        };

        _dbContext.ChatMessages.Add(message);

        var session = await _dbContext.ChatSessions
            .SingleAsync(item => item.Id == sessionId, cancellationToken);
        session.UpdatedAt = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<List<ChatHistoryDto>> GetRecentSessionsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 1, 100);
        var sessions = await _dbContext.ChatSessions
            .Include(session => session.Messages.OrderByDescending(message => message.CreatedAt).Take(5))
            .OrderByDescending(session => session.UpdatedAt)
            .Take(boundedTake)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return sessions.Select(ToDto).ToList();
    }

    public async Task<ChatHistoryDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.ChatSessions
            .Include(item => item.Messages.OrderBy(message => message.CreatedAt))
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken);

        return session is null ? null : ToDto(session);
    }

    private static ChatHistoryDto ToDto(ChatSession session)
    {
        return new ChatHistoryDto
        {
            SessionId = session.Id,
            Title = session.Title,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Messages = session.Messages
                .OrderBy(message => message.CreatedAt)
                .Select(message => new ChatMessageDto
                {
                    Id = message.Id,
                    Role = message.Role,
                    ToolName = message.ToolName,
                    MessageText = message.MessageText,
                    ParametersJson = message.ParametersJson,
                    ResponseText = message.ResponseText,
                    EventType = message.EventType,
                    Success = message.Success,
                    ErrorMessage = message.ErrorMessage,
                    DurationMs = message.DurationMs,
                    CreatedAt = message.CreatedAt
                })
                .ToList()
        };
    }
}
