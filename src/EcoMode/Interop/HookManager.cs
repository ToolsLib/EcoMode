using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ecomode.Interop
{
    internal class HookManager
    {
        private const int WINEVENT_OUTOFCONTEXT = 0;

        private const int EVENT_SYSTEM_FOREGROUND = 3;

        private static IntPtr windowEventHook;
        private static bool eventsEnabled;
        // Root the delegate for the entire lifetime of the native hook.
        private static readonly WinEventProc hookProcDelegate = WindowEventCallback;

        public static void SubscribeToWindowEvents(uint processId = 0)
        {
            if (windowEventHook == IntPtr.Zero)
            {
                windowEventHook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND, // eventMin
                    EVENT_SYSTEM_FOREGROUND, // eventMax
                    IntPtr.Zero,             // hmodWinEventProc
                    hookProcDelegate,        // lpfnWinEventProc
                    processId,               // tests can restrict events to an isolated child
                    0,                       // idThread
                    WINEVENT_OUTOFCONTEXT);

                if (windowEventHook == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            eventsEnabled = true;
        }

        public static void UnsubscribeWindowEvents()
        {
            // Stop changing processes even if Windows refuses to release the hook.
            eventsEnabled = false;
            if (windowEventHook != IntPtr.Zero)
            {
                if (UnhookWinEvent(windowEventHook) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                windowEventHook = IntPtr.Zero;
            }
        }

        public static void WindowEventCallback(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventsEnabled && windowEventHook != IntPtr.Zero)
                EnergyManager.HandleForegroundEvent(hwnd);
        }

        public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(int eventMin, int eventMax,
            IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, int idThread, int dwflags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int UnhookWinEvent(IntPtr hWinEventHook);
    }
}
