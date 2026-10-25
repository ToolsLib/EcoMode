using System.ComponentModel;
using System.Diagnostics;

namespace ecomode;

internal static class StartupElevation
{
    internal const string Confirmation =
        "EcoMode is running without Administrator privileges.\r\n\r\n" +
        "Restart as Administrator to enable power-plan changes and Edge/Chrome policies, and to restore system settings.\r\n\r\n" +
        "Windows will ask for permission. Choose No to continue with user GPU preferences and accessible processes only.";

    internal static SingleInstance? AcquireInstance(bool isAdmin, Func<bool> confirmRestart,
        Action<string, MessageBoxIcon> notify, string mutexName = SingleInstance.Name,
        Func<ProcessStartInfo, bool>? launch = null)
    {
        SingleInstance? instance = SingleInstance.TryAcquire(mutexName);
        try
        {
            if (instance == null)
            {
                NotifyAlreadyRunning(notify);
                return null;
            }
            if (isAdmin || !confirmRestart()) return instance;

            string? failure = null;
            try
            {
                var start = CreateStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("The app path is unavailable."),
                    Environment.GetCommandLineArgs(), AppContext.BaseDirectory);
                // No form, hooks or settings changes exist yet. Release the global lock
                // before launching, so the elevated copy can acquire it immediately.
                instance.Dispose();
                instance = null;
                if ((launch ?? Launch)(start)) return null;
                failure = "Windows did not start the elevated app.";
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // Canceling UAC is a normal choice; continue without another prompt.
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }

            instance ??= SingleInstance.TryAcquire(mutexName);
            if (instance == null) NotifyAlreadyRunning(notify);
            else if (failure != null)
                notify("EcoMode could not restart as Administrator. It will continue in standard mode.\r\n\r\n" + failure,
                    MessageBoxIcon.Warning);
            return instance;
        }
        catch
        {
            instance?.Dispose();
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, string[] commandLine, string workingDirectory)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new InvalidOperationException("The app path must be absolute.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = workingDirectory
        };
        // Framework-dependent launches can use `dotnet EcoMode.dll`; published
        // apphost and single-file builds restart their own executable directly.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (commandLine.Length == 0 || string.IsNullOrWhiteSpace(commandLine[0]))
                throw new InvalidOperationException("The app assembly path is unavailable.");
            start.ArgumentList.Add(Path.GetFullPath(commandLine[0]));
        }
        foreach (string argument in commandLine.Skip(1)) start.ArgumentList.Add(argument);
        return start;
    }

    private static bool Launch(ProcessStartInfo start)
    {
        using var process = Process.Start(start);
        return process != null;
    }

    private static void NotifyAlreadyRunning(Action<string, MessageBoxIcon> notify) =>
        notify("EcoMode is already running in this or another Windows session. Close that instance before starting another.",
            MessageBoxIcon.Information);
}
