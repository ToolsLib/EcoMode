using ecomode.Interop;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ecomode;

public static class EnergyManager
{
    public static readonly HashSet<string> DefaultBypass = new(StringComparer.OrdinalIgnoreCase)
    {
        "ecomode.exe", "msedge.exe", "webviewhost.exe", "chrome.exe", "applicationframehost.exe",
        "taskmgr.exe", "procmon.exe", "procmon64.exe", "widgets.exe", "dwm.exe", "explorer.exe",
        "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe", "sihost.exe",
        "fontdrvhost.exe", "chsime.exe", "ctfmon.exe", "csrss.exe", "smss.exe", "svchost.exe", "wudfrd.exe",
#if DEBUG
        "devenv.exe",
#endif
    };

    private static HashSet<string> _bypass = new(DefaultBypass, StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyCollection<string> BypassProcessList => _bypass;
    public static Action<string, Color?>? Logger { get; set; }
    private static void Log(string message) => Logger?.Invoke(message, null);

    public const string UWPFrameHostApp = "ApplicationFrameHost.exe";
    private static uint _foregroundPid;
    private static readonly uint StateSize = (uint)Marshal.SizeOf<Win32Api.PROCESS_POWER_THROTTLING_STATE>();
    private static readonly Dictionary<uint, ProcessOriginal> Originals = new();
    public static bool HasTrackedProcesses => Originals.Count != 0;

    private sealed record ProcessOriginal(SafeProcessHandle Handle, string Name,
        Win32Api.PriorityClass Priority, Win32Api.PROCESS_POWER_THROTTLING_STATE Throttling);

    public static void SetBypass(IEnumerable<string>? userBypass)
    {
        var merged = new HashSet<string>(DefaultBypass, StringComparer.OrdinalIgnoreCase);
        foreach (string raw in userBypass ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string name = raw.Trim().Trim('"');
            if (name.Length == 0) continue;
            merged.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
        }
        using var self = Process.GetCurrentProcess();
        merged.Add(self.ProcessName + ".exe");
        _bypass = merged;

        // Newly excluded processes may already be throttled. Cleanup always uses
        // the tracked set, regardless of subsequent bypass edits.
        foreach (var pair in Originals.ToArray())
            if (_bypass.Contains(pair.Value.Name)) RestoreProcess(pair.Key, pair.Value);
    }

    private static SafeProcessHandle OpenProcess(uint pid) => new(Win32Api.OpenProcess(
        (uint)(Win32Api.ProcessAccessFlags.QueryLimitedInformation |
               Win32Api.ProcessAccessFlags.SetInformation | Win32Api.ProcessAccessFlags.Synchronize), false, pid), true);

    private static Win32Api.PROCESS_POWER_THROTTLING_STATE ReadThrottling(IntPtr handle)
    {
        var state = new Win32Api.PROCESS_POWER_THROTTLING_STATE
        {
            Version = Win32Api.PROCESS_POWER_THROTTLING_STATE.PROCESS_POWER_THROTTLING_CURRENT_VERSION
        };
        if (!Win32Api.GetProcessInformation(handle, Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref state, StateSize))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return state;
    }

    private static bool ToggleEfficiencyMode(uint pid, bool enable)
    {
        try
        {
            if (Originals.TryGetValue(pid, out var previous) && Win32Api.WaitForSingleObject(previous.Handle.DangerousGetHandle(), 0) == 0)
            {
                // A retained handle identifies the original process even if Windows reuses its PID.
                previous.Handle.Dispose();
                Originals.Remove(pid);
            }

            if (!Originals.TryGetValue(pid, out var original))
            {
                var handle = OpenProcess(pid);
                try
                {
                    if (handle.IsInvalid) return false;
                    string name = GetProcessNameFromHandle(handle.DangerousGetHandle());
                    if (name.Length == 0 || _bypass.Contains(name)) return false;
                    var priority = Win32Api.GetPriorityClass(handle.DangerousGetHandle());
                    if (priority == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                    // Restoring realtime priority can require privileges we do not have.
                    if (priority == Win32Api.PriorityClass.REALTIME_PRIORITY_CLASS) return false;
                    // Leave the process alone if the OS cannot report its original state.
                    original = new(handle, name, priority, ReadThrottling(handle.DangerousGetHandle()));
                    Originals.Add(pid, original);
                }
                finally
                {
                    if (!Originals.ContainsKey(pid)) handle.Dispose();
                }
            }

            if (_bypass.Contains(original.Name))
            {
                RestoreProcess(pid, original);
                return false;
            }

            IntPtr process = original.Handle.DangerousGetHandle();
            var state = ReadThrottling(process);
            var speed = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
            state.ControlMask |= speed;
            state.StateMask = enable ? state.StateMask | speed : state.StateMask & ~speed;
            if (!Win32Api.SetProcessInformation(process, Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref state, StateSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!Win32Api.SetPriorityClass(process, enable ? Win32Api.PriorityClass.IDLE_PRIORITY_CLASS : original.Priority))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return true;
        }
        catch (Exception ex)
        {
            Log($"EcoQoS: could not update process {pid}: {ex.Message}");
            return false;
        }
    }

    private static string GetProcessNameFromHandle(IntPtr handle)
    {
        int capacity = 1024;
        var name = new StringBuilder(capacity);
        return Win32Api.QueryFullProcessImageName(handle, 0, name, ref capacity)
            ? Path.GetFileName(name.ToString()) : string.Empty;
    }

    public static void HandleForegroundEvent(IntPtr hwnd)
    {
        if (Win32Api.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return;
        using (var handle = OpenProcess(pid))
        {
            if (handle.IsInvalid) return;
            if (string.Equals(GetProcessNameFromHandle(handle.DangerousGetHandle()), UWPFrameHostApp, StringComparison.OrdinalIgnoreCase))
            {
                uint hostPid = pid;
                bool found = false;
                Win32Api.EnumChildWindows(hwnd, (child, _) =>
                {
                    if (found || Win32Api.GetWindowThreadProcessId(child, out uint childPid) == 0 || childPid == hostPid) return true;
                    using var childHandle = OpenProcess(childPid);
                    if (childHandle.IsInvalid) return true;
                    pid = childPid;
                    found = true;
                    return false;
                }, IntPtr.Zero);
            }
        }

        // Switching windows within one process must not throttle the foreground app.
        if (pid == _foregroundPid) return;
        uint previousPid = _foregroundPid;
        _foregroundPid = pid;
        if (previousPid != 0) ToggleEfficiencyMode(previousPid, enable: true);
        ToggleEfficiencyMode(pid, enable: false);
    }

    public static void ThrottleAllUserBackgroundProcesses()
    {
        HandleForegroundEvent(Win32Api.GetForegroundWindow());
        using var self = Process.GetCurrentProcess();
        int sessionId = self.SessionId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != sessionId || (uint)process.Id == _foregroundPid || _bypass.Contains(process.ProcessName + ".exe")) continue;
                    if (ToggleEfficiencyMode((uint)process.Id, enable: true))
                        Log($"EcoQoS: throttle -> {process.ProcessName}:{process.Id}");
                }
                catch (InvalidOperationException) { /* process exited */ }
                catch (Win32Exception ex) { Log($"EcoQoS: process unavailable: {ex.Message}"); }
            }
        }
    }

