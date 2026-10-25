using System.Text.Json;

namespace ecomode;

// The snapshot is independent of importable profiles. PendingRestore is written before
// changing Windows, so even a partial apply or an app restart can be recovered.
internal sealed class OriginalSettingsManager : IOriginalSettingsManager
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "original-settings.json");

    private readonly ISystemSettings _system;
    private readonly ISettingsSnapshotStore _store;
    private readonly Action<string> _log;
    private OriginalSettings? _cached;
    private bool _restoreProgressUnsaved;

    public OriginalSettingsManager(ISystemSettings system, Action<string> log, string? path = null)
    {
        _system = system;
        _log = log;
        _store = new FileSettingsSnapshotStore(path ?? DefaultPath);
    }

    public OriginalSettingsManager(ISystemSettings system, Action<string> log, ISettingsSnapshotStore store)
    {
        _system = system;
        _log = log;
        _store = store;
    }

    public bool HasSnapshot => _store.Exists || _cached != null;
    public bool HasPendingChanges => Load()?.Settings.Any(s => s.PendingRestore) == true ||
        _cached?.Settings.Any(s => s.PendingRestore) == true;

    public void EnsureCaptured(AppState profile, bool isAdmin)
    {
        // Validate existing originals and preserve them, including pending recovery.
        // Only an explicit Save originals action may replace a saved baseline.
        if (Load() == null) Capture(profile, isAdmin);
    }

    public void Capture(AppState profile, bool isAdmin)
    {
        if (HasPendingChanges)
            throw new InvalidOperationException(L.T("Restore the pending changes before replacing the original settings."));

        var snapshot = new OriginalSettings();
        CaptureMissing(snapshot, _system.GetTargets(profile, isAdmin).ToList());
        Save(snapshot);
        _log(L.T("Original settings saved ({0:g}) to {1}.", snapshot.SavedAt, _store.Description));
    }

    public bool ApplyEco(AppState profile, bool isAdmin)
    {
        var snapshot = Load() ?? throw new InvalidOperationException(L.T("Save original settings before enabling Eco Mode."));
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
                _log(L.T("Eco applied: {0} / {1}", target.Path, target.Name));
            }
            catch (Exception ex)
            {
                success = false;
                _log(L.T("Eco apply failed: {0}: {1}", target.Name, ex.Message));
            }
        }
        return success;
    }

    public bool Restore(bool isAdmin, bool restoreAll = false)
    {
        OriginalSettings? snapshot;
        try { snapshot = Load(); }
        catch (Exception ex) when (_cached != null && (ex is IOException or UnauthorizedAccessException or JsonException))
        {
            snapshot = _cached;
            _log(L.T("Using the last validated originals because the journal cannot be read: {0}", ex.Message));
        }
        if (snapshot == null)
        {
            _log(L.T("No original settings have been saved. No system settings were changed."));
            return true;
        }

        Validate(snapshot);
        bool success = true;
        foreach (var setting in snapshot.Settings.Where(s => restoreAll || s.PendingRestore))
        {
            // Also journal a manual restore, so a failed attempt stays visible/retryable.
            setting.PendingRestore = true;
            TrySaveRestoreProgress(snapshot);
            try
            {
                if (setting.Target.RequiresAdmin && !isAdmin)
                    throw new UnauthorizedAccessException(L.T("Restart EcoMode as Administrator to restore this setting."));
                _system.Write(setting.Target, setting.Original);
                setting.PendingRestore = false;
                _log(L.T("Original restored: {0} / {1}", setting.Target.Path, setting.Target.Name));
            }
            catch (Exception ex)
            {
                success = false;
                _log(L.T("Restore failed: {0}: {1}", setting.Target.Name, ex.Message));
            }
        }
        TrySaveRestoreProgress(snapshot);
        return success;
    }

    // Move legacy machine entries out of the per-user file. Persist the shared
    // copy before removing anything, so interruption cannot lose the originals.
    internal void MigrateMachineSettingsTo(OriginalSettingsManager destination)
    {
        var source = Load();
        if (source == null || !source.Settings.Any(s => s.Target.RequiresAdmin)) return;
        var machine = destination.Load() ?? new OriginalSettings();
        foreach (var setting in source.Settings.Where(s => s.Target.RequiresAdmin))
        {
            var existing = machine.Settings.SingleOrDefault(s => s.Target.Matches(setting.Target));
            if (existing == null) machine.Settings.Add(setting);
            else existing.PendingRestore |= setting.PendingRestore;
        }
        destination.Save(machine);
        source.Settings.RemoveAll(s => s.Target.RequiresAdmin);
        Save(source);
    }

    private void TrySaveRestoreProgress(OriginalSettings snapshot)
    {
        _cached = snapshot;
        try { Save(snapshot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _restoreProgressUnsaved = true;
            _log(L.T("Restore journal could not be updated. Continuing restoration; pending work may be retried on the next launch: {0}", ex.Message));
        }
    }

    private void CaptureMissing(OriginalSettings snapshot, IEnumerable<SettingTarget> targets)
    {
        foreach (var target in targets)
        {
            // Never overwrite a baseline with a value already changed by EcoMode.
            if (snapshot.Settings.Any(s => s.Target.Matches(target))) continue;
            var original = _system.Read(target);
            _system.Validate(target, original);
            snapshot.Settings.Add(new SavedSetting { Target = target, Original = original });
        }
    }

    private OriginalSettings? Load()
    {
        // A failed manual restore may have pending work that was never writable
        // to disk. Keep that validated state until a later save succeeds.
        if (_restoreProgressUnsaved && _cached?.Settings.Any(s => s.PendingRestore) == true) return _cached;
        if (!_store.Exists) return _cached;
        var snapshot = JsonSerializer.Deserialize<OriginalSettings>(_store.Read());
        Validate(snapshot);
        _cached = snapshot;
        return snapshot;
    }

    private void Validate(OriginalSettings? snapshot)
    {
        if (snapshot == null || snapshot.Version != 1 || snapshot.Settings == null ||
            snapshot.Settings.Any(s => s == null || s.Target == null || s.Target.EcoValue == null || s.Original == null))
            throw new InvalidDataException(L.T("The original settings snapshot is invalid or from an unsupported version."));
        var seen = new List<SettingTarget>();
        foreach (var setting in snapshot.Settings)
        {
            _system.Validate(setting.Target, setting.Original);
            _system.Validate(setting.Target, setting.Target.EcoValue);
            if (seen.Any(t => t.Matches(setting.Target)))
                throw new InvalidDataException(L.T("Duplicate setting in the restore snapshot."));
            seen.Add(setting.Target);
        }
    }

    private void Save(OriginalSettings snapshot)
    {
        Validate(snapshot);
        _store.Write(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        _cached = snapshot;
        _restoreProgressUnsaved = false;
    }
}
