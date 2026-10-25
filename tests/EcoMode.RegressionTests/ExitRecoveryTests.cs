using ecomode;
using ecomode.Interop;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static void RunExitRecoveryTests()
    {
        Run("Quit anyway closes either view and preserves system recovery across restart", QuitAnywayPreservesRecovery);
        Run("retrying restoration in the exit dialog can finish cleanup and close", RetryExitRecovery);
        Run("repeated failure still allows Quit anyway after a retry", RetryThenQuitAnyway);
        Run("keeping the app open retains process originals and supports a later clean quit", KeepOpenPreservesProcesses);
        Run("blocked process restoration and cleanup exceptions cannot prevent confirmed exit", QuitWithBlockedProcesses);
        Run("corrupt recovery data cannot lock the app open or be overwritten on forced exit", QuitWithCorruptRecovery);
        Run("an inaccessible recovery store cannot prevent startup or confirmed exit", QuitWithInaccessibleRecovery);
        Run("failed Windows shutdown cleanup never prompts or cancels shutdown", FailedShutdownCleanup);
        Run("failed hook removal disables further process changes", FailedHookRemoval);
        Run("exit recovery choices are translated, readable and default to retry or keep open", ExitRecoveryDialog);
    }

    private static void QuitAnywayPreservesRecovery()
    {
        foreach (bool admin in new[] { false, true })
            foreach (var view in new[] { AppView.Simple, AppView.Advanced })
            {
                var (system, manager, path) = Setup(Target("policy", SettingKind.MachineRegistry), Target("power", SettingKind.Power), Target("gpu"));
                manager.Capture(Profile, true);
                manager.ApplyEco(Profile, true);
                if (admin) system.FailWrite = "policy";
                using var form = new TestMainForm(admin, manager, SwitchConfig(), view) { RecoveryResponse = RestoreExitChoice.QuitAnyway };
                ShowOffscreen(form);
                form.Hide(); // Exercise quitting from the tray as well.
                Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
                Check(form.IsDisposed && form.RecoveryPromptCount == 1 && manager.HasPendingChanges,
                    "Confirmed exit did not close or discarded pending system recovery.");
                Check(system.Values["gpu"].Data.GetInt32() == 37, "Process/administrator failures blocked independent user restoration.");
                system.FailWrite = null;
                var reopened = new OriginalSettingsManager(system, _ => { }, path);
                Check(reopened.HasPendingChanges && reopened.Restore(true) && system.Values.Values.All(value => value.Data.GetInt32() == 37),
                    "Saved recovery could not be completed after quitting and reopening.");
            }
    }

    private static void RetryExitRecovery()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig())
        {
            RecoveryResponse = RestoreExitChoice.Retry,
            BeforeRecoveryChoice = () => system.FailWrite = null
        };
        ShowOffscreen(form);
        Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
        Check(form.IsDisposed && form.RecoveryPromptCount == 1 && !manager.HasPendingChanges &&
            !Field<bool>(form, "_exitWithoutRestore") && system.Values["setting"].Data.GetInt32() == 37,
            "Retry did not restore originals and close normally.");
    }

    private static void RetryThenQuitAnyway()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig());
        form.RecoveryChoices.Enqueue(RestoreExitChoice.Retry);
        form.RecoveryChoices.Enqueue(RestoreExitChoice.QuitAnyway);
        ShowOffscreen(form);
        Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
        Check(form.IsDisposed && form.RecoveryPromptCount == 2 && manager.HasPendingChanges &&
            system.Values["setting"].Data.GetInt32() == 1, "A second restore failure blocked exit or falsely marked originals restored.");
    }

    private static SafeHandle[] TrackedHandles()
    {
        var records = (IDictionary)typeof(EnergyManager).GetField("Originals", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return records.Values.Cast<object>().Select(record => (SafeHandle)record.GetType().GetProperty("Handle")!.GetValue(record)!).ToArray();
    }

    private static void KeepOpenPreservesProcesses()
    {
        using var child = Child();
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig()) { ProcessRestore = () => false };
        try
        {
            EnergyManager.SetBypass([]);
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            var original = Read(child);
            Toggle(child, true);
            var handles = TrackedHandles();
            ShowOffscreen(form);
            form.Hide();
            Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
            Check(!form.IsDisposed && form.Visible && !Field<bool>(form, "_reallyExit") &&
                EnergyManager.HasTrackedProcesses && handles.All(handle => !handle.IsClosed),
                "Keeping the app open dropped process recovery or left the app hidden.");
            form.ProcessRestore = null;
            Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
            Check(form.IsDisposed && !EnergyManager.HasTrackedProcesses, "A later quit could not clean up retained process settings.");
            VerifyRestored(child, original);
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); child.Kill(); child.WaitForExit(); }
    }

    private static void QuitWithBlockedProcesses()
    {
        foreach (bool throws in new[] { false, true })
        {
            using var child = Child();
            var (system, manager, _) = Setup(Target("setting"));
            manager.Capture(Profile, false);
            manager.ApplyEco(Profile, false);
            int attempts = 0;
            using var form = new TestMainForm(false, manager, SwitchConfig(), AppView.Simple)
            {
                RecoveryResponse = RestoreExitChoice.QuitAnyway,
                ProcessRestore = () => { attempts++; if (throws) throw new Win32Exception(5); return false; }
            };
            try
            {
                EnergyManager.SetBypass([]);
                child.PriorityClass = ProcessPriorityClass.BelowNormal;
                Toggle(child, true);
                var handles = TrackedHandles();
                Check(handles.Length == 1, "The isolated child was not tracked.");
                ShowOffscreen(form);
                Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
                Check(form.IsDisposed && attempts == 1 && form.RecoveryPromptCount == 1 &&
                    !EnergyManager.HasTrackedProcesses && handles.All(handle => handle.IsClosed),
                    "A blocked process prevented exit, leaked its handle, or triggered writes after confirmation.");
                Check(!manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 37,
                    "Blocked processes prevented system settings from restoring.");
                Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS,
                    "The force-exit test falsely restored the process it was supposed to leave unchanged.");
            }
            finally { EnergyManager.UnthrottleAllUserProcesses(); child.Kill(); child.WaitForExit(); }
        }
    }

    private static void QuitWithCorruptRecovery()
    {
        var (system, manager, path) = Setup(Target("setting"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{corrupt originals");
        using var form = new TestMainForm(false, manager, SwitchConfig()) { RecoveryResponse = RestoreExitChoice.QuitAnyway };
        ShowOffscreen(form);
        Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
        Check(form.IsDisposed && File.ReadAllText(path) == "{corrupt originals" && system.Writes == 0,
            "Unreadable recovery data locked the app open or was overwritten during exit.");
    }

    private static void FailedShutdownCleanup()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig()) { ProcessRestore = () => throw new Win32Exception(5) };
        var closing = new FormClosingEventArgs(CloseReason.WindowsShutDown, false);
        Invoke(form, "OnFormClosing", closing);
        Check(!closing.Cancel && form.RecoveryPromptCount == 0 && form.Warnings.Count == 0 && manager.HasPendingChanges,
            "Failed shutdown cleanup blocked shutdown, prompted, or lost pending recovery.");
    }

    private static void QuitWithInaccessibleRecovery()
    {
        var manager = new InaccessibleRecovery();
        using var form = new TestMainForm(false, manager, SwitchConfig()) { RecoveryResponse = RestoreExitChoice.QuitAnyway };
        ShowOffscreen(form);
        Check(Field<Button>(form, "btnRestoreOriginal").Enabled, "Inaccessible recovery did not offer a retry.");
        Invoke(form, "OnMenuQuit", form, EventArgs.Empty);
        Check(form.IsDisposed && form.RecoveryPromptCount == 1 && manager.RestoreAttempts == 1,
            "An inaccessible recovery store locked the app open or bypassed the confirmation.");
    }

    private sealed class InaccessibleRecovery : IOriginalSettingsManager
    {
        internal int RestoreAttempts;
        public bool HasSnapshot => throw new UnauthorizedAccessException("Test recovery store is inaccessible.");
        public bool HasPendingChanges => throw new UnauthorizedAccessException("Test recovery store is inaccessible.");
        public void EnsureCaptured(AppState profile, bool isAdmin) => throw new UnauthorizedAccessException();
        public void Capture(AppState profile, bool isAdmin) => throw new UnauthorizedAccessException();
        public bool ApplyEco(AppState profile, bool isAdmin) => throw new UnauthorizedAccessException();
        public bool Restore(bool isAdmin, bool restoreAll = false)
        {
            RestoreAttempts++;
            throw new UnauthorizedAccessException("Test recovery store is inaccessible.");
        }
    }

    private static void FailedHookRemoval()
    {
        using var child = StartMode("--window-child");
        var hook = typeof(HookManager).GetField("windowEventHook", BindingFlags.Static | BindingFlags.NonPublic)!;
        var enabled = typeof(HookManager).GetField("eventsEnabled", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check((IntPtr)hook.GetValue(null)! == IntPtr.Zero, "An unexpected real hook is active.");
        try
        {
            var ready = ReadReady(child).Split(' ');
            var window = new IntPtr(long.Parse(ready[1]));
            EnergyManager.SetBypass([]);
            child.PriorityClass = ProcessPriorityClass.BelowNormal;
            Toggle(child, true);
            // An invalid native hook simulates Windows refusing UnhookWinEvent;
            // callbacks below can target only this test's isolated child window.
            hook.SetValue(null, new IntPtr(-1));
            enabled.SetValue(null, true);
            Throws(HookManager.UnsubscribeWindowEvents);
            HookManager.WindowEventCallback(IntPtr.Zero, 3, window, 0, 0, 0, 0);
            Check(Win32Api.GetPriorityClass(child.Handle) == Win32Api.PriorityClass.IDLE_PRIORITY_CLASS && EnergyManager.HasTrackedProcesses,
                "A callback changed process settings after hook removal failed.");
        }
        finally
        {
            hook.SetValue(null, IntPtr.Zero);
            enabled.SetValue(null, false);
            EnergyManager.UnthrottleAllUserProcesses();
            child.Kill();
            child.WaitForExit();
        }
    }

    private static void ExitRecoveryDialog()
    {
        try
        {
            foreach (var language in L.Languages)
            {
                L.SetLanguage(language.Code);
                using var dialog = new RestoreExitDialog();
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(-32000, -32000);
                dialog.Show();
                dialog.PerformLayout();
                Check(dialog.Choice == RestoreExitChoice.KeepOpen && ((Button)dialog.AcceptButton!).Text == L.T("Retry restoration") &&
                    ((Button)dialog.CancelButton!).Text == L.T("Keep EcoMode open"), "The exit dialog defaults to destructive exit or lacks a safe cancel action.");
                Check(dialog.Controls[0].Controls.OfType<Label>().Any(label => label.Text == L.T(RestoreExitDialog.Consequences)),
                    "The exit warning does not explain process limits and saved system recovery.");
                foreach (Control control in dialog.Controls[0].Controls)
                {
                    Check(control.Parent!.ClientRectangle.Contains(control.Bounds), "An exit recovery choice is outside the dialog.");
                    if (control is Label label)
                        Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, "The exit warning is clipped.");
                }
                SaveLocalizationSnapshot(dialog, $"ui-{language.Code}-ExitRecovery.png");
                dialog.Controls[0].Controls.OfType<Button>().Single(button => button.Text == L.T("Quit anyway")).PerformClick();
                Check(dialog.Choice == RestoreExitChoice.QuitAnyway, "The explicit Quit anyway button cannot close the dialog.");
            }
            using var canceled = new RestoreExitDialog();
            canceled.StartPosition = FormStartPosition.Manual;
            canceled.Location = new Point(-32000, -32000);
            canceled.Show();
            ((Button)canceled.CancelButton!).PerformClick();
            Check(canceled.Choice == RestoreExitChoice.KeepOpen && canceled.DialogResult == DialogResult.Cancel,
                "Canceling the exit warning authorized a forced exit.");
            using var retried = new RestoreExitDialog();
            retried.StartPosition = FormStartPosition.Manual;
            retried.Location = new Point(-32000, -32000);
            retried.Show();
            ((Button)retried.AcceptButton!).PerformClick();
            Check(retried.Choice == RestoreExitChoice.Retry, "The default exit-warning action did not request a retry.");
        }
        finally { L.SetLanguage("en"); }
    }
}
