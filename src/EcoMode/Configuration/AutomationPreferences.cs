using System.Text.Json;

namespace ecomode;

internal sealed class AutomationPreferences(string? path = null)
{
    private readonly FileSettingsSnapshotStore _store = new(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "automation-settings.json"));

    internal bool Load()
    {
        try { return _store.Exists && JsonSerializer.Deserialize<Preference>(_store.Read())?.OnBattery == true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    internal void Save(bool enabled) => _store.Write(JsonSerializer.Serialize(new Preference { OnBattery = enabled }));
    private sealed class Preference { public bool OnBattery { get; init; } }
}
