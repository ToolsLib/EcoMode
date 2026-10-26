using ecomode;
using Microsoft.Win32;

internal static partial class Program
{
    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        internal bool Enabled { get; set; }
        internal bool UsesCurrentCopy { get; set; } = true;
        internal bool FailRead { get; set; }
        internal bool FailWrite { get; set; }
        internal int Writes { get; private set; }
        public StartupRegistrationStatus Read() => FailRead ? throw new UnauthorizedAccessException("Startup read denied") : new(Enabled, UsesCurrentCopy);
        public void SetEnabled(bool enabled)
        {
            if (FailWrite) throw new UnauthorizedAccessException("Startup write denied");
            Enabled = enabled; UsesCurrentCopy = true; Writes++;
        }
    }

    private static void RunWindowsStartupTests()
    {
        Run("startup commands quote apphost and framework paths without persisting one-off arguments", WindowsStartupCommands);
        Run("Windows startup registration changes only its own disposable registry value", WindowsStartupRegistry);
        Run("startup menu toggles stay synchronized and independent of profiles and originals", WindowsStartupMenus);
        Run("failed startup reads or writes retain a usable setting and permit retry", WindowsStartupFailures);
        Run("automatic startup skips elevation and duplicate-instance prompts", QuietWindowsStartup);
        Run("sign-in startup defers first-run view selection without overwriting preferences", DeferredStartupView);
        Run("sign-in startup has no visible-window flash and tray Show and Quit work", HiddenStartupLifetime);
        Run("hidden startup honors battery automation without applying the manual profile", HiddenStartupAutomation);
        Run("failed restoration from hidden startup opens recovery and supports retry", HiddenStartupRecovery);
        Run("hidden Windows shutdown restores settings without showing prompts", HiddenStartupShutdown);
        Run("startup menu captions and descriptions update in every language", WindowsStartupLanguages);
    }

    private static void WindowsStartupCommands()
    {
        const string app = @"C:\Program Files\Eco Mode & Test\ecomode.exe";
        Check(StartupLaunch.CreateCommand(app, [app, "--one-off", "unused"]) == '"' + app + "\" --startup", "Apphost startup lost quoting or persisted one-off arguments.");
        const string host = @"C:\Program Files\dotnet\dotnet.exe";
        const string assembly = @"C:\Eco Mode\ecomode.dll";
        Check(StartupLaunch.CreateCommand(host, [assembly, "--one-off"]) == '"' + host + "\" \"" + assembly + "\" --startup", "Framework startup lost an argument boundary.");
        Check(StartupLaunch.CreateCommand(host, ["ecomode.dll"]).Contains(Path.GetFullPath("ecomode.dll")), "Relative framework assembly path was not made absolute.");
        foreach (string path in new[] { "relative.exe", "C:\\bad\"path\\app.exe", "C:\\bad\npath\\app.exe", "C:\\" + new string('a', 255) + ".exe" })
            Throws(() => StartupLaunch.CreateCommand(path, []));
        Throws(() => StartupLaunch.CreateCommand(host, []));
        Check(StartupLaunch.IsAutomatic(["--STARTUP"]) && !StartupLaunch.IsAutomatic(["--startup-other"]) && !StartupLaunch.IsAutomatic([]), "Startup switch parsing accepted an unrelated launch.");
    }

    private static void WindowsStartupRegistry()
    {
        string path = @"Software\EcoMode\RegressionTests\Startup\" + Guid.NewGuid().ToString("N");
        using var root = Registry.CurrentUser.CreateSubKey(path, writable: true);
        const string command = "\"C:\\Eco Mode\\ecomode.exe\" --startup";
        var registration = new WindowsStartupRegistration(root, "Run", () => command);
        try
        {
            Check(!registration.Read().Enabled && root.OpenSubKey("Run") == null, "Reading a missing setting created a startup entry.");
            registration.SetEnabled(false);
            using (var run = root.CreateSubKey("Run")) run.SetValue("AnotherApp", "preserve me");
            registration.SetEnabled(true);
            using (var run = root.OpenSubKey("Run")!)
            {
                Check((string)run.GetValue("EcoMode")! == command && run.GetValueKind("EcoMode") == RegistryValueKind.String, "Startup command or registry type was incorrect.");
                Check((string)run.GetValue("AnotherApp")! == "preserve me", "Another startup entry was modified.");
            }
            Check(registration.Read() is { Enabled: true, UsesCurrentCopy: true }, "Registration did not survive reading it again.");
            var moved = new WindowsStartupRegistration(root, "Run", () => "\"C:\\Moved\\ecomode.exe\" --startup");
            Check(moved.Read() is { Enabled: true, UsesCurrentCopy: false }, "A different registered copy was silently accepted or overwritten.");
            registration.SetEnabled(false);
            using (var run = root.OpenSubKey("Run")!) Check(run.GetValue("EcoMode") == null && run.GetValue("AnotherApp") != null, "Disabling startup removed another entry or the Run key.");
            var invalid = new WindowsStartupRegistration(root, "Run", () => throw new InvalidOperationException("Invalid startup command"));
            Throws(() => invalid.SetEnabled(true));
            Check(!registration.Read().Enabled, "Invalid command created a startup entry.");
            registration.SetEnabled(true);
            Check(invalid.Read() is { Enabled: true, UsesCurrentCopy: false }, "Invalid current location hid an existing startup entry.");
            invalid.SetEnabled(false);
            Check(!registration.Read().Enabled, "Invalid current location prevented disabling startup.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private static void WindowsStartupMenus()
    {
        var backend = new FakeStartupRegistration(); var (system, manager, originalPath) = Setup(Target("setting"));
        string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config, startupRegistration: backend);
        string profile = File.ReadAllText(config), originals = File.ReadAllText(originalPath);
        var file = Field<ToolStripMenuItem>(form, "miStartWithWindows"); var tray = Field<ToolStripMenuItem>(form, "miTrayStartWithWindows");
        Check(!file.Checked && !tray.Checked && backend.Writes == 0, "Startup was enabled without opting in.");
        Field<CheckBox>(form, "chkEdge").Checked = true;
        file.PerformClick();
        Check(file.Checked && tray.Checked && backend.Enabled && backend.Writes == 1 && !Field<bool>(form, "_ecoEnabled"), "Startup toggle did not synchronize or enabled Eco Mode.");
        Check(File.ReadAllText(config) == profile && File.ReadAllText(originalPath) == originals && system.Writes == 0 && Field<CheckBox>(form, "chkEdge").Checked,
            "Startup preference saved unrelated edits or changed originals.");
        form.SetView(AppView.Simple); form.ImportProfileJson("{\"GpuPrefApps\":[],\"IncludeEcoQos\":false}"); form.LoadPreset("balanced");
        Check(backend.Enabled && backend.Writes == 1 && file.Checked && tray.Checked, "Changing profile or view reset startup.");
        tray.PerformClick();
        Check(!file.Checked && !tray.Checked && !backend.Enabled && backend.Writes == 2, "Tray menu could not disable startup.");
    }

    private static void WindowsStartupFailures()
    {
        var backend = new FakeStartupRegistration { FailRead = true }; var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig(), startupRegistration: backend);
        var item = Field<ToolStripMenuItem>(form, "miStartWithWindows");
        Check(!item.Checked && item.Enabled && item.ToolTipText?.Contains("read denied") == true, "Failed read prevented startup or hid the error.");
        backend.FailRead = false; backend.FailWrite = true; item.PerformClick();
        Check(!item.Checked && !backend.Enabled && backend.Writes == 0, "Failed registration was shown as enabled.");
        backend.FailWrite = false; item.PerformClick();
        Check(item.Checked && backend.Enabled, "Failed registration could not be retried.");
        backend.FailWrite = true; item.PerformClick();
        Check(item.Checked && backend.Enabled && Field<ToolStripMenuItem>(form, "miTrayStartWithWindows").Checked, "Failed disable falsely cleared the setting.");
        backend.FailWrite = false; item.PerformClick();
    }

    private static void QuietWindowsStartup()
    {
        string name = StartupMutex();
        using var instance = StartupElevation.AcquireInstance(false, () => throw new Exception("Sign-in launch requested elevation."),
            (_, _) => throw new Exception("Sign-in launch displayed a dialog."), name,
            _ => throw new Exception("Sign-in launch attempted an elevated relaunch."), automaticStartup: true);
        Check(instance != null, "Quiet sign-in launch did not acquire its instance lock.");
        using var duplicate = StartupElevation.AcquireInstance(false, () => throw new Exception("Duplicate sign-in launch requested elevation."),
            (_, _) => throw new Exception("Duplicate sign-in launch displayed a notification."), name, automaticStartup: true);
        Check(duplicate == null, "Quiet startup created a duplicate instance.");
    }

    private static void DeferredStartupView()
    {
        string path = Path.Combine(_root, "deferred-startup-view.json"); var preference = new ViewPreferences(path);
        var view = StartupLaunch.SelectView(true, preference, () => throw new Exception("Sign-in launch showed a chooser."), _ => throw new Exception("Unexpected preference write."));
        Check(view == AppView.Simple && !File.Exists(path), "Quiet first launch overwrote or persisted an unchosen view.");
        Check(StartupLaunch.SelectView(false, preference, () => AppView.Advanced, _ => { }) == AppView.Advanced && preference.Load() == AppView.Advanced,
            "Manual launch did not retain the normal first-run chooser.");
        string before = File.ReadAllText(path);
        Check(StartupLaunch.SelectView(true, preference, () => throw new Exception("Saved view was ignored."), _ => { }) == AppView.Advanced && File.ReadAllText(path) == before,
            "Automatic launch ignored or rewrote the saved view.");
    }

    private static void HiddenStartupLifetime()
    {
        var backend = new FakeStartupRegistration { Enabled = true }; var (system, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig(), startMonitoring: true, startupRegistration: backend, startInTray: true);
        int shown = 0, visible = 0; form.Shown += (_, _) => shown++; form.VisibleChanged += (_, _) => { if (form.Visible) visible++; };
        form.Show(); Application.DoEvents();
        Check(!form.Visible && shown == 0 && visible == 0 && form.IsHandleCreated && Field<NotifyIcon>(form, "trayIcon").Visible &&
            Field<System.Windows.Forms.Timer>(form, "_monitorTimer").Enabled && !Field<bool>(form, "_ecoEnabled") && system.Writes == 0,
            "Sign-in launch flashed a window, lacked a tray handle, or applied Eco Mode without opting in.");
        Field<ToolStripMenuItem>(form, "miTrayShow").PerformClick(); Application.DoEvents();
        Check(form.Visible && shown == 1 && visible == 1, "Tray Show could not open the initially hidden main window.");
        Invoke(form, "HideToTray"); Field<ToolStripMenuItem>(form, "miTrayQuit").PerformClick();
        Check(form.IsDisposed && !Field<bool>(form, "_monitoringStarted") && backend.Enabled && backend.Writes == 0,
            "Tray Quit leaked monitoring or erased the sign-in preference.");
    }

    private static void HiddenStartupAutomation()
    {
        var (system, manager, originalPath) = Setup(Target("setting")); var source = new FakePowerSource();
        string autoPath = Path.Combine(_root, "hidden-startup-automatic.json"); new AutomationPreferences(autoPath).Save(true);
        using var form = new TestMainForm(false, manager, SwitchConfig(), powerSource: source, automationPreferencesPath: autoPath,
            startMonitoring: true, startInTray: true)
        { FakeMonitoring = true };
        string before = File.ReadAllText(originalPath); form.Show(); Application.DoEvents();
        Check(!form.Visible && Field<bool>(form, "_automaticProcessMode") && form.Scans.Count == 1 && system.Writes == 0 && File.ReadAllText(originalPath) == before,
            "Hidden launch failed to honor background automation or changed manual profile settings.");
        Field<ToolStripMenuItem>(form, "miTrayQuit").PerformClick();
        Check(form.IsDisposed && !manager.HasPendingChanges, "An initially hidden app could not quit and restore.");
    }

    private static void HiddenStartupRecovery()
    {
        var (system, manager, _) = Setup(Target("setting")); manager.Capture(Profile, false); manager.ApplyEco(Profile, false); system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig(), startMonitoring: true, startInTray: true) { FakeMonitoring = true };
        form.Show(); Application.DoEvents();
        Check(!form.Visible && manager.HasPendingChanges && !Field<bool>(form, "_ecoEnabled"), "Hidden launch discarded pending recovery or enabled Eco Mode.");
        Field<ToolStripMenuItem>(form, "miTrayQuit").PerformClick();
        Check(form.Visible && !form.IsDisposed && form.RecoveryPromptCount == 1 && manager.HasPendingChanges,
            "Failed restoration from the hidden window did not expose recovery.");
        system.FailWrite = null; Field<ToolStripMenuItem>(form, "miTrayQuit").PerformClick();
        Check(form.IsDisposed && !manager.HasPendingChanges, "Recovery from sign-in startup could not finish.");
    }

    private static void HiddenStartupShutdown()
    {
        var (system, manager, _) = Setup(Target("setting")); manager.Capture(Profile, false); manager.ApplyEco(Profile, false);
        using var form = new TestMainForm(false, manager, SwitchConfig(), startInTray: true);
        form.Show();
        var closing = new FormClosingEventArgs(CloseReason.WindowsShutDown, false); Invoke(form, "OnFormClosing", closing);
        Check(!closing.Cancel && !form.Visible && !manager.HasPendingChanges && form.Warnings.Count == 0 && system.Values["setting"].Data.GetInt32() == 37,
            "Hidden shutdown prompted, opened a window or failed to restore.");
    }

    private static void WindowsStartupLanguages()
    {
        try
        {
            var backend = new FakeStartupRegistration { Enabled = true, UsesCurrentCopy = false }; var (_, manager, _) = Setup();
            using var form = new TestMainForm(false, manager, SwitchConfig(), startupRegistration: backend);
            foreach (var language in L.Languages)
            {
                form.SetLanguage(language.Code);
                foreach (string name in new[] { "miStartWithWindows", "miTrayStartWithWindows" })
                {
                    var item = Field<ToolStripMenuItem>(form, name);
                    Check(item.Checked && item.Text == L.T("Start with Windows") && item.ToolTipText == L.T("Startup uses another copy. Turn this option off and on to use this copy."),
                        "Startup label, checked state, or location hint did not update with the language.");
                }
            }
            Check(backend.Writes == 0, "Changing the UI language altered the startup registration.");
        }
        finally { L.SetLanguage("en"); }
    }
}
