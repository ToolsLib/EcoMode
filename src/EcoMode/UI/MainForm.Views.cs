namespace ecomode;

internal sealed record ViewOption(AppView View, string Caption)
{
    public override string ToString() => L.T(Caption);
}

public partial class MainForm
{
    private readonly ViewPreferences _viewPreferences;
    private AppView _view;
    private bool _simpleActivityVisible;
    private bool _activityNeedsAttention;
    private bool _applyingViewLayout;
    private readonly List<Control> _advancedSections = new();
    private readonly List<Control> _simpleSections = new();
    private readonly ComboBox cboView = new UtilityComboBox();
    private readonly Button btnSimpleRestore = new UtilityButton();
    private readonly Button btnSimpleActivity = new UtilityButton();
    private readonly Label lblSimpleSummary = UtilityTheme.Label("");
    private readonly Label lblSimpleOriginals = UtilityTheme.Label("");
    private readonly Label lblSimpleAccess = UtilityTheme.Label("", muted: true);
    private readonly ToolStripMenuItem viewMenu = new("&View");
    private readonly ToolStripMenuItem miSimpleView = new("&Standard view");
    private readonly ToolStripMenuItem miAdvancedView = new("&Advanced view");
    private Control _accessPanel = null!;

    internal AppView CurrentView => _view;

    internal void SetView(AppView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        if (_view == view) return;
        _view = view;
        ApplyViewLayout(resize: true);
        try { _viewPreferences.Save(view); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportViewSaveFailure(L.T("Your view choice could not be remembered. You may be asked again next time. ") + ex.Message);
        }
    }

    internal void ReportViewSaveFailure(string message) => LogWarn(message);

    private void ApplyViewLayout(bool resize)
    {
        bool simple = _view == AppView.Simple;
        _applyingViewLayout = true;
        SuspendLayout();
        try
        {
            TrySetMinSizes(splitMain, 0, 0);
            splitTop.Panel2Collapsed = simple;
            splitMain.Panel2Collapsed = simple && !_simpleActivityVisible;
            foreach (var section in _advancedSections) section.Visible = !simple;
            foreach (var section in _simpleSections) section.Visible = simple;
            _accessPanel.Visible = !simple;
            PlaceBatteryDashboard(simple);
            editMenu.Visible = !simple;
            miSave.Visible = miTraySave.Visible = !simple;
            miSimpleView.Checked = simple;
            miAdvancedView.Checked = !simple;
            cboView.SelectedItem = cboView.Items.Cast<ViewOption>().Single(option => option.View == _view);
            cboView.Refresh();
            lblAppSubtitle.Text = simple ? L.T("Save power. Keep background activity in check.") : L.T("POWER CONTROL  /  Keep background work in check.");
            UpdateSimpleSummary();
            RefreshSimpleStatus();
            if (resize)
            {
                MinimumSize = simple ? new Size(700, 700) : new Size(1100, 700);
                Size = simple ? new Size(780, 760) : new Size(1200, 800);
            }
        }
        finally { ResumeLayout(performLayout: true); _applyingViewLayout = false; }
        if (Visible) LayoutCurrentView();
    }

    private Control BuildSimpleIntroduction()
    {
        var section = SimpleSection();
        var title = UtilityTheme.Label("Save power with one switch.");
        title.Font = UtilityTheme.StatusFont;
        title.Margin = new Padding(0, 0, 0, 4);
        section.Controls.Add(title);
        return section;
    }

