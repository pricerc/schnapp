namespace Schnapp.Domain.Entities;

/// <summary>
/// Org-wide configuration key/value row. Well-known keys live in <see cref="Schnapp.Domain.SettingKeys"/>.
/// </summary>
public sealed class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
