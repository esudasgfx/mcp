using System.Text.Json;
using MCPWebApp.Models;
using MCPWebApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace MCPWebApp.Controllers.Api;

[ApiController]
[Route("api/[controller]")]
public sealed class ConfigController : ControllerBase
{
    private readonly IConfigStoreService _configStoreService;
    private readonly IChatHistoryService _chatHistoryService;
    private readonly IMCPClientService _mcpClientService;
    private readonly IBackgroundRagIndexingQueue _ragIndexingQueue;
    private readonly ILogger<ConfigController> _logger;

    public ConfigController(
        IConfigStoreService configStoreService,
        IChatHistoryService chatHistoryService,
        IMCPClientService mcpClientService,
        IBackgroundRagIndexingQueue ragIndexingQueue,
        ILogger<ConfigController> logger)
    {
        _configStoreService = configStoreService;
        _chatHistoryService = chatHistoryService;
        _mcpClientService = mcpClientService;
        _ragIndexingQueue = ragIndexingQueue;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<ConfigSettingDto>>> GetSettings(
        CancellationToken cancellationToken)
    {
        return await _configStoreService.GetSettingsAsync(cancellationToken);
    }

    [HttpPost("override")]
    public async Task<ActionResult<ConfigSettingDto>> SaveOverride(
        [FromBody] ConfigUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _configStoreService.UpsertOverrideAsync(
                request,
                User.Identity?.Name ?? "web-user",
                cancellationToken);

            var session = await _chatHistoryService.GetOrCreateSessionAsync(
                request.ChatSessionId,
                "Configuration changes",
                cancellationToken);

            var configMessage = await _chatHistoryService.AddMessageAsync(
                session.Id,
                role: "system",
                messageText: request.Reason,
                parametersJson: JsonSerializer.Serialize(request),
                responseText: $"Updated config override {updated.Category}:{updated.Key}.",
                eventType: "config_change",
                success: true,
                cancellationToken: cancellationToken);
            await _ragIndexingQueue.QueueAsync(
                new MemoryIndexJob
                {
                    SourceType = "config_setting",
                    SourceId = updated.Id,
                    Content = BuildConfigMemoryContent(updated),
                    MetadataJson = JsonSerializer.Serialize(updated)
                },
                cancellationToken);
            await _ragIndexingQueue.QueueAsync(
                new MemoryIndexJob
                {
                    SourceType = "chat_message",
                    SourceId = configMessage.Id,
                    ChatSessionId = session.Id,
                    Content = $"Configuration change: {request.Reason}\n{updated.Category}:{updated.Key} -> {updated.EffectiveValue}",
                    MetadataJson = JsonSerializer.Serialize(request)
                },
                cancellationToken);

            // Settings such as Gemini model, tool endpoints, and Python process
            // options are read by the Python MCP process on startup. Restarting
            // after a config change keeps the process aligned with DB overrides.
            await _mcpClientService.RestartAsync(cancellationToken);
            return Ok(updated);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save config override {Category}:{Key}.",
                request.Category,
                request.Key);
            return BadRequest(new
            {
                error = "Unable to save the configuration override.",
                details = ex.Message
            });
        }
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
