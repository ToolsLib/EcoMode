using ecomode;
using ecomode.Interop;
using System.Diagnostics;

internal static partial class Program
{
    private static T Field<T>(LiveAppsForm form, string name) => (T)typeof(LiveAppsForm)
        .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;

    private static void ShowOffscreen(LiveAppsForm form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
    }

    private static void RunLiveAppsTests()
    {
        Run("live process enumeration is query-only and distinguishes tracked changes", LiveProcessReadOnly);
        Run("live exclusions restore all instances and remain after processes exit", LiveExclusionInstances);
        Run("live exclusion saves preserve unrelated unsaved profile edits", LiveExclusionEdits);
        Run("failed live exclusion saves leave runtime and UI unchanged", LiveExclusionSaveFailure);
        Run("built-in exclusions and invalid executable names cannot be changed", LiveExclusionSafety);
        Run("live app filters preserve selection and handle exit and read failures", LiveAppsFiltering);
        Run("live app actions retain pending restoration and surface save errors", LiveAppsActionErrors);
        Run("live app window is reusable from either view and closes with its owner", LiveAppsLifetime);
        Run("live app controls and rows fit in all four languages", LiveAppsLanguages);
    }

    private static void LiveProcessReadOnly()
    {
        using var child = Child();
        EnergyManager.SetBypass([]);
        try
        {
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            var initial = EnergyManager.GetLiveProcesses(false, (uint)child.Id).Single();
            Check(initial.State == LiveProcessState.NotManaged && !initial.Locked && !initial.UserExcluded, "Ordinary child has a false management/exclusion status.");
            VerifyRestored(child, original);
            Toggle(child, true);
            var active = EnergyManager.GetLiveProcesses(true, (uint)child.Id).Single();
            Check(active.State == LiveProcessState.Managed && active.Priority == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS && active.EcoQos == LiveEcoQos.On,
                "Managed process readings do not match native priority/EcoQoS.");
            var pending = EnergyManager.GetLiveProcesses(false, (uint)child.Id).Single();
            Check(pending.State == LiveProcessState.RestorePending && EnergyManager.HasTrackedProcesses &&
                Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, "Refreshing the list restored or lost tracked changes.");
            EnergyManager.UnthrottleAllUserProcesses(); VerifyRestored(child, original);
            var self = EnergyManager.GetLiveProcesses(false, (uint)Environment.ProcessId).Single();
            Check(self.Locked && self.State == LiveProcessState.Excluded, "EcoMode's own process was not locked.");
            child.Kill(); child.WaitForExit();
            Check(EnergyManager.GetLiveProcesses(false, (uint)child.Id).Count == 0, "Exited child remains in the live snapshot.");
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
    }

    private static void LiveExclusionInstances()
    {
        using var first = Child(); using var second = Child();
        var (_, manager, _) = Setup(); string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config);
        try
        {
            first.PriorityClass = second.PriorityClass = ProcessPriorityClass.BelowNormal;
            var originalFirst = Read(first); var originalSecond = Read(second);
            Toggle(first, true); Toggle(second, true);
            form.SetLiveAppExclusion(first.ProcessName + ".exe", true);
            VerifyRestored(first, originalFirst); VerifyRestored(second, originalSecond);
            var row = EnergyManager.GetLiveProcesses(true, (uint)first.Id).Single();
            Check(row.UserExcluded && !row.Locked && row.State == LiveProcessState.Excluded, "User exclusion was not reflected in the live list.");
            Check(AppState.Load(config).BypassProcesses.Contains(first.ProcessName + ".exe"), "Exclusion was not persisted.");
            first.Kill(); first.WaitForExit();
            Check(AppState.Load(config).BypassProcesses.Contains(second.ProcessName + ".exe"), "An exited process lost its executable exclusion.");
            form.SetLiveAppExclusion(second.ProcessName + ".exe", false);
            Check(!AppState.Load(config).BypassProcesses.Any() && !EnergyManager.GetLiveProcesses(false, (uint)second.Id).Single().UserExcluded,
                "Removing an exclusion did not update disk and runtime.");
        }
        finally
        {
            EnergyManager.UnthrottleAllUserProcesses(); EnergyManager.SetBypass([]);
            foreach (var process in new[] { first, second }) if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
        }
    }

