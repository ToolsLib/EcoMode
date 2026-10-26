using ecomode;
using ecomode.Interop;

internal static partial class Program
{
    private static readonly BatterySnapshot TestBattery = new(true, PowerConnection.Battery, 80, false, TimeSpan.FromHours(4), 12.5);
    private sealed class FakePowerSource : IPowerSource
    {
        internal BatterySnapshot Snapshot = TestBattery;
        internal bool Fail;
        public BatterySnapshot Read() => Fail ? throw new IOException("Power read failed") : Snapshot;
    }

    private static void RunPowerMonitoringTests()
    {
        Run("battery APIs decode absent/unknown batteries and signed discharge rates", BatteryReadings);
        Run("battery history requires a useful discharging window and respects gaps", BatteryDrainHistory);
        Run("battery history is bounded and uses elapsed time through clock changes", BatteryHistoryLimits);
        Run("automatic policy is opt-in and respects manual profiles and overrides", AutomaticPolicy);
        Run("background sweeps run once per minute and after resume", SweepTiming);
        Run("periodic reconciliation catches an isolated new process and preserves its original", ReconcileNewProcess);
        Run("automatic settings persist separately and reject failed saves", AutomaticPreferences);
        Run("automatic battery mode restores on AC without applying profile settings", AutomaticTransitions);
        Run("manual override and profile ownership survive power changes", AutomaticManualOverride);
        Run("pending recovery and failed backup block automatic throttling", AutomaticRecoverySafety);
        Run("quitting prevents further automatic work even when restoration fails", AutomaticExitSafety);
        Run("runtime monitoring survives tray hiding and resume, and stops on exit", PowerMonitoringLifetime);
        Run("battery dashboard and history render in both views and all languages", BatteryDashboardLayouts);
    }

    private static void BatteryReadings()
    {
        Check(WindowsPowerSource.DecodeStatus(255, 255, 255, uint.MaxValue) == BatterySnapshot.Unknown, "Unknown Windows values became readings.");
        var absent = WindowsPowerSource.DecodeStatus(1, 128, 100, 3600);
        Check(absent.HasBattery == false && absent.Percent == null && absent.Remaining == null, "An absent battery has invented readings.");
        var normal = WindowsPowerSource.DecodeStatus(0, 0, 0, 3600);
        Check(normal.HasBattery == true && normal.Percent == 0 && normal.Remaining == TimeSpan.FromHours(1), "Zero percent or a normal battery flag was lost.");
        Check(WindowsPowerSource.DecodeStatus(1, 8, 45, 100).Charging && WindowsPowerSource.DecodeStatus(1, 8, 45, 100).Remaining == null, "Charging shows a discharge estimate.");
        Check(WindowsPowerSource.DecodeStatus(0, 0, 101, uint.MaxValue).Percent == null, "Invalid percent was shown.");
        Check(WindowsPowerSource.DecodeDischargeRate(unchecked((uint)-12500), true) == 12.5, "Signed milliwatts were decoded incorrectly.");
        foreach (uint rate in new[] { 0u, 12000u, 0x80000000u }) Check(WindowsPowerSource.DecodeDischargeRate(rate, true) == null, "Invalid/charging rate became discharge.");
        Check(WindowsPowerSource.DecodeDischargeRate(unchecked((uint)-12000), false) == null, "Idle hardware reports discharge.");
        _ = new WindowsPowerSource().Read(); // Read-only interop smoke check; no battery required on CI.
    }

    private static void AddHistory(BatteryHistory history, int minutes, int percent, bool eco = false)
        => history.Add(TimeSpan.FromMinutes(minutes), DateTimeOffset.Now.AddMinutes(minutes), TestBattery with { Percent = percent }, eco);

