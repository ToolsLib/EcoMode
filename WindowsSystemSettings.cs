using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ecomode;

internal sealed class WindowsSystemSettings : ISystemSettings
{
    private readonly RegistryKey _userRoot;

    internal WindowsSystemSettings(RegistryKey? userRoot = null) => _userRoot = userRoot ?? Registry.CurrentUser;

    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Epp = SettingRules.Epp;
    private static readonly Guid Boost = SettingRules.Boost;

    public void Validate(SettingTarget target, SettingValue value) => SettingRules.Validate(target, value);

    public IEnumerable<SettingTarget> GetTargets(AppState profile, bool isAdmin)
    {
        if (profile.IncludePowerPlan && isAdmin)
        {
            string scheme = GetActiveScheme().ToString();
            yield return new(SettingKind.Power, scheme, Epp.ToString(), SettingValue.From(new uint[] { 85, 90 }));
            yield return new(SettingKind.Power, scheme, Boost.ToString(), SettingValue.From(new uint[] { 0, 0 }));
        }

        if (profile.IncludeEdge && isAdmin)
        {
            yield return Policy(@"SOFTWARE\Policies\Microsoft\Edge", "HardwareAccelerationModeEnabled", 0);
            yield return Policy(@"SOFTWARE\Policies\Microsoft\Edge", "EfficiencyModeEnabled", 1);
        }
        if (profile.IncludeChrome && isAdmin)
        {
            yield return Policy(@"SOFTWARE\Policies\Google\Chrome", "HardwareAccelerationModeEnabled", 0);
            yield return Policy(@"SOFTWARE\Policies\Google\Chrome", "BatterySaverModeAvailability", 1);
        }
        if (profile.IncludeGpuPref)
        {
            foreach (string raw in profile.GpuPrefApps.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = raw.Trim().Trim('"');
                if (!File.Exists(path)) continue;
                var target = new SettingTarget(SettingKind.UserRegistry, SettingRules.GpuPath, path,
                    SettingValue.From("GpuPreference=1;", RegistryValueKind.String));
                var current = Read(target);
                yield return target with { EcoValue = SettingValue.From(
                    GpuPreferences.WithPreference(current.Exists ? current.Data.GetString() : null, 1), RegistryValueKind.String) };
            }
        }
    }

    private static SettingTarget Policy(string path, string name, int value) =>
        new(SettingKind.MachineRegistry, path, name, SettingValue.From(value, RegistryValueKind.DWord));

    public SettingValue Read(SettingTarget target)
    {
        SettingRules.ValidateTarget(target);
        if (target.Kind == SettingKind.Power)
        {
            Guid scheme = Guid.Parse(target.Path), setting = Guid.Parse(target.Name);
            Check(PowerReadACValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, out uint ac));
            Check(PowerReadDCValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, out uint dc));
            var value = SettingValue.From(new[] { ac, dc });
            Validate(target, value);
            return value;
        }

        var original = RegistryValueAccess.Read(Root(target), target.Path, target.Name);
        Validate(target, original);
        return original;
    }

    public void Write(SettingTarget target, SettingValue value)
    {
        Validate(target, value);
        if (target.Kind == SettingKind.Power)
        {
            Guid scheme = Guid.Parse(target.Path), setting = Guid.Parse(target.Name);
            uint ac = value.Data[0].GetUInt32(), dc = value.Data[1].GetUInt32();
            Check(PowerWriteACValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, ac));
            Check(PowerWriteDCValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, dc));
            // Refresh only the active plan; restore each value to the plan it came from.
            if (GetActiveScheme() == scheme) Check(PowerSetActiveScheme(IntPtr.Zero, in scheme));
            return;
        }

        RegistryValueAccess.Write(Root(target), target.Path, target.Name, value);
    }

    private RegistryKey Root(SettingTarget target) => target.Kind == SettingKind.MachineRegistry
        ? Registry.LocalMachine : _userRoot;

    private static Guid GetActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    private static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception((int)error);
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, in Guid scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