    private Control BuildSimpleDetails()
    {
        var section = SimpleSection();
        var heading = UtilityTheme.Label("YOUR SETUP", muted: true);
        heading.Font = UtilityTheme.StrongFont;
        heading.Margin = new Padding(0, 4, 0, 4);
        section.Controls.Add(heading);
        lblSimpleSummary.Dock = DockStyle.Fill;
        lblSimpleSummary.Margin = new Padding(0, 0, 0, 8);
        section.Controls.Add(lblSimpleSummary);
        var customize = UtilityTheme.Label("Use Advanced view to change these options.", muted: true);
        customize.Dock = DockStyle.Fill;
        customize.Margin = new Padding(0, 0, 0, 8);
        section.Controls.Add(customize);
        lblSimpleOriginals.Dock = DockStyle.Fill;
        lblSimpleOriginals.Font = UtilityTheme.StrongFont;
        lblSimpleOriginals.Margin = new Padding(0, 0, 0, 6);
        section.Controls.Add(lblSimpleOriginals);
        UtilityTheme.StyleButton(btnSimpleRestore);
        btnSimpleRestore.Text = "Restore saved settings";
        btnSimpleRestore.AutoSize = true;
        btnSimpleRestore.Dock = DockStyle.Fill;
        btnSimpleRestore.Margin = new Padding(0, 0, 0, 8);
        btnSimpleRestore.Click += (_, _) => RestoreOriginalSettings(restoreAll: true);
        section.Controls.Add(btnSimpleRestore);
        lblSimpleAccess.Dock = DockStyle.Fill;
        lblSimpleAccess.Margin = new Padding(0, 0, 0, 8);
        section.Controls.Add(lblSimpleAccess);
        UtilityTheme.StyleButton(btnSimpleActivity);
        btnSimpleActivity.Text = "View activity";
        btnSimpleActivity.AutoSize = true;
        btnSimpleActivity.Dock = DockStyle.Fill;
        btnSimpleActivity.Margin = Padding.Empty;
        btnSimpleActivity.Click += (_, _) =>
        {
            _simpleActivityVisible = !_simpleActivityVisible;
            if (_simpleActivityVisible) _activityNeedsAttention = false;
            splitMain.Panel2Collapsed = !_simpleActivityVisible;
            UpdateActivityButton();
            LayoutCurrentView();
        };
        section.Controls.Add(btnSimpleActivity);
        return section;
    }

    private TableLayoutPanel SimpleSection()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _simpleSections.Add(section);
        return section;
    }

    private void AddAdvancedSection(TableLayoutPanel content, Control section)
    {
        _advancedSections.Add(section);
        AddSection(content, section);
    }

    private void UpdateSimpleSummary()
    {
        var options = new List<string>();
        if (chkPowerPlan.Checked) options.Add(L.T("Power plan savings"));
        if (chkEdge.Checked || chkChrome.Checked) options.Add(L.T("Browser power savings (restart browsers after changes)"));
        if (chkGpuPref.Checked) options.Add(L.T("Lower-power graphics for selected apps"));
        if (chkEcoQos.Checked) options.Add(L.T("Reduced background activity"));
        lblSimpleSummary.Text = options.Count == 0 ? L.T("No power-saving options selected. Choose your options in Advanced view.") :
            string.Join(Environment.NewLine, options.Select(option => "• " + option));
    }

    private void RefreshSimpleStatus()
    {
        lblSimpleOriginals.Text = _originalStatusKey switch
        {
            "ORIGINALS  /  SAVED" => L.T("Your original settings are saved."),
            "ORIGINALS  /  ECO CHANGES ACTIVE" => L.T("Your originals are saved. Switch off to restore them."),
            "ORIGINALS  /  RESTORE PENDING" => L.T("Some settings still need to be restored. Click RETRY."),
            "ORIGINALS  /  SAVE FAILED" => L.T("Your settings could not be saved. Check activity and retry."),
            "ORIGINALS  /  COULD NOT READ" => L.T("Your saved settings could not be read. View activity for help."),
            _ => L.T("Your original settings will be saved before Eco Mode starts.")
        };
        lblSimpleOriginals.ForeColor = lblOriginalStatus.ForeColor;
        btnSimpleRestore.Enabled = btnRestoreOriginal.Enabled;
        lblSimpleAccess.Text = _isAdmin
            ? L.T("EcoMode restores the settings it changed when you quit. Closing the window keeps it running in the tray.")
            : L.T("Some options need Administrator access. Available options still work. Quit restores changes; closing the window keeps EcoMode running in the tray.");
    }

    private void UpdateActivityButton()
    {
        btnSimpleActivity.Text = _simpleActivityVisible ? L.T("Hide activity") :
            _activityNeedsAttention ? L.T("View activity (needs attention)") : L.T("View activity");
        btnSimpleActivity.ForeColor = _activityNeedsAttention ? UtilityTheme.Warning : UtilityTheme.Text;
    }

    private void LayoutCurrentView()
    {
        if (_view == AppView.Simple)
        {
            TrySetMinSizes(splitMain, 0, 76);
            if (!splitMain.Panel2Collapsed) SafeSetSplitter(splitMain, splitMain.Height - 110);
            FitControlPanel();
            return;
        }
        TrySetMinSizes(splitMain, 340, 76);
        TrySetMinSizes(splitTop, 440, 330);
        TrySetMinSizes(splitRight, 100, 100);
        SafeSetSplitter(splitMain, splitMain.Height - 102);
        SafeSetSplitter(splitTop, (int)(splitTop.Width * 0.49));
        SafeSetSplitter(splitRight, (int)(splitRight.Height * 0.50));
        FitControlPanel();
        SafeSetSplitter(splitRight, splitRight.Height / 2);
    }
}
