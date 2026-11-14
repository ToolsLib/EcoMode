namespace ecomode;

// One owned icon per state, reused on every transition and language change.
internal sealed class TrayStatusIcons : IDisposable
{
    private readonly Dictionary<EcoSwitchState, Icon> _icons = new();

    internal TrayStatusIcons()
    {
        try
        {
            Load(EcoSwitchState.Off, "app_simplified.ico");
            Load(EcoSwitchState.On, "tray_eco_on.ico");
            Load(EcoSwitchState.RestorePending, "tray_restore_pending.ico");
        }
        catch { Dispose(); throw; }
    }

    private void Load(EcoSwitchState state, string name)
    {
        using var stream = typeof(TrayStatusIcons).Assembly.GetManifestResourceStream("ecomode.Assets." + name)
            ?? throw new InvalidOperationException("Missing tray icon: " + name);
        using var icon = new Icon(stream);
        // Clone before closing the resource stream so the owned icon is independent.
        _icons.Add(state, (Icon)icon.Clone());
    }

    internal Icon this[EcoSwitchState state] => _icons[state];

    public void Dispose()
    {
        foreach (var icon in _icons.Values) icon.Dispose();
        _icons.Clear();
    }
}
