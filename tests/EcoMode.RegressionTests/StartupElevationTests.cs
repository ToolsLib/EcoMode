using ecomode;
using System.ComponentModel;
using System.Diagnostics;

internal static partial class Program
{
    private static void RunStartupElevationTests()
    {
        Run("administrator startup skips confirmation and declining preserves standard mode", StartupPrivileges);
        Run("elevated restart releases the global lock before the replacement starts", StartupRestart);
        Run("canceling UAC or a failed restart keeps standard startup available", StartupRestartFailure);
        Run("an existing instance prevents another elevation prompt", StartupExistingInstance);
        Run("elevated relaunch supports apphost and dotnet paths with spaces", StartupLaunchArguments);
    }

    private static string StartupMutex() => @"Global\EcoMode.Startup.Tests." + Guid.NewGuid().ToString("N");

    private static void StartupPrivileges()
    {
        foreach (bool admin in new[] { true, false })
        {
            int confirmations = 0;
            string name = StartupMutex();
            using var instance = StartupElevation.AcquireInstance(admin, () => { confirmations++; return false; },
                (_, _) => throw new Exception("Unexpected startup notification."), name,
                _ => throw new Exception("Declined or elevated startup should not relaunch."));
            Check(instance != null && confirmations == (admin ? 0 : 1), "Startup confirmation did not match the current privileges.");
            using var contender = SingleInstance.TryAcquire(name);
            Check(contender == null, "Continuing startup lost its single-instance lock.");
        }
    }

    private static void StartupRestart()
    {
        string name = StartupMutex();
        Process? replacement = null;
        try
        {
            using var instance = StartupElevation.AcquireInstance(false, () => true,
                (_, _) => throw new Exception("Unexpected restart notification."), name, start =>
                {
                    Check(start.UseShellExecute && start.Verb == "runas", "Restart did not request Windows elevation.");
                    replacement = StartMode("--lock-child", name);
                    Check(ReadReady(replacement) == "READY", "The original instance blocked its elevated replacement.");
                    return true;
                });
            Check(instance == null, "Original startup continued after a successful relaunch.");
            using var contender = SingleInstance.TryAcquire(name);
            Check(contender == null, "Replacement did not retain the single-instance lock.");
        }
        finally
        {
            if (replacement != null)
            {
                if (!replacement.HasExited) { replacement.Kill(); replacement.WaitForExit(); }
                replacement.Dispose();
            }
        }
    }

    private static void StartupRestartFailure()
    {
        foreach (string mode in new[] { "cancel", "exception", "no-process" })
        {
            string name = StartupMutex();
            var notifications = new List<string>();
            using var instance = StartupElevation.AcquireInstance(false, () => true,
                (message, _) => notifications.Add(message), name, _ => mode switch
                {
                    "cancel" => throw new Win32Exception(1223),
                    "exception" => throw new Win32Exception(2),
                    _ => false
                });
            Check(instance != null, "Canceled or failed elevation prevented standard startup.");
            Check(notifications.Count == (mode == "cancel" ? 0 : 1), "Canceled UAC should be quiet; failures should be explained.");
            if (notifications.Count != 0)
                Check(notifications[0].Contains("standard mode"), "Restart failure did not explain the fallback.");
            using var contender = SingleInstance.TryAcquire(name);
            Check(contender == null, "Failed elevation did not reacquire the instance lock.");
        }
    }

    private static void StartupExistingInstance()
    {
        string name = StartupMutex();
        using var owner = SingleInstance.TryAcquire(name);
        var notifications = new List<string>();
        using var duplicate = StartupElevation.AcquireInstance(false,
            () => throw new Exception("A duplicate instance should not prompt to elevate."),
            (message, _) => notifications.Add(message), name,
            _ => throw new Exception("A duplicate instance should not relaunch."));
        Check(duplicate == null && notifications.Count == 1 && notifications[0].Contains("already running"),
            "Duplicate startup did not preserve the existing single-instance behavior.");
    }

    private static void StartupLaunchArguments()
    {
        const string app = @"C:\Program Files\EcoMode\ecomode.exe";
        string[] arguments = [app, "--example", @"C:\Folder With Spaces\profile.json"];
        var start = StartupElevation.CreateStartInfo(app, arguments, @"C:\Program Files\EcoMode");
        Check(start.FileName == app && start.UseShellExecute && start.Verb == "runas" &&
            start.ArgumentList.SequenceEqual(arguments.Skip(1)) && start.WorkingDirectory == @"C:\Program Files\EcoMode",
            "Apphost relaunch lost its executable, arguments or working directory.");
        const string dotnet = @"C:\Program Files\dotnet\dotnet.exe";
        string[] frameworkArguments = [@"C:\Eco Mode\ecomode.dll", "--example", "quoted \"value\""];
        var framework = StartupElevation.CreateStartInfo(dotnet, frameworkArguments, @"C:\Eco Mode");
        Check(framework.FileName == dotnet && framework.ArgumentList.SequenceEqual(frameworkArguments),
            "Framework-dependent relaunch lost the assembly path or argument boundaries.");
        Throws(() => StartupElevation.CreateStartInfo("relative.exe", arguments, @"C:\Eco Mode"));
        Throws(() => StartupElevation.CreateStartInfo(dotnet, [], @"C:\Eco Mode"));
    }
}
