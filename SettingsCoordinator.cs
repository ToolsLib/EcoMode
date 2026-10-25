namespace ecomode;

// User GPU preferences belong to the account; power settings and browser policies
// belong to the machine. A shared protected journal prevents another session from
// backing up EcoMode's own machine changes as the original values after a crash.
internal sealed class SettingsCoordinator : IOriginalSettingsManager
{
    private readonly OriginalSettingsManager _user;
    private readonly OriginalSettingsManager _machine;
    private readonly Action<string> _log;

    public SettingsCoordinator(ISystemSettings system, Action<string> log,
        ISettingsSnapshotStore? userStore = null, ISettingsSnapshotStore? machineStore = null)
    {
        _log = log;
        _user = new OriginalSettingsManager(new ScopedSettings(system, false), log,
            userStore ?? new FileSettingsSnapshotStore(OriginalSettingsManager.DefaultPath));
        _machine = new OriginalSettingsManager(new ScopedSettings(system, true), log,
            machineStore ?? new MachineSettingsSnapshotStore());
    }

    public bool HasSnapshot => _user.HasSnapshot || _machine.HasSnapshot;
    public bool HasPendingChanges => _user.HasPendingChanges || _machine.HasPendingChanges;

    public void Capture(AppState profile, bool isAdmin)
    {
        if (isAdmin) _user.MigrateMachineSettingsTo(_machine);
        if (HasPendingChanges) throw new InvalidOperationException("Restore pending changes before replacing the original settings.");
        _user.Capture(profile, false);
        if (isAdmin) _machine.Capture(profile, true);
    }

    public bool ApplyEco(AppState profile, bool isAdmin)
    {
        if (isAdmin) _user.MigrateMachineSettingsTo(_machine);
        if (!_user.HasSnapshot) _user.Capture(profile, false);
        if (isAdmin && !_machine.HasSnapshot) _machine.Capture(profile, true);
        bool user = _user.ApplyEco(profile, false);
        bool machine = !isAdmin || _machine.ApplyEco(profile, true);
        return user && machine;
    }

    public bool Restore(bool isAdmin, bool restoreAll = false)
    {
        if (isAdmin)
        {
            try { _user.MigrateMachineSettingsTo(_machine); }
            catch (Exception ex) { _log($"Could not migrate the legacy journal; attempting restoration from the available originals: {ex.Message}"); }
        }
        // Always attempt both scopes. One unreadable journal cannot block the other.
        bool user = TryRestore(_user, isAdmin, restoreAll);
        bool machine = TryRestore(_machine, isAdmin, restoreAll);
        return user && machine;
    }

    private bool TryRestore(OriginalSettingsManager manager, bool admin, bool all)
    {
        try { return manager.Restore(admin, all); }
        catch (Exception ex) { _log($"Restore failed: {ex.Message}"); return false; }
    }

    private sealed class ScopedSettings(ISystemSettings system, bool machine) : ISystemSettings
    {
        public IEnumerable<SettingTarget> GetTargets(AppState profile, bool isAdmin) =>
            system.GetTargets(profile, isAdmin).Where(t => t.RequiresAdmin == machine);
        public SettingValue Read(SettingTarget target) => system.Read(target);
        public void Write(SettingTarget target, SettingValue value) => system.Write(target, value);
        public void Validate(SettingTarget target, SettingValue value)
        {
            if (machine && !target.RequiresAdmin)
                throw new InvalidDataException("A machine restore journal cannot contain per-user settings.");
            system.Validate(target, value);
        }
    }
}