    private static void BatteryDrainHistory()
    {
        var history = new BatteryHistory();
        for (int minute = 0; minute < 5; minute++) AddHistory(history, minute, minute < 2 ? 80 : 79);
        Check(history.DrainPercentPerHour == null, "A short window became an estimate.");
        AddHistory(history, 5, 79, true);
        Check(Math.Abs(history.DrainPercentPerHour!.Value - 12) < .001, "Observed drop was not scaled per hour.");
        history.Break(); AddHistory(history, 6, 78);
        Check(history.DrainPercentPerHour == null && history.Samples[^1].BreakBefore, "Sleep was included in observed drain.");
        for (int minute = 7; minute <= 12; minute++) AddHistory(history, minute, 78);
        Check(history.DrainPercentPerHour == null, "Unchanged coarse battery percentage became zero usage.");
        AddHistory(history, 13, 80);
        Check(history.DrainPercentPerHour == null, "A recalibration increase distorted drain.");
        history.Add(TimeSpan.FromMinutes(14), DateTimeOffset.Now, TestBattery with { Charging = true }, false);
        Check(history.DrainPercentPerHour == null, "Charging has a drain estimate.");
        history.Add(TimeSpan.FromMinutes(15), DateTimeOffset.Now, BatterySnapshot.Unknown, false);
        Check(history.DrainPercentPerHour == null && history.Samples[^1].BreakBefore, "Unknown reading did not break history.");
        AddHistory(history, 20, 77);
        Check(history.Samples[^1].BreakBefore && history.DrainPercentPerHour == null, "A missing interval was joined.");
    }

    private static void BatteryHistoryLimits()
    {
        var history = new BatteryHistory();
        for (int second = 0; second < 1000; second++) history.Add(TimeSpan.FromSeconds(second), DateTimeOffset.Now.AddDays(-second), TestBattery, second > 500);
        Check(history.Samples.Count == 512 && history.Samples.Any(s => s.EcoOn) && history.Samples.Any(s => !s.EcoOn), "History cap or Eco markers failed.");
        history.Add(TimeSpan.FromHours(2), DateTimeOffset.Now, TestBattery, false);
        Check(history.Samples.Count == 1 && history.DrainPercentPerHour == null, "History retained samples older than an hour.");
        history.Add(TimeSpan.Zero, DateTimeOffset.Now, TestBattery, false);
        Check(history.Samples.Count == 1, "An elapsed clock reset retained invalid ordering.");
        history.Add(TimeSpan.Zero, DateTimeOffset.Now, TestBattery, true);
        Check(history.Samples.Count == 1 && history.Samples[0].EcoOn, "Same-time sample replacement failed.");
    }

    private static void AutomaticPolicy()
    {
        var policy = new AutomaticEcoPolicy();
        Check(policy.DesiredState(TestBattery, false) == null, "Automatic mode is enabled by default.");
        policy.Enabled = true;
        Check(policy.DesiredState(TestBattery, false) == true, "Battery does not enable automatic mode.");
        policy.Override();
        Check(policy.DesiredState(BatterySnapshot.Unknown, false) == null && policy.ManualOverride, "An unknown reading cleared manual override.");
        Check(policy.DesiredState(TestBattery, false) == null, "Override did not persist on the same power source.");
        var ac = TestBattery with { Connection = PowerConnection.External };
        Check(policy.DesiredState(ac, false) == false && !policy.ManualOverride, "A source change did not reset manual override.");
        Check(policy.DesiredState(TestBattery, true) == null, "Automatic mode replaced a manual profile.");
        Check(policy.DesiredState(ac with { HasBattery = false }, false) == false, "No-battery system activates automation.");
        Check(policy.DesiredState(BatterySnapshot.Unknown, false) == null, "Unknown power status switched modes.");
    }

    private static void SweepTiming()
    {
        var sweep = new ProcessSweepSchedule();
        Check(!sweep.Due(TimeSpan.Zero, false) && sweep.Due(TimeSpan.Zero, true), "Sweep enable/disable failed.");
        Check(!sweep.Due(TimeSpan.FromSeconds(59), true) && sweep.Due(TimeSpan.FromSeconds(60), true), "Sweep does not respect the minute interval.");
        Check(sweep.Due(TimeSpan.FromSeconds(61), true, true) && !sweep.Due(TimeSpan.FromSeconds(62), true), "Resume sweep was not forced or repeated too soon.");
        Check(!sweep.Due(TimeSpan.FromSeconds(62), false) && sweep.Due(TimeSpan.FromSeconds(63), true), "Re-enabling retained an old schedule.");
    }

