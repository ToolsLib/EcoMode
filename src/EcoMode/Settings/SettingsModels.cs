using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ecomode;

internal enum SettingKind { UserRegistry, MachineRegistry, Power }

internal sealed record SettingValue([property: JsonRequired] bool Exists,
    [property: JsonRequired] RegistryValueKind Kind, [property: JsonRequired] JsonElement Data)
{
    public static SettingValue From(object value, RegistryValueKind kind = RegistryValueKind.None) =>
        new(true, kind, JsonSerializer.SerializeToElement(value));

    public static SettingValue Missing => new(false, RegistryValueKind.None, JsonSerializer.SerializeToElement<object?>(null));
}

internal sealed record SettingTarget([property: JsonRequired] SettingKind Kind,
    [property: JsonRequired] string Path, [property: JsonRequired] string Name,
    [property: JsonRequired] SettingValue EcoValue)
{
    public bool RequiresAdmin => Kind != SettingKind.UserRegistry;

    public bool Matches(SettingTarget other) => Kind == other.Kind &&
        string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
}

internal sealed class SavedSetting
{
    public required SettingTarget Target { get; init; }
    public required SettingValue Original { get; init; }
    [JsonRequired]
    public bool PendingRestore { get; set; }
}

internal sealed class OriginalSettings
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    [JsonRequired]
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;
    [JsonRequired]
    public List<SavedSetting> Settings { get; init; } = new();
}
