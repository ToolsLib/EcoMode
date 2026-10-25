using ecomode;
using ecomode.Interop;
using System.ComponentModel;

internal static partial class Program
{
    private static void RunProcessSafetyTests()
    {
        Run("security exclusions survive every profile and protected processes are rejected", SecurityExclusions);
        Run("denied priority changes never create a priority restore obligation", DeniedPriorityChange);
        Run("denied QoS changes leave untouched processes with nothing to restore", DeniedQosChange);
        Run("already matching process settings require no writes or restoration", UnchangedProcessSettings);
        Run("genuine priority restore failures stay pending and retry only failed changes", PriorityRestoreRetry);
        Run("QoS restore failures do not block priority cleanup or lose other flags", QosRestoreRetry);
        Run("settings already restored elsewhere need no further write permission", ExternallyRestoredProcess);
    }

    private static void SecurityExclusions()
    {
        foreach (var preset in ProfilePresets.All)
        {
            EnergyManager.SetBypass(preset.CreateState().BypassProcesses);
            Check(ProcessExclusions.SecurityProcesses.All(name => EnergyManager.BypassProcessList.Contains(name)),
                $"{preset.Name} removed a security exclusion.");
        }
        EnergyManager.SetBypass([]);
        foreach (string name in new[] { "MALWAREBYTES.EXE", "mbamservice.exe", "MsMpEng.exe", "NisSrv.exe", "lsass.exe" })
        {
            Check(EnergyManager.BypassProcessList.Contains(name, StringComparer.OrdinalIgnoreCase), "Empty user bypass removed an internal exclusion.");
            Check(!ProcessExclusions.CanManage(name, Win32Api.PROTECTION_LEVEL_NONE), "A known security process was eligible for throttling.");
        }
        foreach (uint protection in new uint[] { 0, 1, 3, 8, uint.MaxValue })
            Check(!ProcessExclusions.CanManage("ordinary-app.exe", protection), "An unknown or protected process was eligible for throttling.");
        Check(ProcessExclusions.CanManage("ordinary-app.exe", Win32Api.PROTECTION_LEVEL_NONE), "Ordinary unprotected applications should remain manageable.");
    }

    private static void DeniedPriorityChange()
    {
        var settings = new FakeProcessSettings { DenyPriority = true };
        var adjustment = new ProcessAdjustment(settings);
        Throws(() => adjustment.Apply(true));
        Check(adjustment.HasChanges && settings.PriorityWrites == 1 && settings.QosWrites == 1, "The partial QoS change should be recoverable.");
        var errors = new List<string>();
        Check(adjustment.Restore((operation, _) => errors.Add(operation)) && !adjustment.HasChanges,
            "A priority change that never succeeded blocked QoS cleanup.");
        Check(settings.PriorityWrites == 1 && settings.QosWrites == 2 && errors.Count == 0,
            "Cleanup tried to restore a priority it never changed.");

        var alreadyEco = new FakeProcessSettings
        {
            DenyPriority = true,
            Throttling = new() { Version = 1, ControlMask = SpeedFlag, StateMask = SpeedFlag }
        };
        var untouched = new ProcessAdjustment(alreadyEco);
        Throws(() => untouched.Apply(true));
        Check(!untouched.HasChanges && alreadyEco.QosWrites == 0, "Priority denial with unchanged QoS created false pending work.");
        Check(untouched.Restore((_, _) => throw new Exception("Untouched process should need no restore.")) && alreadyEco.PriorityWrites == 1,
            "Cleanup retried an unsuccessful priority change.");
    }

    private static void DeniedQosChange()
    {
        var settings = new FakeProcessSettings { DenyQos = true };
        var adjustment = new ProcessAdjustment(settings);
        Throws(() => adjustment.Apply(true));
        Check(!adjustment.HasChanges && settings.PriorityWrites == 0, "A failed initial write created a restore obligation.");
        Check(adjustment.Restore((_, _) => throw new Exception("Untouched process should need no cleanup.")), "Untouched process remained pending.");
        Check(settings.QosWrites == 1, "Cleanup retried a write on an untouched process.");
    }

