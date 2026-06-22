using System.ComponentModel.DataAnnotations;

namespace MCPWebApp.Models;

public sealed class ConfigSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(256)]
    public string Key { get; set; } = string.Empty;

    public string? DefaultValue { get; set; }

    public string? OverrideValue { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }

    public bool IsSecret { get; set; }

    [MaxLength(256)]
    public string? SecretReference { get; set; }

    [MaxLength(128)]
    public string? UpdatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? EffectiveValue => OverrideValue ?? DefaultValue;
}
