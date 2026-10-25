using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ecomode;

internal sealed class WindowsSystemSettings : ISystemSettings
{
    private static readonly Guid Processor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid Epp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    private static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");

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
            foreach (string raw in profile.GpuPrefApps.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = raw.Trim().Trim('"');
                if (!File.Exists(path)) continue;
                yield return new(SettingKind.UserRegistry, @"Software\Microsoft\DirectX\UserGpuPreferences", path,
                    SettingValue.From("GpuPreference=1;", RegistryValueKind.String));
            }
        }
    }

    private static SettingTarget Policy(string path, string name, int value) =>
        new(SettingKind.MachineRegistry, path, name, SettingValue.From(value, RegistryValueKind.DWord));

    public SettingValue Read(SettingTarget target)
    {
        if (target.Kind == SettingKind.Power)
        {
            Guid scheme = Guid.Parse(target.Path), setting = Guid.Parse(target.Name);
            Check(PowerReadACValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, out uint ac));
            Check(PowerReadDCValueIndex(IntPtr.Zero, in scheme, in Processor, in setting, out uint dc));
            return SettingValue.From(new[] { ac, dc });
        }

        using var key = Root(target).OpenSubKey(target.Path);
        if (key == null || !key.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase))
            return SettingValue.Missing;

        object data = key.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidDataException($"Cannot read registry value {target.Name}.");
        return SettingValue.From(data, key.GetValueKind(target.Name));
    }

    public void Write(SettingTarget target, SettingValue value)
    {
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

        if (!value.Exists)
        {
            using var existing = Root(target).OpenSubKey(target.Path, writable: true);
            existing?.DeleteValue(target.Name, throwOnMissingValue: false);
            return;
        }

        object data = value.Kind switch
        {
            RegistryValueKind.DWord => value.Data.GetInt32(),
            RegistryValueKind.QWord => value.Data.GetInt64(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.Data.GetString()!,
            RegistryValueKind.MultiString => value.Data.EnumerateArray().Select(e => e.GetString()!).ToArray(),
            RegistryValueKind.Binary or RegistryValueKind.None => value.Data.GetBytesFromBase64(),
            _ => throw new InvalidDataException($"Unsupported registry value kind: {value.Kind}")
        };
        using var key = Root(target).CreateSubKey(target.Path, writable: true)
            ?? throw new IOException($"Cannot open registry key {target.Path}.");
        key.SetValue(target.Name, data, value.Kind);
    }

    private static RegistryKey Root(SettingTarget target) => target.Kind == SettingKind.MachineRegistry
        ? Registry.LocalMachine : Registry.CurrentUser;

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
