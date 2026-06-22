namespace MCPWebApp.Models;

public sealed class ConfigSettingDto
{
    public Guid Id { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    public string? DefaultValue { get; set; }

    public string? OverrideValue { get; set; }

    public string? EffectiveValue { get; set; }

    public string? Description { get; set; }

    public bool IsSecret { get; set; }

    public string? SecretReference { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
