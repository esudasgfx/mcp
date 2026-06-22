namespace MCPWebApp.Models;

public sealed class ConfigUpdateRequest
{
    public string Category { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string? OverrideValue { get; set; }

    public string? Description { get; set; }

    public bool IsSecret { get; set; }

    public string? SecretReference { get; set; }

    public Guid? ChatSessionId { get; set; }

    public string? Reason { get; set; }
}