    private static void ReconcileNewProcess()
    {
        EnergyManager.SetBypass([]);
        using var child = Child();
        try
        {
            child.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            EnergyManager.RefreshUserBackgroundProcesses((uint)child.Id);
            Check(EnergyManager.HasTrackedProcesses && Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, "New child was missed by reconciliation.");
            EnergyManager.RefreshUserBackgroundProcesses((uint)child.Id);
            Check(EnergyManager.UnthrottleAllUserProcesses(), "Reconciled child could not restore.");
            VerifyRestored(child, original);
            EnergyManager.SetBypass([child.ProcessName]);
            EnergyManager.RefreshUserBackgroundProcesses((uint)child.Id);
            VerifyRestored(child, original);
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); EnergyManager.SetBypass([]); child.Kill(); child.WaitForExit(); }
    }

    private static void AutomaticPreferences()
    {
        string path = Path.Combine(_root, "automation.json");
        var preferences = new AutomationPreferences(path);
        Check(!preferences.Load(), "Missing automation preferences opted in.");
        preferences.Save(true); Check(new AutomationPreferences(path).Load(), "Opt-in did not survive reopening.");
        preferences.Save(false); Check(!preferences.Load(), "Opt-out did not persist.");
        File.WriteAllText(path, "{invalid"); Check(!preferences.Load(), "Corrupt preferences opted in.");
        Directory.CreateDirectory(path + ".blocked");
        var (_, manager, _) = Setup();
        using var form = PowerForm(manager, new FakePowerSource(), path + ".blocked");
        Field<CheckBox>(form, "chkAutomatic").Checked = true;
        Check(!Field<AutomaticEcoPolicy>(form, "_automaticPolicy").Enabled && !Field<CheckBox>(form, "chkAutomatic").Checked && form.Scans.Count == 0,
            "Failed preference write activated automatic mode.");
    }

    private static TestMainForm PowerForm(IOriginalSettingsManager manager, FakePowerSource source, string? autoPath = null)
        => new(false, manager, SwitchConfig(), powerSource: source, automationPreferencesPath: autoPath) { FakeMonitoring = true };

    private static void AutomaticTransitions()
    {
        var (system, manager, path) = Setup(Target("setting"));
        var power = new FakePowerSource();
        using var form = PowerForm(manager, power);
        string before = File.ReadAllText(path);
        string profileBefore = File.ReadAllText(Field<string>(form, "_configPath"));
        form.PollPowerAndProcesses();
        Check(form.Scans.Count == 0, "Automatic mode did work before opting in.");
        Field<CheckBox>(form, "chkAutomatic").Checked = true;
        Check(Field<bool>(form, "_automaticProcessMode") && Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.On && form.Scans.SequenceEqual(new[] { false }), "Battery opt-in did not start background mode.");
        form.PollPowerAndProcesses(); Check(form.Scans.Count == 1, "A tick immediately repeated the sweep.");
        form.PollPowerAndProcesses(true); Check(form.Scans.SequenceEqual(new[] { false, true }), "Resume did not reconcile new processes.");
        power.Fail = true; form.PollPowerAndProcesses(); Check(Field<bool>(form, "_ecoEnabled"), "Unknown power data disabled active mode.");
        power.Fail = false; power.Snapshot = TestBattery with { Connection = PowerConnection.External, Charging = true };
        form.PollPowerAndProcesses();
        Check(!Field<bool>(form, "_ecoEnabled") && !Field<bool>(form, "_ecoHookActive") && !manager.HasPendingChanges && system.Writes == 0 && File.ReadAllText(path) == before,
            "Plugging in failed to restore process-only mode or changed profile originals.");
        power.Snapshot = TestBattery; form.PollPowerAndProcesses();
        Field<CheckBox>(form, "chkAutomatic").Checked = false;
        Check(!Field<bool>(form, "_ecoEnabled"), "Disabling automatic mode left throttling active.");
        Check(File.ReadAllText(Field<string>(form, "_configPath")) == profileBefore, "Automatic mode overwrote the saved manual profile.");
    }

    private static void AutomaticManualOverride()
    {
        var (_, manager, _) = Setup();
        var power = new FakePowerSource();
        using var form = PowerForm(manager, power);
        form.PollPowerAndProcesses(); Field<CheckBox>(form, "chkAutomatic").Checked = true;
        ClickEcoSwitch(form); form.PollPowerAndProcesses();
        Check(!Field<bool>(form, "_ecoEnabled"), "Polling undid manual Eco OFF.");
        power.Snapshot = TestBattery with { Connection = PowerConnection.External }; form.PollPowerAndProcesses();
        power.Snapshot = TestBattery; form.PollPowerAndProcesses();
        Check(Field<bool>(form, "_automaticProcessMode"), "Source change did not resume automation.");
        ClickEcoSwitch(form); ClickEcoSwitch(form);
        Check(Field<bool>(form, "_ecoEnabled") && !Field<bool>(form, "_automaticProcessMode"), "Manual profile did not take ownership.");
        power.Snapshot = TestBattery with { Connection = PowerConnection.External }; form.PollPowerAndProcesses();
        Check(Field<bool>(form, "_ecoEnabled") && !Field<bool>(form, "_automaticProcessMode"), "A power transition replaced a manual profile.");
        Invoke(form, "ResumeAutomaticMode");
        Check(!Field<bool>(form, "_ecoEnabled") && !Field<AutomaticEcoPolicy>(form, "_automaticPolicy").ManualOverride, "Resume automatic did not release manual profile ownership.");
    }

    private static void AutomaticRecoverySafety()
    {
        var (system, manager, _) = Setup(Target("setting"));
        using var form = PowerForm(manager, new FakePowerSource());
        manager.ApplyEco(Profile, false); form.PollPowerAndProcesses();
        Field<CheckBox>(form, "chkAutomatic").Checked = true;
        Check(form.Scans.Count == 0 && Field<bool>(form, "_automationBlocked"), "Pending recovery permitted automatic changes.");
        manager.Restore(false); form.PollPowerAndProcesses();
        Check(Field<bool>(form, "_automaticProcessMode"), "Restored journal kept automatic mode blocked.");
        form.ProcessRestore = () => false;
        var source = (FakePowerSource)Field<IPowerSource>(form, "_powerSource");
        source.Snapshot = TestBattery with { Connection = PowerConnection.External }; form.PollPowerAndProcesses();
        Check(Field<AutomaticEcoPolicy>(form, "_automaticPolicy").ManualOverride, "A failed restore was silently retried every tick.");
        form.ProcessRestore = () => true;
        var (failedSystem, failedManager, _) = Setup(Target("fail")); failedSystem.FailRead = "fail";
        using var failed = PowerForm(failedManager, new FakePowerSource());
        failed.PollPowerAndProcesses(); Field<CheckBox>(failed, "chkAutomatic").Checked = true;
        Check(failed.Scans.Count == 0 && failedSystem.Writes == 0, "A failed initial backup permitted automation.");
    }

    private static void AutomaticExitSafety()
    {
        var (_, manager, _) = Setup();
        using var form = PowerForm(manager, new FakePowerSource());
        form.PollPowerAndProcesses(); Field<CheckBox>(form, "chkAutomatic").Checked = true;
        form.ProcessRestore = () => false;
        typeof(MainForm).GetField("_reallyExit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(form, true);
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
        Invoke(form, "OnFormClosing", closing);
        int scans = form.Scans.Count;
        form.PollPowerAndProcesses(true);
        Check(closing.Cancel && !Field<bool>(form, "_exitInProgress") && form.Scans.Count == scans, "Keeping the app open after failed quit restarted automatic throttling.");
        form.ProcessRestore = () => true;
    }

    private static void PowerMonitoringLifetime()
    {
        var (_, manager, _) = Setup();
        var source = new FakePowerSource();
        string preferencePath = Path.Combine(_root, "auto-start.json");
        new AutomationPreferences(preferencePath).Save(true);
        using var form = new TestMainForm(false, manager, SwitchConfig(), powerSource: source,
            automationPreferencesPath: preferencePath, startMonitoring: true)
        { FakeMonitoring = true };
        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.ShowInTaskbar = false;
        form.Show(); Application.DoEvents();
        var timer = Field<System.Windows.Forms.Timer>(form, "_monitorTimer");
        Check(timer.Enabled && Field<bool>(form, "_monitoringStarted") && Field<bool>(form, "_automaticProcessMode"), "Saved opt-in did not start monitoring.");
        Invoke(form, "HideToTray");
        Check(timer.Enabled && !form.Visible, "Tray hiding stopped monitoring.");
        Invoke(form, "OnPowerModeChanged", form, new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Suspend));
        Application.DoEvents(); Check(!timer.Enabled, "Suspend left the timer active.");
        int scans = form.Scans.Count;
        Invoke(form, "OnPowerModeChanged", form, new Microsoft.Win32.PowerModeChangedEventArgs(Microsoft.Win32.PowerModes.Resume));
        Application.DoEvents();
        Check(timer.Enabled && form.Scans.Count == scans + 1 && form.Scans[^1] && Field<BatteryHistory>(form, "_batteryHistory").Samples[^1].BreakBefore,
            "Resume did not mark a history gap and reconcile new processes.");
        Field<ToolStripMenuItem>(form, "miTrayQuit").PerformClick();
        Check(form.IsDisposed && !Field<bool>(form, "_monitoringStarted"), "Quit retained the static power-event subscription.");
        scans = form.Scans.Count;
        form.PollPowerAndProcesses(true);
        Check(form.Scans.Count == scans, "Closed app performed another sweep.");
    }

    private static void BatteryDashboardLayouts()
    {
        try
        {
            foreach (var language in L.Languages)
            {
                L.SetLanguage(language.Code);
                var (_, manager, _) = Setup(); var source = new FakePowerSource();
                using var form = PowerForm(manager, source);
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.ShowInTaskbar = false; form.Show();
                form.PollPowerAndProcesses();
                var history = Field<BatteryHistory>(form, "_batteryHistory");
                for (int minute = 0; minute <= 60; minute++) AddHistory(history, minute, 88 - minute / 5, minute >= 20);
                foreach (var view in new[] { AppView.Advanced, AppView.Simple })
                {
                    form.SetView(view);
                    Invoke(form, "UpdateBatteryDashboard"); form.PerformLayout();
                    var dashboard = Field<Control>(form, "_batteryDashboard");
                    var toggle = Field<Control>(form, "chkAutomatic");
                    Check(toggle.Visible && dashboard.ClientRectangle.Contains(dashboard.RectangleToClient(toggle.RectangleToScreen(toggle.ClientRectangle))), "Battery toggle is clipped.");
                    Check(Field<Label>(form, "lblBattery").Text.Contains("80"), "Battery percentage is missing.");
                    // The offscreen fixture can start taller than a hosted VM's
                    // display. History must not force additional offscreen growth.
                    int maximumHeight = Math.Max(form.Height, Screen.FromControl(form).WorkingArea.Height);
                    Field<Button>(form, "btnBatteryHistory").PerformClick(); form.PerformLayout();
                    Application.DoEvents();
                    var chart = Field<Control>(form, "_batteryChart");
                    Check(chart.Visible && chart.Width > 200 && chart.Height >= 100 && form.Height <= maximumHeight,
                        $"History is hidden or forces offscreen growth: window={form.Size}, allowed height={maximumHeight}, chart={chart.Size}, visible={chart.Visible}.");
                    if (view == AppView.Advanced)
                        Check(Field<Control>(form, "txtGpuApps").Height >= 50 && Field<Control>(form, "txtEcoQosProcs").Height >= 50,
                            "Expanded history leaves too little space to edit app rules.");
                    using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"battery-{language.Code}-{view}.png"));
                    Field<Button>(form, "btnBatteryHistory").PerformClick();
                }
                source.Snapshot = BatterySnapshot.Unknown; form.PollPowerAndProcesses();
                Check(Field<Label>(form, "lblBattery").Text == L.T("Power status unavailable"), "Unknown hardware displayed old readings.");
                source.Snapshot = TestBattery with { HasBattery = false, Percent = null, Remaining = null, DischargeWatts = null }; form.PollPowerAndProcesses();
                Check(!Field<CheckBox>(form, "chkAutomatic").Enabled && Field<Label>(form, "lblBattery").Text == L.T("No battery detected"), "Desktop battery state is misleading.");
            }
        }
        finally { L.SetLanguage("en"); }
    }
}
