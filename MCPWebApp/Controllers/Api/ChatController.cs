using System.Text.Json;
using MCPWebApp.Models;
using MCPWebApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace MCPWebApp.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly IMCPClientService _mcpClientService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IMCPClientService mcpClientService,
        ILogger<ChatController> logger)
    {
        _mcpClientService = mcpClientService;
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

        try
        {
            var responseJson = await _mcpClientService.SendRequestAsync(
                "tools/call",
                new
                {
                    name = request.ToolName,
                    arguments = request.Parameters
                },
                cancellationToken);

            var content = ExtractToolContent(responseJson, out var error);
            if (error is not null)
            {
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new ChatResponse
                    {
                        Success = false,
                        Error = error
                    });
            }

            return Ok(new ChatResponse
            {
                Success = true,
                Content = content
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "MCP tool call timed out.");
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                new ChatResponse
                {
                    Success = false,
                    Error = "The Python MCP server took too long to respond."
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call MCP tool {ToolName}.", request.ToolName);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new ChatResponse
                {
                    Success = false,
                    Error = "Unable to get a response from the Python MCP server."
                });
        }
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
}