    private static void LiveExclusionEdits()
    {
        var (system, manager, originalPath) = Setup(Target("setting")); string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config) { FakeMonitoring = true };
        string before = File.ReadAllText(originalPath);
        Field<CheckBox>(form, "chkEdge").Checked = true;
        Field<TextBoxBase>(form, "txtEcoQosProcs").Text = "unsaved-other-app";
        form.SetLiveAppExclusion("example.exe", true);
        var saved = AppState.Load(config);
        Check(!saved.IncludeEdge && saved.BypassProcesses.SequenceEqual(new[] { "example.exe" }) &&
            Field<CheckBox>(form, "chkEdge").Checked && Field<TextBoxBase>(form, "txtEcoQosProcs").Lines.Contains("unsaved-other-app"),
            "Saving an exclusion saved unrelated controls or discarded unsaved bypass edits.");
        ClickEcoSwitch(form);
        typeof(MainForm).GetField("_ecoHookActive", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, true);
        form.SetLiveAppExclusion("example.exe", false);
        Check(form.Scans.SequenceEqual(new[] { true }), "Removing an exclusion during active Eco Mode did not refresh background apps.");
        Check(File.ReadAllText(originalPath) == before || manager.HasPendingChanges, "Exclusion changes corrupted originals.");
        Check(system.Writes == 1, "Exclusion changes applied profile settings unexpectedly.");
        ClickEcoSwitch(form);
        EnergyManager.SetBypass([]);
    }

    private static void LiveExclusionSaveFailure()
    {
        var (_, manager, _) = Setup(); string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config);
        string before = File.ReadAllText(config); string edited = Field<TextBoxBase>(form, "txtEcoQosProcs").Text;
        File.SetAttributes(config, FileAttributes.ReadOnly);
        try
        {
            Throws(() => form.SetLiveAppExclusion("example.exe", true));
            Check(File.ReadAllText(config) == before && Field<AppState>(form, "_state").BypassProcesses.Length == 0 &&
                !EnergyManager.BypassProcessList.Contains("example.exe") && Field<TextBoxBase>(form, "txtEcoQosProcs").Text == edited,
                "Failed persistence changed runtime or controls.");
        }
        finally { File.SetAttributes(config, FileAttributes.Normal); }
    }

    private static void LiveExclusionSafety()
    {
        var (_, manager, _) = Setup(); string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config);
        string before = File.ReadAllText(config);
        foreach (string name in new[] { "Malwarebytes.exe", "MsMpEng.exe", "msedge.exe", "", "../other.exe", @"C:\other.exe", "app", "bad|name.exe" })
            Throws(() => form.SetLiveAppExclusion(name, false));
        Check(File.ReadAllText(config) == before, "An invalid/safety exclusion edited the profile.");
    }

    private static LiveProcessSnapshot[] DemoLiveApps() => new[]
    {
        new LiveProcessSnapshot(1204, "background-worker.exe", LiveProcessState.Managed, Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, LiveEcoQos.On, false, false),
        new LiveProcessSnapshot(1308, "foreground-editor.exe", LiveProcessState.Foreground, Win32Api.PriorityClass.NORMAL_PRIORITY_CLASS, LiveEcoQos.Off, false, false),
        new LiveProcessSnapshot(1412, "video-call.exe", LiveProcessState.Excluded, Win32Api.PriorityClass.NORMAL_PRIORITY_CLASS, LiveEcoQos.SystemManaged, true, false),
        new LiveProcessSnapshot(1516, "MsMpEng.exe", LiveProcessState.Excluded, null, LiveEcoQos.Unavailable, false, true),
        new LiveProcessSnapshot(1620, "restore-worker.exe", LiveProcessState.RestorePending, Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, LiveEcoQos.On, true, false),
        new LiveProcessSnapshot(1724, "protected-app.exe", LiveProcessState.Protected, null, LiveEcoQos.Unavailable, false, true)
    };

    private static void SelectLiveApp(LiveAppsForm form, uint id)
    {
        var grid = Field<DataGridView>(form, "_grid"); grid.ClearSelection();
        var row = grid.Rows.Cast<DataGridViewRow>().Single(value => ((LiveProcessSnapshot)value.Tag!).Id == id);
        grid.CurrentCell = row.Cells[0]; row.Selected = true;
    }

    private static void LiveAppsFiltering()
    {
        var source = DemoLiveApps(); bool fail = false; int writes = 0;
        using var form = new LiveAppsForm(() => fail ? throw new IOException("Simulated list read failure") : source, (_, _) => writes++);
        ShowOffscreen(form); form.RefreshApps(); var grid = Field<DataGridView>(form, "_grid");
        Check(grid.Rows.Count == source.Length && writes == 0, "Listing changed an exclusion.");
        SelectLiveApp(form, 1204); form.RefreshApps();
        Check(((LiveProcessSnapshot)grid.SelectedRows[0].Tag!).Id == 1204, "Refresh lost the selected process.");
        source = source.Select(value => value.Id == 1204 ? value with { Name = "replacement.exe" } : value).ToArray();
        form.RefreshApps();
        Check(grid.SelectedRows.Count == 0 && !Field<Button>(form, "_exclude").Enabled, "A reused PID silently changed the actionable executable.");
        SelectLiveApp(form, 1204);
        Field<TextBox>(form, "_search").Text = "1412";
        Check(grid.Rows.Count == 1 && grid.SelectedRows.Count == 0 && !Field<Button>(form, "_exclude").Enabled, "Filtering retained a stale actionable selection.");
        Field<TextBox>(form, "_search").Clear();
        Field<ComboBox>(form, "_filter").SelectedIndex = 1; Check(grid.Rows.Count == 1, "Managed filter includes unrelated processes.");
        Field<ComboBox>(form, "_filter").SelectedIndex = 3; Check(grid.Rows.Count == 1, "Restore filter lost pending work.");
        Field<ComboBox>(form, "_filter").SelectedIndex = 0; SelectLiveApp(form, 1204);
        source = source.Where(value => value.Id != 1204).ToArray(); form.RefreshApps();
        Check(!Field<Button>(form, "_exclude").Enabled && grid.SelectedRows.Count == 0, "An exited process left a stale selection.");
        fail = true; form.RefreshApps();
        Check(grid.Rows.Count == 0 && Field<Label>(form, "_summary").Text.Contains("Simulated"), "Read failure left stale rows or hid the error.");
        Check(writes == 0, "Search/filter/refresh modified a process.");
    }

    private static void LiveAppsActionErrors()
    {
        var source = DemoLiveApps(); bool fail = false; string? changed = null;
        using var form = new LiveAppsForm(() => source, (name, excluded) =>
        {
            if (fail) throw new IOException("Simulated save failure");
            changed = name;
            source = source.Select(value => value.Name == name ? value with { UserExcluded = excluded, State = LiveProcessState.RestorePending } : value).ToArray();
        });
        ShowOffscreen(form); SelectLiveApp(form, 1516);
        Check(!Field<Button>(form, "_exclude").Enabled, "Built-in safety exclusion is editable.");
        SelectLiveApp(form, 1204); Field<Button>(form, "_exclude").PerformClick();
        Check(changed == "background-worker.exe" && Field<Label>(form, "_feedback").Text.Contains("still need restoration"), "Partial restoration was reported as complete.");
        fail = true; Field<Button>(form, "_exclude").PerformClick();
        Check(Field<Label>(form, "_feedback").Text.Contains("Simulated save failure"), "Failed action was hidden.");
    }

    private static void LiveAppsLifetime()
    {
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig()); ShowOffscreen(form);
        foreach (var view in new[] { AppView.Simple, AppView.Advanced })
        {
            form.SetView(view); Field<Button>(form, "btnLiveApps").PerformClick();
            var apps = Field<LiveAppsForm>(form, "_liveAppsForm");
            Check(apps.Visible && Field<System.Windows.Forms.Timer>(apps, "_timer").Enabled, "Live list does not open from this view.");
            Field<ToolStripMenuItem>(form, "miLiveApps").PerformClick();
            Check(ReferenceEquals(apps, Field<LiveAppsForm>(form, "_liveAppsForm")), "Opening the list created a second window.");
            apps.WindowState = FormWindowState.Minimized;
            Check(!Field<System.Windows.Forms.Timer>(apps, "_timer").Enabled, "Minimized live list keeps polling.");
            apps.WindowState = FormWindowState.Normal;
            Check(Field<System.Windows.Forms.Timer>(apps, "_timer").Enabled, "Restored live list did not resume polling.");
            Invoke(form, "HideToTray");
            Check(!apps.Visible && !Field<System.Windows.Forms.Timer>(apps, "_timer").Enabled, "Hiding EcoMode left its app list visible or polling.");
            Invoke(form, "RestoreFromTray");
        }
        var previous = Field<LiveAppsForm>(form, "_liveAppsForm"); previous.Close();
        Field<Button>(form, "btnLiveApps").PerformClick();
        Check(previous.IsDisposed && !ReferenceEquals(previous, Field<LiveAppsForm>(form, "_liveAppsForm")), "A closed list could not reopen.");
        form.Dispose();
        Check(Field<LiveAppsForm>(form, "_liveAppsForm").IsDisposed, "Disposing the owner leaked the live window.");
    }

    private static void LiveAppsLanguages()
    {
        try
        {
            foreach (var language in L.Languages)
            {
                L.SetLanguage(language.Code);
                using var form = new LiveAppsForm(DemoLiveApps, (_, _) => { }); ShowOffscreen(form);
                form.Size = form.MinimumSize; form.PerformLayout(); Application.DoEvents();
                SelectLiveApp(form, 1204);
                var action = Field<Button>(form, "_exclude");
                Check(action.Text == L.T("Never throttle this app") && action.Parent!.ClientRectangle.Contains(action.Bounds) &&
                    action.Width >= TextRenderer.MeasureText(action.Text, action.Font).Width, "Translated action is clipped.");
                var grid = Field<DataGridView>(form, "_grid");
                Check(grid.Height >= 200 && grid.Columns[2].HeaderText == L.T("EcoMode status") &&
                    grid.Rows[0].Cells[2].Value!.ToString() == L.T("Managed by EcoMode"), "Translated grid is missing or unusable.");
                L.SetLanguage(language.Code == "en" ? "fr" : "en");
                Check(action.Text == L.T("Never throttle this app"), "An open live list did not update its language.");
                L.SetLanguage(language.Code);
                using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"live-apps-{language.Code}.png"));
            }
        }
        finally { L.SetLanguage("en"); }
    }
}
