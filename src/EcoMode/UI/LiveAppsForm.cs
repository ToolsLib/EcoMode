using ecomode.Interop;

namespace ecomode;

internal sealed class LiveAppsForm : Form
{
    private readonly Func<IReadOnlyList<LiveProcessSnapshot>> _read;
    private readonly Action<string, bool> _setExclusion;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly DataGridView _grid = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new UtilityComboBox();
    private readonly Button _exclude = new UtilityButton();
    private readonly Label _summary = UtilityTheme.Label("", muted: true);
    private readonly Label _selection = UtilityTheme.Label("", muted: true);
    private readonly Label _feedback = UtilityTheme.Label("", muted: true);
    private IReadOnlyList<LiveProcessSnapshot> _snapshot = [];
    private bool _refreshing;
    private bool _ready;

    internal LiveAppsForm(Func<IReadOnlyList<LiveProcessSnapshot>> read, Action<string, bool> setExclusion)
    {
        _read = read; _setExclusion = setExclusion;
        Text = "Live apps";
        Font = UtilityTheme.BodyFont;
        BackColor = UtilityTheme.Background; ForeColor = UtilityTheme.Text;
        Size = new Size(1060, 650); MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = CurrentAutoScaleDimensions;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        var title = UtilityTheme.Label("Live apps"); title.Font = UtilityTheme.StatusFont; title.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(title, 0, 0);
        var tools = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        _search.Dock = DockStyle.Fill; _search.BorderStyle = BorderStyle.FixedSingle; _search.BackColor = UtilityTheme.Input;
        _search.ForeColor = UtilityTheme.Text; _search.AccessibleName = "Search app or PID";
        _search.Margin = new Padding(0, 3, 10, 3); _search.TextChanged += (_, _) => RenderRows();
        _filter.Dock = DockStyle.Fill; _filter.DropDownStyle = ComboBoxStyle.DropDownList; _filter.AccessibleName = "Filter apps";
        _filter.Items.AddRange(new object[] { new AppFilter("All apps"), new AppFilter("Managed by EcoMode"), new AppFilter("Excluded apps"), new AppFilter("Needs restoration") });
        _filter.SelectedIndex = 0; _filter.SelectedIndexChanged += (_, _) => RenderRows();
        tools.Controls.Add(_search, 0, 0); tools.Controls.Add(_filter, 1, 0); root.Controls.Add(tools, 0, 1);
        _summary.Dock = DockStyle.Fill; _summary.Margin = new Padding(0, 8, 0, 8); root.Controls.Add(_summary, 0, 2);
        ConfigureGrid(); root.Controls.Add(_grid, 0, 3);
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 10, 0, 0) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selection.Dock = DockStyle.Fill; _selection.TextAlign = ContentAlignment.MiddleLeft; _selection.Margin = new Padding(0, 0, 12, 0);
        UtilityTheme.StyleButton(_exclude); _exclude.AutoSize = true; _exclude.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _exclude.Click += (_, _) => ChangeExclusion();
        actions.Controls.Add(_selection, 0, 0); actions.Controls.Add(_exclude, 1, 0); root.Controls.Add(actions, 0, 4);
        _feedback.Dock = DockStyle.Fill; _feedback.Margin = new Padding(0, 8, 0, 0); root.Controls.Add(_feedback, 0, 5);
        _timer.Tick += (_, _) => RefreshApps();
        _ready = true;
        L.LanguageChanged += RefreshLanguage;
        RefreshLanguage();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill; _grid.BackgroundColor = UtilityTheme.Input; _grid.BorderStyle = BorderStyle.None;
        _grid.ReadOnly = true; _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false; _grid.MultiSelect = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false; _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _grid.EnableHeadersVisualStyles = false; _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = UtilityTheme.Surface, ForeColor = UtilityTheme.Text, Font = UtilityTheme.StrongFont, Padding = new Padding(6) };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UtilityTheme.Input,
            ForeColor = UtilityTheme.Text,
            SelectionBackColor = UtilityTheme.Hover,
            SelectionForeColor = UtilityTheme.Text,
            Font = UtilityTheme.BodyFont,
            Padding = new Padding(6, 4, 6, 4),
            WrapMode = DataGridViewTriState.True
        };
        _grid.GridColor = UtilityTheme.Border; _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.AccessibleName = "Running apps in your Windows session";
        foreach (var (name, weight) in new[] { ("App", 27), ("PID", 9), ("EcoMode status", 28), ("Priority", 18), ("EcoQoS", 18) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = weight,
                MinimumWidth = name == "PID" ? 65 : 90,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        _grid.SelectionChanged += (_, _) => { if (!_refreshing) UpdateSelection(); };
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateRefreshTimer();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRefreshTimer();
    }

    private void UpdateRefreshTimer()
    {
        if (!_ready) return;
        if (Visible && WindowState != FormWindowState.Minimized) { RefreshApps(); _timer.Start(); }
        else _timer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); L.LanguageChanged -= RefreshLanguage; }
        base.Dispose(disposing);
    }

    internal void RefreshApps()
    {
        if (IsDisposed) return;
        try { _snapshot = _read(); RenderRows(); }
        catch (Exception ex)
        {
            _snapshot = []; RenderRows();
            _summary.Text = L.T("App list unavailable: {0}", ex.Message);
        }
    }

    private void RefreshLanguage()
    {
        L.TranslateTree(this);
        _search.PlaceholderText = L.T("Search app or PID");
        foreach (DataGridViewColumn column in _grid.Columns) column.HeaderText = L.T(column.Name);
        _filter.Refresh();
        _feedback.Text = L.T("Exclusions apply to every instance of the executable and are saved to your profile. Readings refresh every 2 seconds.");
        RenderRows();
    }

    private LiveProcessSnapshot? Selected => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as LiveProcessSnapshot : null;

    private void RenderRows()
    {
        if (!_ready) return;
        var selected = Selected;
        int first = _grid.FirstDisplayedScrollingRowIndex;
        string search = _search.Text.Trim();
        var rows = _snapshot.Where(value => (value.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || value.Id.ToString().Contains(search)) &&
            (_filter.SelectedIndex switch
            {
                1 => value.State == LiveProcessState.Managed,
                2 => value.State is LiveProcessState.Excluded or LiveProcessState.Protected || value.UserExcluded,
                3 => value.State == LiveProcessState.RestorePending,
                _ => true
            })).ToArray();
        _refreshing = true; _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var app in rows)
            {
                int index = _grid.Rows.Add(app.Name, app.Id, StateCaption(app.State), PriorityCaption(app.Priority), QosCaption(app.EcoQos));
                var row = _grid.Rows[index]; row.Tag = app;
                row.Cells[2].Style.ForeColor = app.State == LiveProcessState.Managed ? UtilityTheme.Accent :
                    app.State == LiveProcessState.RestorePending ? UtilityTheme.Warning : UtilityTheme.Text;
            }
            _grid.ClearSelection();
            var chosen = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                row.Tag is LiveProcessSnapshot value && value.Id == selected?.Id &&
                string.Equals(value.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
            if (chosen != null) { chosen.Selected = true; _grid.CurrentCell = chosen.Cells[0]; }
            else _grid.CurrentCell = null;
            if (first >= 0 && first < _grid.Rows.Count) _grid.FirstDisplayedScrollingRowIndex = first;
        }
        finally { _grid.ResumeLayout(); _refreshing = false; }
        _summary.Text = L.T("{0} of {1} apps · {2} managed · {3} need restoration", rows.Length, _snapshot.Count,
            _snapshot.Count(app => app.State == LiveProcessState.Managed), _snapshot.Count(app => app.State == LiveProcessState.RestorePending));
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var app = Selected;
        _exclude.Enabled = app != null && !app.Locked;
        _exclude.Text = app?.UserExcluded == true ? L.T("Remove exclusion") : L.T("Never throttle this app");
        _selection.Text = app == null ? L.T("Select an app to manage its exclusion.") : app.Locked ? L.T("Built-in or protected app. Safety exclusion is locked.") :
            L.T("Applies to all {0} processes.", app.Name);
    }

    private void ChangeExclusion()
    {
        var app = Selected;
        if (app == null || app.Locked) return;
        try
        {
            _setExclusion(app.Name, !app.UserExcluded);
            RefreshApps();
            bool pending = _snapshot.Any(value => string.Equals(value.Name, app.Name, StringComparison.OrdinalIgnoreCase) && value.State == LiveProcessState.RestorePending);
            _feedback.ForeColor = pending ? UtilityTheme.Warning : UtilityTheme.Muted;
            _feedback.Text = pending ? L.T("Exclusion saved, but some settings still need restoration. Use Restore originals to retry.") :
                L.T("Exclusion preference saved for {0}.", app.Name);
        }
        catch (Exception ex)
        {
            _feedback.ForeColor = UtilityTheme.Warning;
            _feedback.Text = L.T("Could not change the exclusion: {0}", ex.Message);
        }
    }

    private static string StateCaption(LiveProcessState state) => L.T(state switch
    {
        LiveProcessState.Managed => "Managed by EcoMode",
        LiveProcessState.RestorePending => "Needs restoration",
        LiveProcessState.Foreground => "Foreground app",
        LiveProcessState.Excluded => "Excluded from throttling",
        LiveProcessState.Protected => "Protected by Windows",
        LiveProcessState.Unavailable => "Access unavailable",
        _ => "Not managed by EcoMode"
    });

    private static string QosCaption(LiveEcoQos qos) => L.T(qos switch
    {
        LiveEcoQos.On => "ON",
        LiveEcoQos.Off => "OFF",
        LiveEcoQos.SystemManaged => "Windows managed",
        _ => "Unavailable"
    });

    private static string PriorityCaption(Win32Api.PriorityClass? priority) => L.T(priority switch
    {
        Win32Api.PriorityClass.IDLE_PRIORITY_CLASS => "Idle",
        Win32Api.PriorityClass.BELOW_NORMAL_PRIORITY_CLASS => "Below normal",
        Win32Api.PriorityClass.NORMAL_PRIORITY_CLASS => "Normal",
        Win32Api.PriorityClass.ABOVE_NORMAL_PRIORITY_CLASS => "Above normal",
        Win32Api.PriorityClass.HIGH_PRIORITY_CLASS => "High",
        Win32Api.PriorityClass.REALTIME_PRIORITY_CLASS => "Realtime",
        _ => "Unavailable"
    });

    private sealed record AppFilter(string Caption) { public override string ToString() => L.T(Caption); }
}
