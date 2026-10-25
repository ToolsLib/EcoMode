using System.Globalization;

namespace ecomode;

public partial class MainForm
{
    private readonly UtilityCard _batteryDashboard = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10) };
    private readonly Panel _standardBatteryHost = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
    private readonly Panel _advancedBatteryHost = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, 10) };
    private readonly Label lblBattery = UtilityTheme.Label("");
    private readonly Label lblBatteryUsage = UtilityTheme.Label("", muted: true);
    private readonly Label lblAutomaticStatus = UtilityTheme.Label("", muted: true);
    private readonly UtilitySwitch chkAutomatic = new();
    private readonly Button btnBatteryHistory = new UtilityButton();
    private readonly Button btnResumeAutomatic = new UtilityButton();
    private BatteryHistoryChart _batteryChart = null!;
    private Control _batteryLegend = null!;
    private bool _updatingAutomationSwitch, _historyVisible;

    private void BuildBatteryDashboard()
    {
        _batteryDashboard.Name = "batteryDashboard";
        _advancedBatteryHost.Controls.Add(_batteryDashboard);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _batteryDashboard.Controls.Add(content);
        var heading = UtilityTheme.Label("BATTERY", muted: true);
        heading.Font = UtilityTheme.StrongFont;
        heading.Anchor = AnchorStyles.Left;
        content.Controls.Add(heading, 0, 0);
        UtilityTheme.StyleButton(btnBatteryHistory);
        btnBatteryHistory.AutoSize = true;
        btnBatteryHistory.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnBatteryHistory.Height = 25;
        btnBatteryHistory.Margin = Padding.Empty;
        btnBatteryHistory.Text = "Show history";
        btnBatteryHistory.Click += (_, _) =>
        {
            _historyVisible = !_historyVisible;
            if (!_historyVisible) MinimumSize = _view == AppView.Simple ? new Size(700, 700) : new Size(1100, 700);
            UpdateBatteryDashboard();
            FitControlPanel();
        };
        var tools = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        UtilityTheme.StyleButton(btnLiveApps);
        btnLiveApps.Text = "Live apps";
        btnLiveApps.AutoSize = true;
        btnLiveApps.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnLiveApps.Margin = new Padding(0, 0, 6, 0);
        btnLiveApps.Click += (_, _) => ShowLiveApps();
        tools.Controls.Add(btnLiveApps);
        tools.Controls.Add(btnBatteryHistory);
        content.Controls.Add(tools, 1, 0);
        lblBattery.Font = UtilityTheme.StrongFont;
        lblBattery.Dock = DockStyle.Fill;
        lblBattery.Margin = new Padding(0, 6, 0, 3);
        content.Controls.Add(lblBattery, 0, 1); content.SetColumnSpan(lblBattery, 2);
        lblBatteryUsage.Dock = DockStyle.Fill;
        lblBatteryUsage.Margin = new Padding(0, 0, 0, 8);
        content.Controls.Add(lblBatteryUsage, 0, 2); content.SetColumnSpan(lblBatteryUsage, 2);
        _batteryChart = new BatteryHistoryChart(_batteryHistory) { Visible = false };
        content.Controls.Add(_batteryChart, 0, 3); content.SetColumnSpan(_batteryChart, 2);
        _batteryLegend = UtilityTheme.Label("Last 60 min · green: Eco on · gray: Eco off · dotted: mode change", muted: true);
        _batteryLegend.Dock = DockStyle.Fill;
        _batteryLegend.Margin = new Padding(0, 4, 0, 8);
        _batteryLegend.Visible = false;
        content.Controls.Add(_batteryLegend, 0, 4); content.SetColumnSpan(_batteryLegend, 2);
        var automatic = UtilityTheme.Label("Automatic on battery (background apps only)");
        automatic.Dock = DockStyle.Fill;
        automatic.TextAlign = ContentAlignment.MiddleLeft;
        automatic.Margin = new Padding(0, 0, 10, 0);
        automatic.Cursor = Cursors.Hand;
        automatic.Click += (_, _) => { if (chkAutomatic.Enabled) chkAutomatic.Checked = !chkAutomatic.Checked; };
        content.Controls.Add(automatic, 0, 5);
        chkAutomatic.AccessibleName = "Automatic on battery (background apps only)";
        chkAutomatic.AccessibleDescription = "Automatically limit background apps on battery and restore them when plugged in.";
        chkAutomatic.Anchor = AnchorStyles.Right;
        chkAutomatic.Margin = Padding.Empty;
        _updatingAutomationSwitch = true;
        chkAutomatic.Checked = _automaticPolicy.Enabled;
        _updatingAutomationSwitch = false;
        chkAutomatic.CheckedChanged += (_, _) => { if (!_updatingAutomationSwitch) SetAutomaticMode(chkAutomatic.Checked); };
        content.Controls.Add(chkAutomatic, 1, 5);
        lblAutomaticStatus.Dock = DockStyle.Fill;
        lblAutomaticStatus.Margin = new Padding(0, 5, 0, 0);
        content.Controls.Add(lblAutomaticStatus, 0, 6); content.SetColumnSpan(lblAutomaticStatus, 2);
        UtilityTheme.StyleButton(btnResumeAutomatic);
        btnResumeAutomatic.AutoSize = true;
        btnResumeAutomatic.Dock = DockStyle.Fill;
        btnResumeAutomatic.Margin = new Padding(0, 5, 0, 0);
        btnResumeAutomatic.Text = "Resume automatic mode";
        btnResumeAutomatic.Visible = false;
        btnResumeAutomatic.Click += (_, _) => ResumeAutomaticMode();
        content.Controls.Add(btnResumeAutomatic, 0, 7); content.SetColumnSpan(btnResumeAutomatic, 2);
    }

    private void PlaceBatteryDashboard(bool simple)
    {
        var host = simple ? _standardBatteryHost : _advancedBatteryHost;
        if (_batteryDashboard.Parent != host) host.Controls.Add(_batteryDashboard);
        _standardBatteryHost.Visible = simple;
        _advancedBatteryHost.Visible = !simple;
        UpdateBatteryDashboard();
    }

    private void UpdateBatteryDashboard()
    {
        if (_batteryChart == null) return;
        string source = _battery.Charging ? L.T("Charging") : _battery.Connection switch
        {
            PowerConnection.Battery => L.T("On battery"),
            PowerConnection.External => L.T("Plugged in"),
            _ => L.T("Power status unavailable")
        };
        lblBattery.Text = _battery.HasBattery == false ? L.T("No battery detected") :
            _battery.Percent is int percent ? (percent / 100d).ToString("P0", CultureInfo.CurrentCulture) + " · " + source : source;
        var usage = new List<string>();
        if (_battery.Remaining is TimeSpan remaining)
            usage.Add(L.T("About {0} h {1} min left", (int)remaining.TotalHours, remaining.Minutes));
        if (_battery.DischargeWatts is double watts) usage.Add(L.T("{0:F1} W discharge", watts));
        if (_batteryHistory.DrainPercentPerHour is double drain) usage.Add(L.T("{0:F1}%/h observed", drain));
        lblBatteryUsage.Text = usage.Count > 0 ? string.Join(" · ", usage) : L.T("Runtime and discharge readings depend on your hardware.");
        _batteryChart.Visible = _batteryLegend.Visible = _historyVisible;
        btnBatteryHistory.Text = _historyVisible ? L.T("Hide history") : L.T("Show history");
        chkAutomatic.Enabled = _battery.HasBattery != false || _automaticPolicy.Enabled;
        string status = !_automaticPolicy.Enabled ? "Automatic mode is off." :
            _automationBlocked || _originalSettingsCaptureFailed ? "Restore or backup needs attention. Automatic mode is paused." :
            _ecoEnabled && !_automaticProcessMode ? "Manual profile active. Resume automatic mode to hand back control." :
            _automaticPolicy.ManualOverride ? "Manual override until the power source changes." :
            _automaticProcessMode ? "Automatic · background apps only" :
            _battery.HasBattery == false ? "No battery detected. Automatic mode is idle." :
            _battery.Connection == PowerConnection.External ? "Plugged in · automatic mode is idle." : "Waiting for power status.";
        lblAutomaticStatus.Text = L.T(status);
        btnResumeAutomatic.Visible = _automaticPolicy.Enabled && (_automaticPolicy.ManualOverride || _ecoEnabled && !_automaticProcessMode);
        _batteryChart.Invalidate();
    }
}
