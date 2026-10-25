using ecomode;
using ecomode.Interop;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class Program
{
    private static void RunRecoveryTests(string[] args)
    {
        Run("profiles normalize null entries and survive save/reload", ProfileNormalization);
        Run("invalid imports preserve the current profile and runtime state", InvalidImports);
        Run("invalid startup profiles fall back without preventing recovery", InvalidStartupProfile);
        Run("snapshot destinations and types are validated before any writes", SnapshotValidation);
        Run("GPU changes preserve unrelated preference fields", GpuPreferenceFields);
        Run("native GPU application preserves extra fields and restores the complete original", NativeGpuRoundTrip);
        Run("a read-only journal cannot block rollback", ReadOnlyRollback);
        Run("failed journal writes prevent Eco ON mutations", JournalWriteFailure);
        Run("unsaved manual-restore failures remain pending in memory", UnsavedManualRestore);
        Run("machine originals survive recovery from another user", SharedMachineRecovery);
        Run("legacy machine journals migrate without losing originals", LegacyMigration);
        Run("failed legacy migration and an unreadable scope do not prevent other rollback", MigrationFailureRecovery);
        Run("unelevated recovery can exit and reopen elevated", ElevationRecovery);
        Run("the global instance lock blocks another process", GlobalInstanceLock);
        Run("abnormal exit releases the global instance lock", GlobalInstanceCrash);
        Run("foreground hooks handle same-process windows and stop on unsubscribe", ForegroundTransitions);
        Run("cleanup handles an exited tracked process", ExitedProcessCleanup);
        if (!args.Contains("--skip-power-read"))
            Run("native power writes and restores stay on a disposable inactive plan", NativePowerRoundTrip);
    }

    private static void ProfileNormalization()
    {
        string path = Path.Combine(_root, "normalized-profile.json");
        var state = AppState.Parse("{\"GpuPrefApps\":null,\"BypassProcesses\":[null,\"  code  \",\"CODE\",\"\"]}");
        Check(state.GpuPrefApps.Length == 0 && state.BypassProcesses.SequenceEqual(new[] { "code" }), "Null or duplicate profile entries survived.");
        state.Save(path);
        var loaded = AppState.Load(path);
        EnergyManager.SetBypass(loaded.BypassProcesses);
        Check(loaded.GpuPrefApps.Length == 0 && loaded.BypassProcesses.Length == 1, "Normalized profile did not round-trip.");
        Throws(() => AppState.Parse("{\"GpuPrefApps\":[\"relative.exe\"]}"));
    }

    private static void InvalidImports()
    {
        string path = Path.Combine(_root, "import-profile.json");
        new AppState { BypassProcesses = new[] { "keep" } }.Save(path);
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, path);
        try
        {
            string before = File.ReadAllText(path);
            foreach (string invalid in new[] { "{broken", "null", "{\"GpuPrefApps\":[42]}", "{\"GpuPrefApps\":[\"relative.exe\"]}" })
            {
                Throws(() => form.ImportProfileJson(invalid));
                Check(File.ReadAllText(path) == before && Field<AppState>(form, "_state").BypassProcesses.Contains("keep"), "Invalid import replaced the existing profile.");
            }
            form.ImportProfileJson("{\"BypassProcesses\":[null,\"valid\"],\"GpuPrefApps\":null}");
            Check(AppState.Load(path).BypassProcesses.SequenceEqual(new[] { "valid" }), "Sanitized import was not saved.");
        }
        finally { EnergyManager.Logger = null; }
    }

    private static void InvalidStartupProfile()
    {
        string path = Path.Combine(_root, "bad-startup-profile.json");
        foreach (string json in new[] { "{broken", "{\"GpuPrefApps\":[\"bad\"]}", "{\"GpuPrefApps\":null,\"BypassProcesses\":[null]}" })
        {
            File.WriteAllText(path, json);
            var (_, manager, _) = Setup();
            using var form = new TestMainForm(false, manager, path);
            Check(Field<AppState>(form, "_state").GpuPrefApps != null, "Startup profile was not normalized.");
            EnergyManager.Logger = null;
        }
    }

    private static void SnapshotValidation()
    {
        var target = new SettingTarget(SettingKind.MachineRegistry, @"SOFTWARE\Policies\Microsoft\Edge",
            "EfficiencyModeEnabled", SettingValue.From(1, RegistryValueKind.DWord));
        var (system, manager, path) = Setup(target);
        system.Values[target.Name] = SettingValue.From(0, RegistryValueKind.DWord);
        system.Validator = SettingRules.Validate;
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        string valid = File.ReadAllText(path);
        Action<JsonNode>[] corruptions =
        {
            n => n["Settings"]![0]!["Target"]!["Path"] = @"SOFTWARE\Unexpected",
            n => n["Settings"]![0]!["Target"]!["Name"] = "UnexpectedValue",
            n => n["Settings"]![0]!["Target"]!["Kind"] = 99,
            n => n["Settings"]![0]!["Original"]!["Kind"] = (int)RegistryValueKind.String,
            n => n["Settings"]![0]!["Original"]!["Data"] = 999,
            n => n["Settings"]![0]!["Original"]!["Data"] = "not a number",
            n => n["Settings"]!.AsArray().Add(n["Settings"]![0]!.DeepClone()),
            n => n["Settings"]![0]!["Original"]!.AsObject().Remove("Exists"),
            n => n["Settings"]![0]!.AsObject().Remove("PendingRestore"),
            n => n["Settings"]![0]!["Target"]!.AsObject().Remove("EcoValue")
        };
        foreach (var corrupt in corruptions)
        {
            var json = JsonNode.Parse(valid)!;
            corrupt(json);
            File.WriteAllText(path, json.ToJsonString());
            int writes = system.Writes;
            var restarted = new OriginalSettingsManager(system, _ => { }, path);
            Throws(() => restarted.Restore(true));
            Throws(() => restarted.ApplyEco(Profile, true));
            Check(system.Writes == writes, "An invalid snapshot caused a settings write.");
        }
        var windows = new WindowsSystemSettings();
        Throws(() => windows.Write(target with { Path = @"SOFTWARE\Unexpected" }, target.EcoValue));
        Throws(() => SettingRules.Validate(target with { Kind = SettingKind.Power }, SettingValue.From(new uint[] { 1, 2 })));
        var power = new SettingTarget(SettingKind.Power, Guid.NewGuid().ToString(), SettingRules.Epp.ToString(), SettingValue.From(new uint[] { 85, 90 }));
        Throws(() => SettingRules.Validate(power, SettingValue.From(new uint[] { 101, 0 })));
        Throws(() => SettingRules.Validate(power, SettingValue.From(new[] { "bad", "data" })));
        Throws(() => SettingRules.Validate(power, SettingValue.Missing));
        var gpu = new SettingTarget(SettingKind.UserRegistry, SettingRules.GpuPath, @"C:\test.exe", SettingValue.From("GpuPreference=1;", RegistryValueKind.String));
        Throws(() => SettingRules.Validate(gpu, SettingValue.From(1, RegistryValueKind.DWord)));
        Throws(() => SettingRules.Validate(gpu with { Name = "relative.exe" }, gpu.EcoValue));
        File.WriteAllText(path, valid);
        Check(manager.Restore(true), "Valid original snapshot could not be restored.");
    }

    private static void GpuPreferenceFields()
    {
        string updated = GpuPreferences.WithPreference("AutoHDREnable=1;GpuPreference=2;VRROptimizeEnable=0;", 1);
        Check(updated.Contains("AutoHDREnable=1;") && updated.Contains("VRROptimizeEnable=0;") && updated.Contains("GpuPreference=1;"), "GPU update lost unrelated fields.");
        Check(!updated.Contains("GpuPreference=2"), "Previous GPU mode remained.");
        Check(GpuPreferences.WithPreference("gpuPreference=2;GpuPreference=0;Custom=7;", 1) == "Custom=7;GpuPreference=1;", "Duplicate GPU preference keys were not replaced.");
    }

    private static void ReadOnlyRollback()
    {
        var (system, manager, path) = Setup(Target("setting"));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Check(manager.Restore(true), "Journal failure blocked a successful rollback.");
            Check(system.Values["setting"].Data.GetInt32() == 37, "Original value was not restored.");
            Check(manager.HasPendingChanges, "On-disk pending markers must remain retryable.");
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Check(manager.Restore(true) && !manager.HasPendingChanges, "Writable journal retry did not clear pending work.");
    }

    private static void NativeGpuRoundTrip()
    {
        string path = @"Software\EcoMode\RegressionTests\" + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(path, writable: true);
        try
        {
            string app = Environment.ProcessPath!;
            var profile = new AppState { IncludePowerPlan = false, IncludeEdge = false, IncludeChrome = false, GpuPrefApps = new[] { app } };
            string original = "AutoHDREnable=1;GpuPreference=2;VRROptimizeEnable=0;";
            RegistryValueAccess.Write(root, SettingRules.GpuPath, app, SettingValue.From(original, RegistryValueKind.String));
            var windows = new WindowsSystemSettings(root);
            var manager = new OriginalSettingsManager(windows, _ => { }, new MemoryStore());
            manager.Capture(profile, false);
            Check(manager.ApplyEco(profile, false), "Native GPU apply failed.");
            string applied = RegistryValueAccess.Read(root, SettingRules.GpuPath, app).Data.GetString()!;
            Check(applied.Contains("GpuPreference=1;") && applied.Contains("AutoHDREnable=1;") && applied.Contains("VRROptimizeEnable=0;"), "Native GPU apply removed unrelated preferences.");
            Check(manager.Restore(false) && RegistryValueAccess.Read(root, SettingRules.GpuPath, app).Data.GetString() == original, "Native GPU original was not restored exactly.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private static void JournalWriteFailure()
    {
        var system = new FakeSystem { Targets = new() { Target("setting") } };
        system.Values["setting"] = SettingValue.From(37, RegistryValueKind.DWord);
        var store = new MemoryStore();
        var manager = new OriginalSettingsManager(system, _ => { }, store);
        manager.Capture(Profile, true);
        store.FailWrites = true;
        Throws(() => manager.ApplyEco(Profile, true));
        Check(system.Writes == 0, "Eco ON changed settings without persisting originals.");
        store.FailWrites = false;
        manager.ApplyEco(Profile, true);
        store.FailWrites = true;
        Check(manager.Restore(true) && system.Values["setting"].Data.GetInt32() == 37, "Rollback required a writable journal.");
        store.FailWrites = false;
        manager.ApplyEco(Profile, true);
        store.FailReads = true;
        Check(manager.Restore(true) && system.Values["setting"].Data.GetInt32() == 37,
            "Cached validated originals were not available when the journal became unreadable.");
    }

    private static void SharedMachineRecovery()
    {
        var system = new FakeSystem { Targets = new() { Target("machine", SettingKind.MachineRegistry), Target("user") } };
        system.Values["machine"] = SettingValue.From(37, RegistryValueKind.DWord);
        system.Values["user"] = SettingValue.From(81, RegistryValueKind.DWord);
        var shared = new MemoryStore();
        var firstUser = new SettingsCoordinator(system, _ => { }, new MemoryStore(), shared);
        firstUser.Capture(Profile, true);
        firstUser.ApplyEco(Profile, true);
        // Simulate a crash and a different account/session reading the same protected journal.
        var secondUser = new SettingsCoordinator(system, _ => { }, new MemoryStore(), shared);
        Check(secondUser.HasPendingChanges, "Another session could not see pending machine changes.");
        secondUser.ApplyEco(Profile, true);
        secondUser.Restore(true);
        Check(system.Values["machine"].Data.GetInt32() == 37, "Another user captured EcoMode's machine value as the original.");
        firstUser.Restore(true);
        Check(system.Values["user"].Data.GetInt32() == 81, "The first user's GPU settings lost their originals.");
        // Reopening after an uninstall/reinstall reuses preserved stores.
        Check(new SettingsCoordinator(system, _ => { }, new MemoryStore(), shared).HasSnapshot, "Preserved machine baseline could not be reused.");
    }

    private static void UnsavedManualRestore()
    {
        var system = new FakeSystem { Targets = new() { Target("setting") } };
        system.Values["setting"] = SettingValue.From(37, RegistryValueKind.DWord);
        var store = new MemoryStore();
        var manager = new OriginalSettingsManager(system, _ => { }, store);
        manager.Capture(Profile, true);
        system.Values["setting"] = SettingValue.From(99, RegistryValueKind.DWord);
        system.FailWrite = "setting";
        store.FailWrites = true;
        Check(!manager.Restore(true, restoreAll: true), "Failed manual restoration was not reported.");
        Check(manager.HasPendingChanges && manager.HasPendingChanges, "Status refresh discarded unsaved restoration work.");
        system.FailWrite = null;
        Check(manager.Restore(true) && system.Values["setting"].Data.GetInt32() == 37, "Pending manual restoration could not be retried.");
    }

    private static void LegacyMigration()
    {
        var system = new FakeSystem { Targets = new() { Target("machine", SettingKind.MachineRegistry), Target("user") } };
        system.Values["machine"] = SettingValue.From(37, RegistryValueKind.DWord);
        system.Values["user"] = SettingValue.From(81, RegistryValueKind.DWord);
        var legacyStore = new MemoryStore();
        var shared = new MemoryStore();
        var legacy = new OriginalSettingsManager(system, _ => { }, legacyStore);
        legacy.Capture(Profile, true);
        legacy.ApplyEco(Profile, true);
        var upgraded = new SettingsCoordinator(system, _ => { }, legacyStore, shared);
        Check(upgraded.Restore(true), "Upgraded app failed to restore legacy originals.");
        Check(system.Values["machine"].Data.GetInt32() == 37 && system.Values["user"].Data.GetInt32() == 81, "Migration lost originals.");
        Check(JsonSerializer.Deserialize<OriginalSettings>(legacyStore.Read())!.Settings.All(s => !s.Target.RequiresAdmin), "Machine entries remained in the user journal.");
        Check(shared.Exists, "Shared machine journal was not persisted before migration completed.");
    }

    private static void ElevationRecovery()
    {
        var (system, manager, path) = Setup(Target("machine", SettingKind.MachineRegistry));
        manager.Capture(Profile, true);
        manager.ApplyEco(Profile, true);
        using (var form = new TestMainForm(false, manager) { RecoveryResponse = RestoreExitChoice.QuitAnyway })
        {
            ShowOffscreen(form);
            Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
            Check(form.IsDisposed && manager.HasPendingChanges, "Recovery exit must close while preserving system originals.");
        }
        var elevated = new OriginalSettingsManager(system, _ => { }, path);
        using var retry = new TestMainForm(true, elevated);
        ShowOffscreen(retry);
        Invoke(retry, "OnMenuQuit", retry, EventArgs.Empty);
        Check(retry.IsDisposed && !elevated.HasPendingChanges && system.Values["machine"].Data.GetInt32() == 37, "Elevated relaunch could not finish recovery.");
    }

    private static void MigrationFailureRecovery()
    {
        var system = new FakeSystem { Targets = new() { Target("machine", SettingKind.MachineRegistry), Target("user") } };
        system.Values["machine"] = SettingValue.From(37, RegistryValueKind.DWord);
        system.Values["user"] = SettingValue.From(81, RegistryValueKind.DWord);
        var legacyStore = new MemoryStore();
        var shared = new MemoryStore { FailWrites = true };
        var legacy = new OriginalSettingsManager(system, _ => { }, legacyStore);
        legacy.Capture(Profile, true);
        legacy.ApplyEco(Profile, true);
        var upgraded = new SettingsCoordinator(system, _ => { }, legacyStore, shared);
        Check(upgraded.Restore(true), "A failed shared-journal migration blocked known legacy rollback.");
        Check(system.Values["machine"].Data.GetInt32() == 37 && system.Values["user"].Data.GetInt32() == 81, "Migration failure lost originals.");
        Check(JsonSerializer.Deserialize<OriginalSettings>(legacyStore.Read())!.Settings.Any(s => s.Target.RequiresAdmin), "Failed migration discarded the source originals.");

        shared.FailWrites = false;
        var userStore = new MemoryStore();
        var coordinator = new SettingsCoordinator(system, _ => { }, userStore, shared);
        coordinator.Capture(Profile, true);
        coordinator.ApplyEco(Profile, true);
        var restarted = new SettingsCoordinator(system, _ => { }, userStore, shared);
        userStore.FailReads = true;
        Check(!restarted.Restore(true), "An unreadable user journal should be reported.");
        Check(system.Values["machine"].Data.GetInt32() == 37, "User journal failure blocked machine rollback.");
        userStore.FailReads = false;
        Check(restarted.Restore(true) && system.Values["user"].Data.GetInt32() == 81, "User recovery retry failed.");
    }

    private static Process StartMode(string mode, params string[] parameters)
    {
        // Native process tests need a different executable name from the test
        // runner, whose name is deliberately excluded by the app's self-bypass.
        // The standalone localization smoke test does not launch child processes.
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "EcoMode.RegressionTests.dll"));
        start.ArgumentList.Add(mode);
        foreach (string parameter in parameters) start.ArgumentList.Add(parameter);
        return Process.Start(start)!;
    }

    private static string ReadReady(Process child)
    {
        var ready = child.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result == null) throw new Exception("Test child did not become ready.");
        return ready.Result;
    }

    private static void GlobalInstanceLock()
    {
        Check(SingleInstance.Name.StartsWith("Global\\", StringComparison.Ordinal), "Production lock must coordinate Windows sessions.");
        string name = @"Global\EcoMode.Tests." + Guid.NewGuid().ToString("N");
        using var owner = SingleInstance.TryAcquire(name);
        Check(owner != null, "Could not acquire test lock.");
        using var child = StartMode("--lock-child", name);
        Check(ReadReady(child) == "BUSY", "Another process acquired the machine-wide lock.");
        Check(child.WaitForExit(10000), "Lock contender did not exit.");
    }

    private static void GlobalInstanceCrash()
    {
        string name = @"Global\EcoMode.Tests." + Guid.NewGuid().ToString("N");
        using var child = StartMode("--lock-child", name);
        try
        {
            Check(ReadReady(child) == "READY", "Child could not acquire test lock.");
            child.Kill();
            child.WaitForExit();
            using var recovered = SingleInstance.TryAcquire(name);
            Check(recovered != null, "Abnormal exit left the machine-wide lock blocked.");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
    }

    private static int RunWindowChild()
    {
        using var first = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
        using var second = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), ShowInTaskbar = false };
        first.Show();
        second.Show();
        Console.WriteLine($"READY {first.Handle.ToInt64()} {second.Handle.ToInt64()}");
        Application.Run(first);
        return 0;
    }

    private static void ForegroundTransitions()
    {
        using var child = StartMode("--window-child");
        try
        {
            var ready = ReadReady(child).Split(' ');
            Check(ready.Length == 3 && ready[0] == "READY", "Child windows were not created.");
            var first = new IntPtr(long.Parse(ready[1]));
            var second = new IntPtr(long.Parse(ready[2]));
            EnergyManager.SetBypass(Array.Empty<string>());
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            HookManager.SubscribeToWindowEvents((uint)child.Id);
            HookManager.WindowEventCallback(IntPtr.Zero, 3, first, 0, 0, 0, 0);
            HookManager.WindowEventCallback(IntPtr.Zero, 3, second, 0, 0, 0, 0);
            Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.BELOW_NORMAL_PRIORITY_CLASS, "Switching windows within a process throttled the foreground app.");
            using var ownWindow = new Form();
            EnergyManager.HandleForegroundEvent(ownWindow.Handle);
            Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, "Previous foreground process was not throttled.");
            HookManager.UnsubscribeWindowEvents();
            HookManager.WindowEventCallback(IntPtr.Zero, 3, first, 0, 0, 0, 0);
            Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS, "Unsubscribed hook still changed processes.");
            EnergyManager.HandleForegroundEvent(first);
            Check(EnergyManager.UnthrottleAllUserProcesses(), "Foreground test cleanup failed.");
            VerifyRestored(child, original);
        }
        finally
        {
            HookManager.UnsubscribeWindowEvents();
            EnergyManager.UnthrottleAllUserProcesses();
            child.Kill();
            child.WaitForExit();
        }
    }

    private static void ExitedProcessCleanup()
    {
        using var child = Child();
        EnergyManager.SetBypass(Array.Empty<string>());
        Toggle(child, true);
        child.Kill();
        child.WaitForExit();
        Check(EnergyManager.UnthrottleAllUserProcesses() && !EnergyManager.HasTrackedProcesses, "Exited process prevented cleanup.");
        Check(EnergyManager.UnthrottleAllUserProcesses(), "Repeated cleanup should be harmless.");
    }

    private static void NativePowerRoundTrip()
    {
        var windows = new WindowsSystemSettings();
        var profile = new AppState { IncludeEdge = false, IncludeChrome = false, IncludeGpuPref = false };
        Guid active = Guid.Parse(windows.GetTargets(profile, true).First().Path);
        Check(PowerDuplicateScheme(IntPtr.Zero, in active, out IntPtr pointer) == 0, "Could not create disposable power plan.");
        Guid copy;
        try { copy = Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
        Check(copy != active, "Power API did not create a separate plan.");
        try
        {
            foreach (var target in windows.GetTargets(profile, true))
            {
                var disposable = target with { Path = copy.ToString() };
                var original = windows.Read(disposable);
                windows.Write(disposable, disposable.EcoValue);
                Check(Same(windows.Read(disposable), disposable.EcoValue), "Native AC/DC write failed.");
                windows.Write(disposable, original);
                Check(Same(windows.Read(disposable), original), "Native AC/DC restore failed.");
            }
            Check(Guid.Parse(windows.GetTargets(profile, true).First().Path) == active, "Power test switched the active plan.");
        }
        finally { Check(PowerDeleteScheme(IntPtr.Zero, in copy) == 0, "Could not remove the disposable power plan."); }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerDuplicateScheme(IntPtr root, in Guid source, out IntPtr destination);
    [DllImport("powrprof.dll")]
    private static extern uint PowerDeleteScheme(IntPtr root, in Guid scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);

    private sealed class MemoryStore : ISettingsSnapshotStore
    {
        private string? _json;
        public bool FailReads;
        public bool FailWrites;
        public string Description => "test snapshot";
        public bool Exists => _json != null;
        public string Read() => FailReads ? throw new IOException("Simulated unavailable journal") : _json!;
        public void Write(string json)
        {
            if (FailWrites) throw new IOException("Simulated full or read-only storage");
            _json = json;
        }
    }
}
