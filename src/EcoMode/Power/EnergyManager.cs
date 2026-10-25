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

    private static HashSet<string> _bypass = new(DefaultBypass.Concat(ProcessExclusions.SecurityProcesses), StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _userBypass = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyCollection<string> BypassProcessList => _bypass;
    public static Action<string, Color?>? Logger { get; set; }
    private static void Log(string message) => Logger?.Invoke(message, null);

    public const string UWPFrameHostApp = "ApplicationFrameHost.exe";
    private static uint _foregroundPid;
    private static readonly uint StateSize = (uint)Marshal.SizeOf<Win32Api.PROCESS_POWER_THROTTLING_STATE>();
    private static readonly Dictionary<uint, ProcessOriginal> Originals = new();
    public static bool HasTrackedProcesses => Originals.Count != 0;

    private sealed record ProcessOriginal(SafeProcessHandle Handle, string Name, ProcessAdjustment Adjustment);

    public static void SetBypass(IEnumerable<string>? userBypass)
    {
        var merged = new HashSet<string>(DefaultBypass.Concat(ProcessExclusions.SecurityProcesses), StringComparer.OrdinalIgnoreCase);
        var user = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in userBypass ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string name = raw.Trim().Trim('"');
            if (name.Length == 0) continue;
            user.Add(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
        }
        using var self = Process.GetCurrentProcess();
        merged.Add(self.ProcessName + ".exe");
        merged.UnionWith(user);
        _userBypass = user;
        _bypass = merged;

        // Newly excluded processes may already be throttled. Cleanup always uses
        // the tracked set, regardless of subsequent bypass edits.
        foreach (var pair in Originals.ToArray())
            if (_bypass.Contains(pair.Value.Name)) RestoreProcess(pair.Key, pair.Value);
    }

    private static SafeProcessHandle OpenProcess(uint pid, bool writable = true) => new(Win32Api.OpenProcess(
        (uint)(Win32Api.ProcessAccessFlags.QueryLimitedInformation |
               (writable ? Win32Api.ProcessAccessFlags.SetInformation : 0) | Win32Api.ProcessAccessFlags.Synchronize), false, pid), true);

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
                // Inspect using query-only rights before requesting any write access.
                // Holding this handle also prevents targeting a reused PID during inspection.
                using var inspection = OpenProcess(pid, writable: false);
                if (inspection.IsInvalid) return false;
                string name = GetProcessNameFromHandle(inspection.DangerousGetHandle());
                if (name.Length == 0 || _bypass.Contains(name) || ProcessExclusions.SecurityProcesses.Contains(name)) return false;
                if (!Win32Api.GetProcessProtectionInformation(inspection.DangerousGetHandle(),
                    Win32Api.PROCESS_INFORMATION_CLASS.ProcessProtectionLevelInfo, out var protection,
                    (uint)Marshal.SizeOf<Win32Api.PROCESS_PROTECTION_LEVEL_INFORMATION>()) ||
                    !ProcessExclusions.CanManage(name, protection.ProtectionLevel)) return false;
                var handle = OpenProcess(pid);
                try
                {
                    if (handle.IsInvalid) return false;
                    var adjustment = new ProcessAdjustment(new NativeProcessSettings(handle));
                    // Restoring realtime priority can require privileges we do not have.
                    if (adjustment.OriginalPriority == Win32Api.PriorityClass.REALTIME_PRIORITY_CLASS) return false;
                    original = new(handle, name, adjustment);
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

            original.Adjustment.Apply(enable);
            return true;
        }
        catch (Exception ex)
        {
            Log(L.T("EcoQoS: could not update process {0}: {1}", pid, ex.Message));
            return false;
        }
        finally
        {
            // Failed/no-op writes must not leave an unmodified process pending forever.
            if (Originals.TryGetValue(pid, out var unchanged) && !unchanged.Adjustment.HasChanges)
            {
                Originals.Remove(pid);
                unchanged.Handle.Dispose();
            }
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
        uint pid = GetForegroundProcessId(hwnd);
        if (pid == 0) return;
        // Switching windows within one process must not throttle the foreground app.
        if (pid == _foregroundPid) return;
        uint previousPid = _foregroundPid;
        _foregroundPid = pid;
        if (previousPid != 0) ToggleEfficiencyMode(previousPid, enable: true);
        ToggleEfficiencyMode(pid, enable: false);
    }

    private static uint GetForegroundProcessId(IntPtr hwnd)
    {
        if (Win32Api.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return 0;
        using (var handle = OpenProcess(pid, writable: false))
        {
            if (handle.IsInvalid) return 0;
            if (string.Equals(GetProcessNameFromHandle(handle.DangerousGetHandle()), UWPFrameHostApp, StringComparison.OrdinalIgnoreCase))
            {
                uint hostPid = pid;
                bool found = false;
                Win32Api.EnumChildWindows(hwnd, (child, _) =>
                {
                    if (found || Win32Api.GetWindowThreadProcessId(child, out uint childPid) == 0 || childPid == hostPid) return true;
                    using var childHandle = OpenProcess(childPid, writable: false);
                    if (childHandle.IsInvalid) return true;
                    pid = childPid;
                    found = true;
                    return false;
                }, IntPtr.Zero);
            }
        }

        return pid;
    }

    // Query-only enumeration: opening or refreshing the list must never adjust a process.
    internal static IReadOnlyList<LiveProcessSnapshot> GetLiveProcesses(bool processModeActive, uint? onlyProcess = null)
    {
        var result = new List<LiveProcessSnapshot>();
        using var self = Process.GetCurrentProcess();
        uint foreground = GetForegroundProcessId(Win32Api.GetForegroundWindow());
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    uint pid = (uint)process.Id;
                    if (onlyProcess != null && pid != onlyProcess || process.SessionId != self.SessionId) continue;
                    string name = process.ProcessName + ".exe";
                    bool locked = DefaultBypass.Contains(name) || ProcessExclusions.SecurityProcesses.Contains(name) || pid == (uint)self.Id;
                    bool excluded = _bypass.Contains(name), userExcluded = _userBypass.Contains(name);
                    bool tracked = Originals.TryGetValue(pid, out var original) &&
                        Win32Api.WaitForSingleObject(original.Handle.DangerousGetHandle(), 0) == 258;
                    using var handle = OpenProcess(pid, writable: false);
                    Win32Api.PriorityClass? priority = null;
                    var qos = LiveEcoQos.Unavailable;
                    bool accessible = !handle.IsInvalid;
                    bool protectedProcess = false;
                    if (accessible)
                    {
                        if (Win32Api.WaitForSingleObject(handle.DangerousGetHandle(), 0) != 258) continue;
                        // Verify the image on this handle rather than trusting a potentially reused PID/name pair.
                        string actualName = GetProcessNameFromHandle(handle.DangerousGetHandle());
                        if (!string.Equals(name, actualName, StringComparison.OrdinalIgnoreCase)) continue;
                        var nativePriority = Win32Api.GetPriorityClass(handle.DangerousGetHandle());
                        if (nativePriority != 0) priority = nativePriority;
                        protectedProcess = Win32Api.GetProcessProtectionInformation(handle.DangerousGetHandle(),
                            Win32Api.PROCESS_INFORMATION_CLASS.ProcessProtectionLevelInfo, out var protection,
                            (uint)Marshal.SizeOf<Win32Api.PROCESS_PROTECTION_LEVEL_INFORMATION>()) &&
                            protection.ProtectionLevel != Win32Api.PROTECTION_LEVEL_NONE;
                        try
                        {
                            var state = ReadThrottling(handle.DangerousGetHandle());
                            const Win32Api.ProcessorPowerThrottlingFlags speed = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                            qos = (state.ControlMask & speed) == 0 ? LiveEcoQos.SystemManaged :
                                (state.StateMask & speed) != 0 ? LiveEcoQos.On : LiveEcoQos.Off;
                        }
                        catch (Win32Exception) { /* Protected or exiting process: show unavailable. */ }
                    }
                    var status = tracked && (!processModeActive || excluded) ? LiveProcessState.RestorePending :
                        excluded ? LiveProcessState.Excluded :
                        protectedProcess ? LiveProcessState.Protected : pid == foreground ? LiveProcessState.Foreground :
                        tracked ? LiveProcessState.Managed : !accessible || priority == null && qos == LiveEcoQos.Unavailable ?
                        LiveProcessState.Unavailable : LiveProcessState.NotManaged;
                    result.Add(new(pid, name, status, priority, qos, userExcluded, locked || protectedProcess));
                }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                catch (Win32Exception) { /* Session/name is unavailable. */ }
            }
        }
        return result.OrderBy(value => value.State == LiveProcessState.RestorePending ? 0 : value.State == LiveProcessState.Managed ? 1 : 2)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.Id).ToArray();
    }

    public static void ThrottleAllUserBackgroundProcesses()
        => ScanUserBackgroundProcesses(newProcessesOnly: false);

    internal static void RefreshUserBackgroundProcesses(uint? onlyProcess = null)
        => ScanUserBackgroundProcesses(newProcessesOnly: true, onlyProcess);

    private static void ScanUserBackgroundProcesses(bool newProcessesOnly, uint? onlyProcess = null)
    {
        foreach (var pair in Originals.ToArray())
            if (Win32Api.WaitForSingleObject(pair.Value.Handle.DangerousGetHandle(), 0) == 0) RestoreProcess(pair.Key, pair.Value);
        if (onlyProcess == null) HandleForegroundEvent(Win32Api.GetForegroundWindow());
        using var self = Process.GetCurrentProcess();
        int sessionId = self.SessionId;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (onlyProcess != null && (uint)process.Id != onlyProcess) continue;
                    if (_bypass.Contains(process.ProcessName + ".exe") ||
                        process.SessionId != sessionId || (uint)process.Id == _foregroundPid) continue;
                    uint pid = (uint)process.Id;
                    bool tracked = Originals.ContainsKey(pid);
                    if (newProcessesOnly && tracked) continue;
                    if (ToggleEfficiencyMode(pid, enable: true) && !tracked && Originals.ContainsKey(pid))
                        Log(L.T("EcoQoS: throttle -> {0}:{1}", process.ProcessName, process.Id));
                }
                catch (InvalidOperationException) { /* process exited */ }
                catch (Win32Exception ex) { Log(L.T("EcoQoS: process unavailable: {0}", ex.Message)); }
            }
        }
    }

    private static bool RestoreProcess(uint pid, ProcessOriginal original)
    {
        IntPtr handle = original.Handle.DangerousGetHandle();
        bool success = Win32Api.WaitForSingleObject(handle, 0) == 0 ||
            original.Adjustment.Restore((operation, ex) => Log(L.T("{0} restore failed for {1}:{2}: {3}", operation, original.Name, pid, ex.Message)));
        if (success)
        {
            Originals.Remove(pid);
            original.Handle.Dispose();
        }
        return success;
    }

    private sealed class NativeProcessSettings(SafeProcessHandle handle) : IProcessSettings
    {
        public Win32Api.PriorityClass ReadPriority()
        {
            var priority = Win32Api.GetPriorityClass(handle.DangerousGetHandle());
            if (priority == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            return priority;
        }

        public Win32Api.PROCESS_POWER_THROTTLING_STATE ReadThrottling() => EnergyManager.ReadThrottling(handle.DangerousGetHandle());

        public void WritePriority(Win32Api.PriorityClass priority)
        {
            if (!Win32Api.SetPriorityClass(handle.DangerousGetHandle(), priority))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        public void WriteThrottling(Win32Api.PROCESS_POWER_THROTTLING_STATE state)
        {
            if (!Win32Api.SetProcessInformation(handle.DangerousGetHandle(), Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling, ref state, StateSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
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

    /// <summary>Release in-memory records at exit without claiming restoration succeeded.</summary>
    internal static void ReleaseTrackedProcessHandles()
    {
        _foregroundPid = 0;
        var remaining = Originals.Values.ToArray();
        Originals.Clear();
        foreach (var original in remaining) original.Handle.Dispose();
    }
}
