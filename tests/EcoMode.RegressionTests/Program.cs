using ecomode;
using ecomode.Interop;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class Program
{
    private static readonly AppState Profile = new();
    private static string _root = "";
    private static int _passed;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--lock-child"))
        {
            using var instance = SingleInstance.TryAcquire(args[1]);
            Console.WriteLine(instance == null ? "BUSY" : "READY");
            if (instance != null) Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        if (args.Contains("--window-child")) return RunWindowChild();
        if (args.Contains("--child"))
        {
            Console.WriteLine("READY");
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        L.SetLanguage("en");
        if (args.Contains("--localization-smoke-test"))
        {
            ResourceCatalogs();
            Console.WriteLine("PASS: all four localization catalogs loaded from the published executable.");
            return 0;
        }

        _root = Path.Combine(Path.GetTempPath(), "EcoMode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            if (args.Contains("--localization-tests-only"))
            {
                RunLocalizationTests();
                Console.WriteLine($"PASS: {_passed} localization checks.");
                return 0;
            }
            Run("repeated Eco ON preserves originals and removes originally absent values", OriginalValues);
            Run("restore survives restart and changes to selected options/apps", Restart);
            Run("new targets extend the baseline without overwriting existing originals", Extend);
            Run("pending changes prevent replacing the baseline", PreventOverwrite);
            Run("capture failure leaves system and saved baseline untouched", CaptureFailure);
            Run("journal is persisted before any mutation", JournalBeforeWrite);
            Run("partial apply is recoverable", PartialApply);
            Run("non-admin restore continues with user settings and retains admin work", Privileges);
            Run("partial restore continues and can be retried", PartialRestore);
            Run("manual restore can reuse a clean saved snapshot", ManualRestore);
            Run("Eco OFF without a snapshot makes no system changes", NoSnapshot);
            Run("corrupt snapshot blocks Eco ON without system changes", CorruptSnapshot);
            Run("registry values retain their data and type through serialization", RegistryTypes);
            Run("Windows registry adapter restores exact types and absent values", NativeRegistry);
            if (args.Contains("--skip-power-read"))
                Console.WriteLine("SKIP: hardware-dependent active power plan read (hosted CI VM).");
            else
                Run("Windows power adapter captures AC/DC values from the active plan", NativePowerRead);
            Run("native process cleanup restores priority and EcoQoS", ProcessCleanup);
            Run("bypass changes restore processes already throttled", BypassCleanup);
            Run("quit warns and restores system settings and throttling; X keeps running", QuitCleanup);
            Run("canceling the quit warning leaves settings unchanged", CancelQuit);
            Run("failed quit restoration warns, stays open, and supports retry", FailedQuit);
            Run("Windows shutdown restores settings without a modal warning", ShutdownCleanup);
            Run("power controls and header fit the default and minimum window sizes", RenderForm);
            RunRecoveryTests(args);
            RunProfilePresetTests();
            RunStartupElevationTests();
            RunProcessSafetyTests();
            RunInterfaceTests();
            RunStartupOriginalsTests();
            RunViewTests();
            RunExitRecoveryTests();
            RunPowerMonitoringTests();
            RunLiveAppsTests();
            RunLocalizationTests();
            Console.WriteLine($"PASS: {_passed} regression checks.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            EnergyManager.UnthrottleAllUserProcesses();
            EnergyManager.Logger = null;
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws(Action action)
    {
        bool threw = false;
        try { action(); } catch { threw = true; }
        Check(threw, "Expected the operation to fail.");
    }

    private static SettingTarget Target(string name, SettingKind kind = SettingKind.UserRegistry) =>
        new(kind, "test", name, SettingValue.From(1, RegistryValueKind.DWord));

    private static (FakeSystem System, OriginalSettingsManager Manager, string Path) Setup(params SettingTarget[] targets)
    {
        var system = new FakeSystem { Targets = targets.ToList() };
        foreach (var target in targets) system.Values[target.Name] = SettingValue.From(37, RegistryValueKind.DWord);
        string path = Path.Combine(_root, Guid.NewGuid().ToString("N"), "original-settings.json");
        return (system, new OriginalSettingsManager(system, _ => { }, path), path);
    }

    private static bool Same(SettingValue left, SettingValue right) => left.Exists == right.Exists && left.Kind == right.Kind &&
        JsonNode.DeepEquals(JsonNode.Parse(left.Data.GetRawText()), JsonNode.Parse(right.Data.GetRawText()));

    private static void OriginalValues()
    {
        var (system, manager, _) = Setup(Target("existing"), Target("absent"));
        system.Values["absent"] = SettingValue.Missing;
        manager.Capture(Profile, true);
        Check(manager.ApplyEco(Profile, true), "Initial apply failed.");
        Check(manager.ApplyEco(Profile, true), "Repeated apply failed.");
        Check(manager.Restore(true), "Restore failed.");
        Check(system.Values["existing"].Data.GetInt32() == 37, "Original was overwritten by Eco ON.");
        Check(!system.Values["absent"].Exists, "Absent policy should be removed on restore.");
        Check(!manager.HasPendingChanges, "Restore should clear pending changes.");
    }

    private static void Restart()
    {
        var (system, manager, path) = Setup(Target("removed-app"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        system.Targets.Clear();
        var restarted = new OriginalSettingsManager(system, _ => { }, path);
        Check(restarted.HasPendingChanges, "Pending work was lost across restart.");
        Check(restarted.Restore(true), "Restore after restart failed.");
        Check(system.Values["removed-app"].Data.GetInt32() == 37, "Removed app was not restored.");
    }

    private static void Extend()
    {
        var (system, manager, _) = Setup(Target("first"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        system.Targets.Add(Target("later"));
        system.Values["later"] = SettingValue.From(81, RegistryValueKind.DWord);
        manager.ApplyEco(Profile, true);
        manager.Restore(true);
        Check(system.Values["first"].Data.GetInt32() == 37 && system.Values["later"].Data.GetInt32() == 81,
            "Extending the baseline lost an original.");
    }

    private static void PreventOverwrite()
    {
        var (_, manager, path) = Setup(Target("setting"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        string before = File.ReadAllText(path);
        Throws(() => manager.Capture(Profile, true));
        Check(File.ReadAllText(path) == before, "Pending snapshot was overwritten.");
    }

    private static void CaptureFailure()
    {
        var (system, manager, path) = Setup(Target("first"));
        manager.Capture(Profile, true);
        string before = File.ReadAllText(path);
        system.Targets.Add(Target("unreadable"));
        system.FailRead = "unreadable";
        Throws(() => manager.ApplyEco(Profile, true));
        Check(system.Writes == 0 && File.ReadAllText(path) == before, "Read failure allowed partial mutations.");
    }

    private static void JournalBeforeWrite()
    {
        var (system, manager, path) = Setup(Target("one"), Target("two"));
        manager.Capture(Profile, true);
        system.BeforeWrite = () => Check(JsonSerializer.Deserialize<OriginalSettings>(File.ReadAllText(path))!
            .Settings.All(s => s.PendingRestore), "A mutation preceded the persistent journal.");
        manager.ApplyEco(Profile, true);
    }

    private static void PartialApply()
    {
        var (system, manager, _) = Setup(Target("one"), Target("two"));
        manager.Capture(Profile, true);
        system.FailWrite = "two";
        Check(!manager.ApplyEco(Profile, true) && manager.HasPendingChanges, "Failed apply was reported as complete.");
        system.FailWrite = null;
        Check(manager.Restore(true), "Could not recover a partial apply.");
        Check(system.Values.Values.All(v => v.Data.GetInt32() == 37), "Partial apply lost originals.");
    }

    private static void Privileges()
    {
        var (system, manager, _) = Setup(Target("machine", SettingKind.MachineRegistry), Target("user"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        Check(!manager.Restore(false), "Admin-only restore should remain pending.");
        Check(system.Values["user"].Data.GetInt32() == 37 && system.Values["machine"].Data.GetInt32() == 1,
            "Restore did not respect elevation or continue with user settings.");
        Check(manager.HasPendingChanges && manager.Restore(true) && !manager.HasPendingChanges, "Elevated retry failed.");
    }

    private static void PartialRestore()
    {
        var (system, manager, _) = Setup(Target("one"), Target("two"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        system.FailWrite = "one";
        Check(!manager.Restore(true) && manager.HasPendingChanges, "Restore failure was lost.");
        Check(system.Values["two"].Data.GetInt32() == 37, "Failure blocked other restores.");
        system.FailWrite = null;
        Check(manager.Restore(true) && !manager.HasPendingChanges, "Restore retry failed.");
    }

    private static void ManualRestore()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        manager.Restore(true);
        system.Values["setting"] = SettingValue.From(99, RegistryValueKind.DWord);
        Check(manager.Restore(true, restoreAll: true), "Manual restore failed.");
        Check(system.Values["setting"].Data.GetInt32() == 37 && manager.HasSnapshot, "Snapshot could not be reused.");
    }

    private static void NoSnapshot()
    {
        var (system, manager, _) = Setup(Target("setting"));
        Check(manager.Restore(true) && system.Writes == 0, "Restore without a snapshot changed settings.");
        Throws(() => manager.ApplyEco(Profile, true));
        Check(system.Writes == 0, "Eco ON changed settings without a snapshot.");
    }

    private static void CorruptSnapshot()
    {
        var (system, manager, path) = Setup(Target("setting"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (string invalid in new[] { "broken JSON", "{}", "{\"Version\":2,\"SavedAt\":\"2026-10-25T00:00:00Z\",\"Settings\":[]}" })
        {
            File.WriteAllText(path, invalid);
            Throws(() => manager.ApplyEco(Profile, true));
        }
        Check(system.Writes == 0, "Corrupt baseline allowed system changes.");
    }

    private static void RegistryTypes()
    {
        SettingValue[] values =
        {
            SettingValue.Missing,
            SettingValue.From(-1, RegistryValueKind.DWord),
            SettingValue.From(long.MaxValue, RegistryValueKind.QWord),
            SettingValue.From("GpuPreference=2;OtherPreference=7;", RegistryValueKind.String),
            SettingValue.From("%TEMP%\\app.exe", RegistryValueKind.ExpandString),
            SettingValue.From(new[] { "a", "b" }, RegistryValueKind.MultiString),
            SettingValue.From(new byte[] { 0, 255, 42 }, RegistryValueKind.Binary),
            SettingValue.From(new byte[] { 1, 2 }, RegistryValueKind.None)
        };
        foreach (var original in values)
        {
            var (system, manager, path) = Setup(Target("value"));
            system.Values["value"] = original;
            manager.Capture(Profile, true);
            manager.ApplyEco(Profile, true);
            new OriginalSettingsManager(system, _ => { }, path).Restore(true);
            Check(Same(system.Values["value"], original), "Value or registry type was lost.");
        }
        // A power entry stores both AC and DC indices, including nondefault boost modes.
        var (power, saved, _) = Setup(Target("power", SettingKind.Power));
        power.Values["power"] = SettingValue.From(new uint[] { 17, 63 });
        saved.Capture(Profile, true);
        saved.ApplyEco(Profile, true);
        saved.Restore(true);
        Check(power.Values["power"].Data[0].GetUInt32() == 17 && power.Values["power"].Data[1].GetUInt32() == 63,
            "Power AC/DC values were not preserved.");
    }

    private static void NativeRegistry()
    {
        string path = @"Software\EcoMode\RegressionTests\" + Guid.NewGuid().ToString("N");
        var windows = new WindowsSystemSettings();
        var target = new SettingTarget(SettingKind.UserRegistry, path, "value", SettingValue.From(1, RegistryValueKind.DWord));
        try
        {
            Check(!RegistryValueAccess.Read(Registry.CurrentUser, target.Path, target.Name).Exists, "New test key should have no original value.");
            SettingValue[] originals =
            {
                SettingValue.From(-1, RegistryValueKind.DWord),
                SettingValue.From(long.MinValue, RegistryValueKind.QWord),
                SettingValue.From("GpuPreference=2;OtherPreference=7;", RegistryValueKind.String),
                SettingValue.From("%TEMP%\\app.exe", RegistryValueKind.ExpandString),
                SettingValue.From(new[] { "a", "b" }, RegistryValueKind.MultiString),
                SettingValue.From(new byte[] { 0, 255, 42 }, RegistryValueKind.Binary),
                SettingValue.From(new byte[] { 1, 2 }, RegistryValueKind.None)
            };
            foreach (var value in originals)
            {
                RegistryValueAccess.Write(Registry.CurrentUser, target.Path, target.Name, value);
                var original = RegistryValueAccess.Read(Registry.CurrentUser, target.Path, target.Name);
                Check(Same(original, value), "Native registry adapter changed a value or its kind.");
                RegistryValueAccess.Write(Registry.CurrentUser, target.Path, target.Name, target.EcoValue);
                RegistryValueAccess.Write(Registry.CurrentUser, target.Path, target.Name, JsonSerializer.Deserialize<SettingValue>(JsonSerializer.Serialize(original))!);
                Check(Same(RegistryValueAccess.Read(Registry.CurrentUser, target.Path, target.Name), original), "Native registry restore did not round-trip.");
            }
            RegistryValueAccess.Write(Registry.CurrentUser, target.Path, target.Name, SettingValue.Missing);
            Check(!RegistryValueAccess.Read(Registry.CurrentUser, target.Path, target.Name).Exists, "Original absence was not restored.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private static void NativePowerRead()
    {
        var windows = new WindowsSystemSettings();
        var profile = new AppState { IncludeEdge = false, IncludeChrome = false, IncludeGpuPref = false, IncludeEcoQos = false };
        var targets = windows.GetTargets(profile, true).ToList();
        Check(targets.Count == 2 && targets.All(t => t.Kind == SettingKind.Power), "Expected EPP and boost targets.");
        foreach (var target in targets)
        {
            var value = windows.Read(target);
            Check(value.Exists && value.Data.GetArrayLength() == 2, "Power capture must include both AC and DC indices.");
            Console.WriteLine($"  Captured power setting {target.Name}: AC={value.Data[0]}, DC={value.Data[1]}");
        }
    }

    private static Process Child()
    {
        var child = StartMode("--child");
        var ready = child.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result != "READY")
        {
            child.Kill();
            child.Dispose();
            throw new Exception("Test child did not start.");
        }
        return child;
    }

    private static Win32Api.PROCESS_POWER_THROTTLING_STATE Read(Process process)
    {
        var state = new Win32Api.PROCESS_POWER_THROTTLING_STATE { Version = 1 };
        Check(Win32Api.GetProcessInformation(process.Handle, Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
            ref state, (uint)Marshal.SizeOf<Win32Api.PROCESS_POWER_THROTTLING_STATE>()), "Cannot read child EcoQoS state.");
        return state;
    }

    private static void Toggle(Process child, bool enabled)
    {
        bool changed = (bool)typeof(EnergyManager).GetMethod("ToggleEfficiencyMode", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { (uint)child.Id, enabled })!;
        Check(changed, "Could not change the isolated test child.");
    }

    private static void VerifyRestored(Process child, Win32Api.PROCESS_POWER_THROTTLING_STATE original)
    {
        Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.BELOW_NORMAL_PRIORITY_CLASS, "Child priority was not restored.");
        var restored = Read(child);
        Check(original.ControlMask == restored.ControlMask && original.StateMask == restored.StateMask, "Child EcoQoS state was not restored.");
        Check(!EnergyManager.HasTrackedProcesses, "Restored process handle was retained.");
    }

    private static void ProcessCleanup()
    {
        using var child = Child();
        try
        {
            EnergyManager.SetBypass(Array.Empty<string>());
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            Toggle(child, true);
            Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, "Child was not throttled.");
            Toggle(child, false);
            Toggle(child, true);
            Check(EnergyManager.UnthrottleAllUserProcesses(), "Child cleanup failed.");
            VerifyRestored(child, original);
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); child.Kill(); child.WaitForExit(); }
    }

    private static void BypassCleanup()
    {
        using var child = Child();
        try
        {
            EnergyManager.SetBypass(Array.Empty<string>());
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            Toggle(child, true);
            EnergyManager.SetBypass(new[] { child.ProcessName });
            VerifyRestored(child, original);
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); child.Kill(); child.WaitForExit(); }
    }

    private static void Invoke(MainForm form, string method, params object[] args) =>
        typeof(MainForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

    private static T Field<T>(MainForm form, string name) =>
        (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static void QuitCleanup()
    {
        using var child = Child();
        var (system, manager, _) = Setup(Target("power", SettingKind.Power),
            Target("policy", SettingKind.MachineRegistry), Target("gpu"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        using var form = new TestMainForm(true, manager);
        try
        {
            ShowOffscreen(form);
            EnergyManager.SetBypass(Array.Empty<string>());
            foreach (string option in new[] { "chkEcoQos", "chkPowerPlan", "chkEdge", "chkChrome", "chkGpuPref" })
                Field<CheckBox>(form, option).Checked = false;
            system.Targets.Clear();
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            Toggle(child, true);
            var minimize = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Invoke(form, "OnFormClosing", minimize);
            Check(minimize.Cancel && EnergyManager.HasTrackedProcesses && manager.HasPendingChanges,
                "X should keep EcoMode and its settings running in the tray.");
            Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
            Check(form.IsDisposed, "Quit did not close the form.");
            Check(form.Warnings.Count == 1 && form.Warnings[0].Contains("power plan") && form.Warnings[0].Contains("browser policies"),
                "Quit must warn about restoring system settings.");
            Check(!manager.HasPendingChanges && system.Values.Values.All(v => v.Data.GetInt32() == 37),
                "Quit did not restore power, registry, and GPU settings.");
            VerifyRestored(child, original);
        }
        finally
        {
            EnergyManager.UnthrottleAllUserProcesses();
            EnergyManager.Logger = null;
            Field<NotifyIcon>(form, "trayIcon").Dispose();
            child.Kill();
            child.WaitForExit();
        }
    }

    private static void ShowOffscreen(MainForm form)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
    }

    private static void CancelQuit()
    {
        var (system, manager, _) = Setup(Target("policy", SettingKind.MachineRegistry));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        using var form = new TestMainForm(true, manager) { QuitResponse = DialogResult.Cancel };
        try
        {
            ShowOffscreen(form);
            int before = system.Writes;
            Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
            Check(!form.IsDisposed && !Field<bool>(form, "_reallyExit") && manager.HasPendingChanges && system.Writes == before,
                "Canceling Quit should leave the app and settings unchanged.");
            Check(form.Warnings.Count == 1, "Quit warning was not displayed.");
        }
        finally
        {
            form.Hide();
            EnergyManager.Logger = null;
            Field<NotifyIcon>(form, "trayIcon").Dispose();
        }
    }

    private static void FailedQuit()
    {
        foreach (bool isAdmin in new[] { true, false })
        {
            var (system, manager, _) = Setup(Target("policy", SettingKind.MachineRegistry), Target("gpu"));
            manager.Capture(Profile, true);
            manager.ApplyEco(Profile, true);
            using var form = new TestMainForm(isAdmin, manager);
            try
            {
                ShowOffscreen(form);
                if (isAdmin) system.FailWrite = "policy";
                Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
                Check(!form.IsDisposed && !Field<bool>(form, "_reallyExit") && manager.HasPendingChanges,
                    "Failed restoration should cancel exit and preserve pending work.");
                Check(form.Warnings.Count >= 2 && form.Warnings.Any(w => w.Contains("Administrator")),
                    "Failed restoration should display an actionable warning.");
                Check(system.Values["gpu"].Data.GetInt32() == 37, "A failed policy restore should not block GPU restoration.");
                Check(Field<NotifyIcon>(form, "trayIcon").Visible, "Tray icon must remain available after failed Quit.");
                system.FailWrite = null;
                if (isAdmin)
                {
                    Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
                    Check(form.IsDisposed && !manager.HasPendingChanges, "Retrying Quit should finish restoration and close.");
                }
            }
            finally
            {
                if (!form.IsDisposed) form.Hide();
                EnergyManager.Logger = null;
                Field<NotifyIcon>(form, "trayIcon").Dispose();
            }
        }
    }

    private static void ShutdownCleanup()
    {
        var (system, manager, _) = Setup(Target("power", SettingKind.Power), Target("policy", SettingKind.MachineRegistry));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        using var form = new TestMainForm(true, manager);
        try
        {
            var shutdown = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
            Invoke(form, "OnFormClosing", shutdown);
            Check(!shutdown.Cancel && form.Warnings.Count == 0 && !manager.HasPendingChanges,
                "Shutdown must restore settings without showing a modal warning.");
            Check(system.Values.Values.All(v => v.Data.GetInt32() == 37), "Shutdown did not restore system settings.");
        }
        finally
        {
            EnergyManager.Logger = null;
            Field<NotifyIcon>(form, "trayIcon").Dispose();
        }
    }

    private sealed class TestMainForm : MainForm
    {
        public DialogResult QuitResponse = DialogResult.OK;
        public readonly List<string> Warnings = new();

        public RestoreExitChoice RecoveryResponse = RestoreExitChoice.KeepOpen;
        public readonly Queue<RestoreExitChoice> RecoveryChoices = new();
        public Action? BeforeRecoveryChoice;
        public int RecoveryPromptCount;
        public Func<bool>? ProcessRestore;
        public bool FakeMonitoring;
        public readonly List<bool> Scans = new();
        public TestMainForm(bool isAdmin, IOriginalSettingsManager manager, string? configPath = null,
            AppView? initialView = AppView.Advanced, string? viewPreferencesPath = null, string? languagePreferencesPath = null,
            IPowerSource? powerSource = null, string? automationPreferencesPath = null, bool startMonitoring = false)
            : base(isAdmin, manager, configPath, viewPreferencesPath ?? Path.Combine(_root, "view-" + Guid.NewGuid().ToString("N") + ".json"), initialView,
                languagePreferencesPath ?? Path.Combine(_root, "language-" + Guid.NewGuid().ToString("N") + ".json"),
                powerSource: powerSource, automationPreferencesPath: automationPreferencesPath ?? Path.Combine(_root, "auto-" + Guid.NewGuid().ToString("N") + ".json"), startMonitoring: startMonitoring)
        { }

        internal override DialogResult ShowExitWarning(string message, string title, MessageBoxButtons buttons)
        {
            Warnings.Add(message);
            return buttons == MessageBoxButtons.OKCancel ? QuitResponse : DialogResult.OK;
        }

        internal override RestoreExitChoice ShowIncompleteRestoreDialog()
        {
            RecoveryPromptCount++;
            Warnings.Add(L.T(RestoreExitDialog.Explanation) + " " + L.T(RestoreExitDialog.Consequences) + " " + L.T(RestoreExitDialog.RecoveryHint));
            BeforeRecoveryChoice?.Invoke();
            return RecoveryChoices.Count > 0 ? RecoveryChoices.Dequeue() : RecoveryResponse;
        }

        internal override bool RestoreProcessSettings() => ProcessRestore?.Invoke() ?? base.RestoreProcessSettings();
        internal override void StartForegroundMonitoring() { if (!FakeMonitoring) base.StartForegroundMonitoring(); }
        internal override void ScanBackgroundProcesses(bool newProcessesOnly)
        {
            if (FakeMonitoring) Scans.Add(newProcessesOnly);
            else base.ScanBackgroundProcesses(newProcessesOnly);
        }
    }

    private static void RenderForm()
    {
        var (_, manager, _) = Setup();
        string configPath = Path.Combine(_root, "render-profile.json");
        ProfilePresets.Get("balanced").CreateState().Save(configPath);
        using var form = new MainForm(false, manager, configPath,
            Path.Combine(_root, "render-view.json"), AppView.Advanced,
            automationPreferencesPath: Path.Combine(_root, "render-auto.json"), startMonitoring: false);
        try
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            foreach (var size in new[] { new Size(1200, 800), new Size(1100, 700) })
            {
                form.Size = size;
                Invoke(form, "OnFormShown", form, EventArgs.Empty);
                form.PerformLayout();
                using (var initial = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(initial, new Rectangle(Point.Empty, form.Size));
                    initial.Save(Path.Combine(AppContext.BaseDirectory, $"ui-{size.Width}.png"));
                }
                foreach (string name in new[] { "btnSaveOriginal", "btnRestoreOriginal", "btnLoadPreset" })
                {
                    var button = Field<Button>(form, name);
                    Check(button.Parent!.ClientRectangle.Contains(button.Bounds), $"{name} is clipped at {size}: {button.Bounds} in {button.Parent.ClientRectangle}.");
                    var surface = Field<SplitContainer>(form, "splitTop").Panel1;
                    var visibleBounds = surface.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
                    Check(surface.ClientRectangle.Contains(visibleBounds), $"{name} is outside the visible control area at {size}.");
                }
                var powerSwitch = Field<EcoModeSwitch>(form, "ecoSwitch");
                var switchArea = Field<SplitContainer>(form, "splitTop").Panel1;
                Check(switchArea.ClientRectangle.Contains(switchArea.RectangleToClient(powerSwitch.RectangleToScreen(powerSwitch.ClientRectangle))),
                    $"Eco switch is clipped at {size}.");
                CheckHeaderLayout(form);
                CheckOptionLayout(form);
                // Windows can add a few pixels to MinimumSize for the current DPI's window frame.
                Check(form.Width <= size.Width + 16 && form.Height <= size.Height + 32,
                    $"The runtime layout unexpectedly enlarged the requested size: {form.Size} instead of {size}.");
                var mainMenu = form.MainMenuStrip!;
                var content = Field<SplitContainer>(form, "splitMain");
                Check(form.RectangleToClient(content.RectangleToScreen(content.ClientRectangle)).Top >= mainMenu.Bottom,
                    "The app content covers the File and Edit menus.");
                var picker = Field<ComboBox>(form, "cboPresets");
                Check(picker.Parent!.ClientRectangle.Contains(picker.Bounds) && picker.Width > 150,
                    $"Preset picker is clipped at {size}.");
                foreach (var preset in ProfilePresets.All)
                {
                    picker.SelectedItem = preset;
                    form.PerformLayout();
                    var description = Field<Label>(form, "lblPresetDescription");
                    Check(description.Text == preset.Description && description.Parent!.ClientRectangle.Contains(description.Bounds),
                        $"Preset description is clipped at {size}.");
                    var lastOption = Field<CheckBox>(form, "chkEcoQos");
                    var viewport = lastOption.Parent!.Parent!;
                    var optionBounds = viewport.RectangleToClient(lastOption.RectangleToScreen(lastOption.ClientRectangle));
                    Check(viewport.ClientRectangle.Contains(optionBounds), $"Options require scrolling for {preset.Name} at {size}.");
                    var restore = Field<Button>(form, "btnRestoreOriginal");
                    var controlArea = Field<SplitContainer>(form, "splitTop").Panel1;
                    Check(controlArea.ClientRectangle.Contains(controlArea.RectangleToClient(restore.RectangleToScreen(restore.ClientRectangle))),
                        $"Recovery controls require scrolling for {preset.Name} at {size}.");
                }
                picker.SelectedItem = ProfilePresets.Get("balanced");
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"ui-{size.Width}.png"));
            }
        }
        finally
        {
            form.Hide();
            EnergyManager.Logger = null;
            Field<NotifyIcon>(form, "trayIcon").Dispose();
        }
    }

    private sealed class FakeSystem : ISystemSettings
    {
        public List<SettingTarget> Targets = new();
        public readonly Dictionary<string, SettingValue> Values = new();
        public string? FailRead;
        public string? FailWrite;
        public Action? BeforeWrite;
        public Action<SettingTarget, SettingValue>? Validator;
        public int Writes;

        public void Validate(SettingTarget target, SettingValue value) => Validator?.Invoke(target, value);

        public IEnumerable<SettingTarget> GetTargets(AppState profile, bool isAdmin) =>
            Targets.Where(t => isAdmin || !t.RequiresAdmin);

        public SettingValue Read(SettingTarget target) => target.Name == FailRead
            ? throw new IOException("Simulated capture failure") : Values[target.Name];

        public void Write(SettingTarget target, SettingValue value)
        {
            BeforeWrite?.Invoke();
            Writes++;
            if (target.Name == FailWrite) throw new IOException("Simulated write failure");
            Values[target.Name] = value;
        }
    }
}
