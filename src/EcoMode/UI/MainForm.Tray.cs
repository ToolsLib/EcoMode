namespace ecomode;

public partial class MainForm
{
    private TrayStatusIcons? _trayStatusIcons;

    private void RefreshTrayStatus()
    {
        if (_trayStatusIcons == null) return;
        var icon = _trayStatusIcons[ecoSwitch.State];
        if (!ReferenceEquals(trayIcon.Icon, icon)) trayIcon.Icon = icon;
        string text = "EcoMode — " + L.T(ecoSwitch.State switch
        {
            EcoSwitchState.On => "Eco mode is on",
            EcoSwitchState.RestorePending => "Restore pending",
            _ => "Eco mode is off"
        });
        if (trayIcon.Text != text) trayIcon.Text = text;
    }
}
