using ecomode.Interop; // or ecomode.Interop
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ecomode // or ecomode
{
    // public unsafe class EnergyManager
    public class EnergyManager
    {
        public static readonly HashSet<string> BypassProcessList = new(StringComparer.OrdinalIgnoreCase)
        {
            // Not ourselves
            "energystar.exe",
            "ecotogglegui.exe", // add your EXE if different

            // Browsers / aware
            "msedge.exe",
            "webviewhost.exe",
            "chrome.exe",

            // UWP frame host
            "applicationframehost.exe",

            // Tools
            "taskmgr.exe",
            "procmon.exe",
            "procmon64.exe",

            // Widgets
            "widgets.exe",

            // Shell
            "dwm.exe",
            "explorer.exe",
            "shellexperiencehost.exe",
            "startmenuexperiencehost.exe",
            "searchhost.exe",
            "sihost.exe",
            "fontdrvhost.exe",

            // IME
            "chsime.exe",
            "ctfmon.exe",

#if DEBUG
            "devenv.exe",
#endif
            // Services
            "csrss.exe",
            "smss.exe",
            "svchost.exe",

            // WUDF
            "wudfrd.exe",
        };

        public const string UWPFrameHostApp = "ApplicationFrameHost.exe";

        private static uint pendingProcPid = 0;
        private static string pendingProcName = "";

        private static readonly IntPtr pThrottleOn;
        private static readonly IntPtr pThrottleOff;
        private static readonly int szControlBlock;

        static EnergyManager()
        {
            szControlBlock = Marshal.SizeOf<Win32Api.PROCESS_POWER_THROTTLING_STATE>();
            pThrottleOn  = Marshal.AllocHGlobal(szControlBlock);
            pThrottleOff = Marshal.AllocHGlobal(szControlBlock);

            var throttleState = new Win32Api.PROCESS_POWER_THROTTLING_STATE
            {
                Version = Win32Api.PROCESS_POWER_THROTTLING_STATE.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask   = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            };
            var unthrottleState = new Win32Api.PROCESS_POWER_THROTTLING_STATE
            {
                Version = Win32Api.PROCESS_POWER_THROTTLING_STATE.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask   = Win32Api.ProcessorPowerThrottlingFlags.None,
            };

            Marshal.StructureToPtr(throttleState,  pThrottleOn,  false);
            Marshal.StructureToPtr(unthrottleState,pThrottleOff, false);
        }

        private static void ToggleEfficiencyMode(IntPtr hProcess, bool enable)
        {
            if (hProcess == IntPtr.Zero) return;

            Win32Api.SetProcessInformation(
                hProcess,
                Win32Api.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                enable ? pThrottleOn : pThrottleOff,
                (uint)szControlBlock);

            // Optional: match mode with a background priority bump
            Win32Api.SetPriorityClass(
                hProcess,
                enable ? Win32Api.PriorityClass.IDLE_PRIORITY_CLASS
                       : Win32Api.PriorityClass.NORMAL_PRIORITY_CLASS);
        }

        private static string GetProcessNameFromHandle(IntPtr hProcess)
        {
            int capacity = 1024;
            var sb = new StringBuilder(capacity);
            if (Win32Api.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                return Path.GetFileName(sb.ToString());
            return string.Empty;
        }

        public static void HandleForegroundEvent(IntPtr hwnd)
        {
            uint procId;
            var windowThreadId = Win32Api.GetWindowThreadProcessId(hwnd, out procId);
            if (windowThreadId == 0 || procId == 0) return;

            var procHandle = Win32Api.OpenProcess(
                (uint)(Win32Api.ProcessAccessFlags.QueryLimitedInformation |
                       Win32Api.ProcessAccessFlags.SetInformation),
                false, procId);
            if (procHandle == IntPtr.Zero) return;

            var appName = GetProcessNameFromHandle(procHandle);

            // UWP hosting scenario: find real app child
            if (string.Equals(appName, UWPFrameHostApp, StringComparison.OrdinalIgnoreCase))
            {
                var found = false;
                Win32Api.EnumChildWindows(hwnd, (innerHwnd, _) =>
                {
                    if (found) return true;
                    if (Win32Api.GetWindowThreadProcessId(innerHwnd, out uint innerProcId) > 0)
                    {
                        if (procId == innerProcId) return true;

                        var innerProcHandle = Win32Api.OpenProcess(
                            (uint)(Win32Api.ProcessAccessFlags.QueryLimitedInformation |
                                   Win32Api.ProcessAccessFlags.SetInformation),
                            false, innerProcId);
                        if (innerProcHandle == IntPtr.Zero) return true;

                        found = true;
                        Win32Api.CloseHandle(procHandle);
                        procHandle = innerProcHandle;
                        procId = innerProcId;
                        appName = GetProcessNameFromHandle(procHandle);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            // Foreground gets unthrottled (boost), previous gets throttled
            bool bypass = !string.IsNullOrEmpty(appName) && BypassProcessList.Contains(appName);
            if (!bypass)
            {
                ToggleEfficiencyMode(procHandle, enable: false); // unthrottle
            }

            if (pendingProcPid != 0)
            {
                var prevProcHandle = Win32Api.OpenProcess(
                    (uint)Win32Api.ProcessAccessFlags.SetInformation, false, pendingProcPid);
                if (prevProcHandle != IntPtr.Zero)
                {
                    ToggleEfficiencyMode(prevProcHandle, enable: true); // throttle previous
                    Win32Api.CloseHandle(prevProcHandle);
                }
                pendingProcPid = 0;
                pendingProcName = string.Empty;
            }

            if (!bypass)
            {
                pendingProcPid = procId;
                pendingProcName = appName;
            }

            Win32Api.CloseHandle(procHandle);
        }

        /// <summary>Throttle everything in the current session except the foreground/pending process and bypass list.</summary>
        public static void ThrottleAllUserBackgroundProcesses()
        {
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.SessionId != currentSessionId) continue;
                    if ((uint)proc.Id == pendingProcPid) continue; // keep foreground unthrottled

                    var name = proc.ProcessName + ".exe";
                    if (BypassProcessList.Contains(name)) continue;

                    var hProcess = Win32Api.OpenProcess(
                        (uint)Win32Api.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                    if (hProcess == IntPtr.Zero) continue;

                    ToggleEfficiencyMode(hProcess, enable: true); // throttle
                    Win32Api.CloseHandle(hProcess);
                }
                catch { /* access denied/race — ignore */ }
            }
        }

        /// <summary>Remove Eco throttling from all user-session processes (used for Eco OFF).</summary>
        public static void UnthrottleAllUserProcesses()
        {
            int currentSessionId = Process.GetCurrentProcess().SessionId;

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.SessionId != currentSessionId) continue;
                    var name = proc.ProcessName + ".exe";
                    if (BypassProcessList.Contains(name)) continue;

                    var hProcess = Win32Api.OpenProcess(
                        (uint)Win32Api.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                    if (hProcess == IntPtr.Zero) continue;

                    ToggleEfficiencyMode(hProcess, enable: false); // unthrottle
                    Win32Api.CloseHandle(hProcess);
                }
                catch { /* ignore */ }
            }

            // Reset pending so next foreground event starts clean
            pendingProcPid = 0;
            pendingProcName = string.Empty;
        }
    }
}