    private static void UnchangedProcessSettings()
    {
        var settings = new FakeProcessSettings
        {
            Priority = Win32Api.PriorityClass.IDLE_PRIORITY_CLASS,
            Throttling = new() { Version = 1, ControlMask = SpeedFlag, StateMask = SpeedFlag },
            DenyPriority = true,
            DenyQos = true
        };
        var adjustment = new ProcessAdjustment(settings);
        adjustment.Apply(true);
        Check(!adjustment.HasChanges && settings.PriorityWrites == 0 && settings.QosWrites == 0, "No-op apply requested unnecessary write access.");
        Check(adjustment.Restore((_, _) => throw new Exception("No-op apply should need no cleanup.")), "No-op apply remained pending.");
    }

    private static void PriorityRestoreRetry()
    {
        var settings = new FakeProcessSettings();
        var adjustment = new ProcessAdjustment(settings);
        adjustment.Apply(true);
        settings.DenyPriority = true;
        var errors = new List<string>();
        Check(!adjustment.Restore((operation, _) => errors.Add(operation)) && adjustment.HasChanges, "A real restore failure was discarded.");
        Check(errors.SequenceEqual(new[] { "Priority" }) && settings.QosWrites == 2, "Priority failure blocked independent QoS restoration.");
        settings.DenyPriority = false;
        Check(adjustment.Restore((_, _) => throw new Exception("Retry should succeed.")), "Priority restore did not recover.");
        Check(settings.QosWrites == 2 && settings.Priority == Win32Api.PriorityClass.BELOW_NORMAL_PRIORITY_CLASS,
            "Retry repeated successful QoS cleanup or failed to restore priority.");
    }

    private static void QosRestoreRetry()
    {
        var settings = new FakeProcessSettings();
        var adjustment = new ProcessAdjustment(settings);
        adjustment.Apply(true);
        settings.Throttling.ControlMask |= (Win32Api.ProcessorPowerThrottlingFlags)4;
        settings.Throttling.StateMask |= (Win32Api.ProcessorPowerThrottlingFlags)4;
        settings.DenyQos = true;
        var errors = new List<string>();
        Check(!adjustment.Restore((operation, _) => errors.Add(operation)), "A real QoS failure was discarded.");
        Check(errors.SequenceEqual(new[] { "EcoQoS" }) && settings.Priority == adjustment.OriginalPriority, "QoS failure blocked priority restoration.");
        settings.DenyQos = false;
        Check(adjustment.Restore((_, _) => throw new Exception("Retry should succeed.")), "QoS retry did not recover.");
        Check(settings.PriorityWrites == 2 && settings.Throttling.ControlMask == (Win32Api.ProcessorPowerThrottlingFlags)4 &&
            settings.Throttling.StateMask == (Win32Api.ProcessorPowerThrottlingFlags)4, "Cleanup changed unrelated throttling flags or repeated priority writes.");
    }

    private static void ExternallyRestoredProcess()
    {
        var settings = new FakeProcessSettings();
        var adjustment = new ProcessAdjustment(settings);
        adjustment.Apply(true);
        settings.Priority = adjustment.OriginalPriority;
        settings.Throttling = new() { Version = 1 };
        settings.DenyPriority = settings.DenyQos = true;
        Check(adjustment.Restore((_, _) => throw new Exception("Matching originals should require no write.")), "Externally restored settings remained pending.");
        Check(settings.PriorityWrites == 1 && settings.QosWrites == 1, "Matching originals caused unnecessary restoration writes.");
    }

    private const Win32Api.ProcessorPowerThrottlingFlags SpeedFlag = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;

    private sealed class FakeProcessSettings : IProcessSettings
    {
        internal Win32Api.PriorityClass Priority = Win32Api.PriorityClass.BELOW_NORMAL_PRIORITY_CLASS;
        internal Win32Api.PROCESS_POWER_THROTTLING_STATE Throttling = new() { Version = 1 };
        internal bool DenyPriority;
        internal bool DenyQos;
        internal int PriorityWrites;
        internal int QosWrites;

        public Win32Api.PriorityClass ReadPriority() => Priority;
        public Win32Api.PROCESS_POWER_THROTTLING_STATE ReadThrottling() => Throttling;
        public void WritePriority(Win32Api.PriorityClass priority)
        {
            PriorityWrites++;
            if (DenyPriority) throw new Win32Exception(5);
            Priority = priority;
        }
        public void WriteThrottling(Win32Api.PROCESS_POWER_THROTTLING_STATE state)
        {
            QosWrites++;
            if (DenyQos) throw new Win32Exception(5);
            Throttling = state;
        }
    }
}
