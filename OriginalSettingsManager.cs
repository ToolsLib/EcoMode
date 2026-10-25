using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ecomode;

internal enum SettingKind { UserRegistry, MachineRegistry, Power }

internal sealed record SettingValue(bool Exists, RegistryValueKind Kind, JsonElement Data)
{
    public static SettingValue From(object value, RegistryValueKind kind = RegistryValueKind.None) =>
        new(true, kind, JsonSerializer.SerializeToElement(value));

    public static SettingValue Missing => new(false, RegistryValueKind.None, JsonSerializer.SerializeToElement<object?>(null));
}

internal sealed record SettingTarget(SettingKind Kind, string Path, string Name, SettingValue EcoValue)
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

internal interface ISystemSettings
{
    IEnumerable<SettingTarget> GetTargets(AppState profile, bool isAdmin);
    SettingValue Read(SettingTarget target);
    void Write(SettingTarget target, SettingValue value);
}

// The snapshot is independent of importable profiles. PendingRestore is written before
// changing Windows, so even a partial apply or an app restart can be recovered.
internal sealed class OriginalSettingsManager
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "original-settings.json");

    private readonly ISystemSettings _system;
    private readonly string _path;
    private readonly Action<string> _log;

    public OriginalSettingsManager(ISystemSettings system, Action<string> log, string? path = null)
    {
        _system = system;
        _log = log;
        _path = path ?? DefaultPath;
    }

    public bool HasSnapshot => File.Exists(_path);
    public bool HasPendingChanges => Load()?.Settings.Any(s => s.PendingRestore) == true;

    public void Capture(AppState profile, bool isAdmin)
    {
        if (HasPendingChanges)
            throw new InvalidOperationException("Restore the pending changes before replacing the original settings.");

        var snapshot = new OriginalSettings();
        CaptureMissing(snapshot, _system.GetTargets(profile, isAdmin).ToList());
        Save(snapshot);
        _log($"Original settings saved ({snapshot.SavedAt:g}) to {_path}.");
    }

    public bool ApplyEco(AppState profile, bool isAdmin)
    {
        var snapshot = Load() ?? throw new InvalidOperationException("Save original settings before enabling Eco Mode.");
        var targets = _system.GetTargets(profile, isAdmin).ToList();
        CaptureMissing(snapshot, targets);
        foreach (var target in targets)
            snapshot.Settings.Single(s => s.Target.Matches(target)).PendingRestore = true;

        // If capture or persistence fails, no system changes are made.
        Save(snapshot);
        bool success = true;
        foreach (var target in targets)
        {
            try
            {
                _system.Write(target, target.EcoValue);
                _log($"Eco applied: {target.Path} / {target.Name}");
            }
            catch (Exception ex)
            {
                success = false;
                _log($"Eco apply failed: {target.Name}: {ex.Message}");
            }
        }
        return success;
    }

    public bool Restore(bool isAdmin, bool restoreAll = false)
    {
        var snapshot = Load();
        if (snapshot == null)
        {
            _log("No original settings have been saved. No system settings were changed.");
            return true;
        }

        bool success = true;
        foreach (var setting in snapshot.Settings.Where(s => restoreAll || s.PendingRestore))
        {
            // Also journal a manual restore, so a failed attempt stays visible/retryable.
            setting.PendingRestore = true;
            Save(snapshot);
            try
            {
                if (setting.Target.RequiresAdmin && !isAdmin)
                    throw new UnauthorizedAccessException("Restart EcoMode as Administrator to restore this setting.");
                _system.Write(setting.Target, setting.Original);
                setting.PendingRestore = false;
                _log($"Original restored: {setting.Target.Path} / {setting.Target.Name}");
            }
            catch (Exception ex)
            {
                success = false;
                _log($"Restore failed: {setting.Target.Name}: {ex.Message}");
            }
        }
        Save(snapshot);
        return success;
    }

    private void CaptureMissing(OriginalSettings snapshot, IEnumerable<SettingTarget> targets)
    {
        foreach (var target in targets)
        {
            // Never overwrite a baseline with a value already changed by EcoMode.
            if (snapshot.Settings.Any(s => s.Target.Matches(target))) continue;
            snapshot.Settings.Add(new SavedSetting { Target = target, Original = _system.Read(target) });
        }
    }

    private OriginalSettings? Load()
    {
        if (!File.Exists(_path)) return null;
        var snapshot = JsonSerializer.Deserialize<OriginalSettings>(File.ReadAllText(_path));
        if (snapshot == null || snapshot.Version != 1 || snapshot.Settings == null ||
            snapshot.Settings.Any(s => s == null || s.Target == null || s.Original == null))
            throw new InvalidDataException("The original settings snapshot is invalid or from an unsupported version.");
        return snapshot;
    }

    private void Save(OriginalSettings snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}