    private static bool RestoreProcess(uint pid, ProcessOriginal original)
    {
        bool success = true;
        IntPtr handle = original.Handle.DangerousGetHandle();
        if (Win32Api.WaitForSingleObject(handle, 0) != 0)
        {
            // Attempt both independently, so a QoS failure cannot block priority restoration.
            try
            {
                var state = ReadThrottling(handle);
                var speed = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                state.ControlMask = (state.ControlMask & ~speed) | (original.Throttling.ControlMask & speed);
                state.StateMask = (state.StateMask & ~speed) | (original.Throttling.StateMask & speed);
                if (!Win32Api.SetProcessInformation(handle, Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref state, StateSize))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception ex)
            {
                success = false;
                Log($"EcoQoS restore failed for {original.Name}:{pid}: {ex.Message}");
            }
            if (!Win32Api.SetPriorityClass(handle, original.Priority))
            {
                success = false;
                Log($"Priority restore failed for {original.Name}:{pid}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }
        }
        if (success)
        {
            Originals.Remove(pid);
            original.Handle.Dispose();
        }
        return success;
    }

    /// <summary>Restore only processes EcoMode changed, including those now on the bypass list.</summary>
    public static bool UnthrottleAllUserProcesses()
    {
        _foregroundPid = 0;
        bool success = true;
        foreach (var pair in Originals.ToArray())
            if (!RestoreProcess(pair.Key, pair.Value)) success = false;
        return success;
    }
}
