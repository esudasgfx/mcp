using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MCPWebApp.Models;
using MCPWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MCPWebApp.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly IMCPClientService _mcpClientService;
    private readonly IChatHistoryService _chatHistoryService;
    private readonly IRagMemoryService _ragMemoryService;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IMCPClientService mcpClientService,
        IChatHistoryService chatHistoryService,
        IRagMemoryService ragMemoryService,
        IOptions<RagOptions> ragOptions,
        ILogger<ChatController> logger)
    {
        _mcpClientService = mcpClientService;
        _chatHistoryService = chatHistoryService;
        _ragMemoryService = ragMemoryService;
        _ragOptions = ragOptions.Value;
        _logger = logger;
    }

    [HttpGet("tools")]
    public async Task<IActionResult> GetTools(CancellationToken cancellationToken)
    {
        try
        {
            var tools = await _mcpClientService.ListToolsAsync(cancellationToken);
            return Ok(tools);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list MCP tools.");
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new ChatResponse
                {
                    Success = false,
                    Error = "Unable to retrieve tools from the Python MCP server."
                });
        }
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return BadRequest(new ChatResponse
            {
                Success = false,
                Error = "ToolName is required."
            });
        }

        var stopwatch = Stopwatch.StartNew();
        var session = await _chatHistoryService.GetOrCreateSessionAsync(
            request.SessionId,
            $"Chat - {request.ToolName}",
            cancellationToken);
        var parametersJson = request.Parameters.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : request.Parameters.GetRawText();
        var userQuery = ExtractQuery(request.Parameters) ?? parametersJson;
        var retrievedMemories = await _ragMemoryService.SearchAsync(
            userQuery,
            cancellationToken: cancellationToken);
        var augmentedArguments = AugmentParametersWithMemory(
            request.Parameters,
            userQuery,
            retrievedMemories,
            _ragOptions.MaxContextChars);

        var userMessage = await _chatHistoryService.AddMessageAsync(
            session.Id,
            role: "user",
            messageText: userQuery,
            toolName: request.ToolName,
            parametersJson: parametersJson,
            eventType: "tool_call_request",
            cancellationToken: cancellationToken);
        await _ragMemoryService.UpsertMemoryAsync(
            "chat_message",
            userMessage.Id,
            BuildMemoryContent(userMessage.Role, userMessage.ToolName, userMessage.MessageText, userMessage.ParametersJson),
            chatSessionId: session.Id,
            toolName: request.ToolName,
            cancellationToken: cancellationToken);

        try
        {
            var responseJson = await _mcpClientService.SendRequestAsync(
                "tools/call",
                new
                {
                    name = request.ToolName,
                    arguments = augmentedArguments
                },
                cancellationToken);

            var content = ExtractToolContent(responseJson, out var error);
            if (error is not null)
            {
                var errorMessage = await _chatHistoryService.AddMessageAsync(
                    session.Id,
                    role: "assistant",
                    messageText: userQuery,
                    toolName: request.ToolName,
                    parametersJson: parametersJson,
                    rawJsonRpcResponse: responseJson,
                    eventType: "tool_call_response",
                    success: false,
                    errorMessage: error,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    cancellationToken: cancellationToken);

                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new ChatResponse
                    {
                        SessionId = session.Id,
                        MessageId = errorMessage.Id,
                        Success = false,
                        Error = error,
                        RetrievedMemoryCount = retrievedMemories.Count
                    });
            }

            var assistantMessage = await _chatHistoryService.AddMessageAsync(
                session.Id,
                role: "assistant",
                messageText: userQuery,
                toolName: request.ToolName,
                parametersJson: parametersJson,
                responseText: content,
                rawJsonRpcResponse: responseJson,
                eventType: "tool_call_response",
                success: true,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);
            await _ragMemoryService.UpsertMemoryAsync(
                "chat_message",
                assistantMessage.Id,
                BuildMemoryContent(
                    assistantMessage.Role,
                    assistantMessage.ToolName,
                    assistantMessage.ResponseText,
                    assistantMessage.ParametersJson),
                chatSessionId: session.Id,
                toolName: request.ToolName,
                cancellationToken: cancellationToken);

            return Ok(new ChatResponse
            {
                SessionId = session.Id,
                MessageId = assistantMessage.Id,
                Success = true,
                Content = content,
                RetrievedMemoryCount = retrievedMemories.Count
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "MCP tool call timed out.");
            var errorMessage = await _chatHistoryService.AddMessageAsync(
                session.Id,
                role: "assistant",
                messageText: userQuery,
                toolName: request.ToolName,
                parametersJson: parametersJson,
                eventType: "tool_call_response",
                success: false,
                errorMessage: "The Python MCP server took too long to respond.",
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new ChatResponse
                {
                    SessionId = session.Id,
                    MessageId = errorMessage.Id,
                    Success = false,
                    Error = "The Python MCP server took too long to respond.",
                    RetrievedMemoryCount = retrievedMemories.Count
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call MCP tool {ToolName}.", request.ToolName);
            var errorMessage = await _chatHistoryService.AddMessageAsync(
                session.Id,
                role: "assistant",
                messageText: userQuery,
                toolName: request.ToolName,
                parametersJson: parametersJson,
                eventType: "tool_call_response",
                success: false,
                errorMessage: "Unable to get a response from the Python MCP server.",
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return StatusCode(
                StatusCodes.Status502BadGateway,
                new ChatResponse
                {
                    SessionId = session.Id,
                    MessageId = errorMessage.Id,
                    Success = false,
                    Error = "Unable to get a response from the Python MCP server.",
                    RetrievedMemoryCount = retrievedMemories.Count
                });
        }
    }

    [HttpPost("memory/search")]
    public async Task<ActionResult<List<MemorySearchResultDto>>> SearchMemory(
        [FromBody] MemorySearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest("Query is required.");
        }

        return await _ragMemoryService.SearchAsync(
            request.Query,
            request.TopK,
            request.MinSimilarity,
            cancellationToken);
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<ChatHistoryDto>>> GetHistory(
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        return await _chatHistoryService.GetRecentSessionsAsync(take, cancellationToken);
    }

    [HttpGet("history/{sessionId:guid}")]
    public async Task<ActionResult<ChatHistoryDto>> GetSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _chatHistoryService.GetSessionAsync(sessionId, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    private static string ExtractToolContent(string responseJson, out string? error)
    {
        error = null;
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var errorElement))
        {
            error = errorElement.TryGetProperty("message", out var message)
                ? message.GetString() ?? errorElement.GetRawText()
                : errorElement.GetRawText();
            return string.Empty;
        }

        if (!root.TryGetProperty("result", out var result))
        {
            error = "The MCP server response did not contain a result.";
            return string.Empty;
        }

        if (result.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array)
        {
            var parts = content
                .EnumerateArray()
                .Select(item =>
                    item.TryGetProperty("text", out var text)
                        ? text.GetString()
                        : item.GetRawText())
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return string.Join(Environment.NewLine, parts);
        }

        return result.ToString();
    }

    private static string? ExtractQuery(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("query", out var query) &&
            query.ValueKind == JsonValueKind.String)
        {
            return query.GetString();
        }

        return null;
    }

    private static Dictionary<string, object?> AugmentParametersWithMemory(
        JsonElement parameters,
        string userQuery,
        IReadOnlyList<MemorySearchResultDto> retrievedMemories,
        int maxContextChars)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parameters.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in parameters.EnumerateObject())
            {
                arguments[property.Name] = property.Value.Clone();
            }
        }

        if (retrievedMemories.Count == 0)
        {
            return arguments;
        }

        var existingQuery = arguments.TryGetValue("query", out var queryValue)
            ? JsonSerializer.Serialize(queryValue).Trim('"')
            : userQuery;
        var memoryContext = BuildMemoryContext(retrievedMemories, maxContextChars);
        arguments["query"] =
            $"{existingQuery}\n\nRelevant prior memory retrieved from PostgreSQL/pgvector:\n{memoryContext}\n\nUse this memory only when it is relevant to the current request.";

        return arguments;
    }

    private static string BuildMemoryContext(
        IReadOnlyList<MemorySearchResultDto> retrievedMemories,
        int maxContextChars)
    {
        var builder = new StringBuilder();
        foreach (var memory in retrievedMemories)
        {
            builder
                .Append("- [")
                .Append(memory.SourceType)
                .Append(" similarity=")
                .Append(memory.Similarity.ToString("0.000"))
                .Append("] ")
                .Append(memory.Content.ReplaceLineEndings(" "))
                .AppendLine();

            if (builder.Length >= maxContextChars)
            {
                break;
            }
        }

        var context = builder.ToString();
        return context.Length <= maxContextChars
            ? context
            : context[..maxContextChars];
    }

    private static string BuildMemoryContent(
        string role,
        string? toolName,
        string? text,
        string? parametersJson)
    {
        return new StringBuilder()
            .Append("Role: ")
            .Append(role)
            .AppendLine()
            .Append("Tool: ")
            .Append(toolName)
            .AppendLine()
            .Append("Text: ")
            .Append(text)
            .AppendLine()
            .Append("Parameters: ")
            .Append(parametersJson)
            .ToString();
    }
}
