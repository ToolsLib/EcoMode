namespace ecomode;

internal interface ISystemSettings
{
    IEnumerable<SettingTarget> GetTargets(AppState profile, bool isAdmin);
    SettingValue Read(SettingTarget target);
    void Write(SettingTarget target, SettingValue value);
    void Validate(SettingTarget target, SettingValue value) { }
}

internal interface IOriginalSettingsManager
{
    bool HasSnapshot { get; }
    bool HasPendingChanges { get; }
    void EnsureCaptured(AppState profile, bool isAdmin);
    void Capture(AppState profile, bool isAdmin);
    bool ApplyEco(AppState profile, bool isAdmin);
    bool Restore(bool isAdmin, bool restoreAll = false);
}
