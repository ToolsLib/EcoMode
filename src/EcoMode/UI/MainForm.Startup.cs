namespace ecomode;

public partial class MainForm
{
    private readonly IStartupRegistration _startupRegistration;
    private readonly bool _startInTray;
    private bool _trayStartupInitialized, _startupEnabled;
    private readonly ToolStripMenuItem miStartWithWindows = new("Start with Windows");
    private readonly ToolStripMenuItem miTrayStartWithWindows = new("Start with Windows");

    private void BuildStartupMenus()
    {
        fileMenu.DropDownItems.Insert(0, miStartWithWindows);
        fileMenu.DropDownItems.Insert(1, new ToolStripSeparator());
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, miTrayStartWithWindows);
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, new ToolStripSeparator());
        miStartWithWindows.Click += (_, _) => ToggleWindowsStartup();
        miTrayStartWithWindows.Click += (_, _) => ToggleWindowsStartup();
        fileMenu.DropDownOpening += (_, _) => RefreshStartupOption();
        trayMenu.Opening += (_, _) => RefreshStartupOption();
    }

    private void RefreshStartupOption(bool reportFailure = false)
    {
        string description;
        try
        {
            var status = _startupRegistration.Read();
            _startupEnabled = status.Enabled;
            description = L.T(status.Enabled && !status.UsesCurrentCopy
                ? "Startup uses another copy. Turn this option off and on to use this copy."
                : "Start in the notification area when you sign in. Applies to your Windows account.");
        }
        catch (Exception ex)
        {
            description = L.T("Could not read the Windows startup setting: {0}", ex.Message);
            if (reportFailure) LogWarn(description);
        }
        miStartWithWindows.Checked = miTrayStartWithWindows.Checked = _startupEnabled;
        miStartWithWindows.ToolTipText = miTrayStartWithWindows.ToolTipText = description;
        miStartWithWindows.AccessibleDescription = miTrayStartWithWindows.AccessibleDescription = description;
    }

    private void ToggleWindowsStartup()
    {
        try
        {
            bool enabled = !_startupEnabled;
            _startupRegistration.SetEnabled(enabled);
            _startupEnabled = enabled;
            LogInfo(L.T(enabled ? "EcoMode will start in the notification area when you sign in."
                : "EcoMode will no longer start automatically when you sign in."));
        }
        catch (Exception ex)
        {
            string message = L.T("Could not update the Windows startup setting: {0}", ex.Message);
            LogWarn(message);
            if (!Visible) ShowTrayTip(message);
        }
        RefreshStartupOption();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _startInTray && !_trayStartupInitialized)
        {
            _trayStartupInitialized = true;
            // Keep a real, hidden main window so tray actions and Windows shutdown
            // still use the normal lifetime and restoration handlers, without a flash.
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
            StartRuntimeMonitoring();
            if (_activityNeedsAttention)
                ShowTrayTip(L.T("EcoMode started in the notification area. Open it to review settings that need attention."));
            return;
        }
        base.SetVisibleCore(value);
    }
}
