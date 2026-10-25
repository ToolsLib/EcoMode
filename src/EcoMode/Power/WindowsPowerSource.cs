using System.Runtime.InteropServices;

namespace ecomode;

internal sealed class WindowsPowerSource : IPowerSource
{
    public BatterySnapshot Read()
    {
        if (!GetSystemPowerStatus(out var status)) return BatterySnapshot.Unknown;
        var snapshot = DecodeStatus(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent, status.BatteryLifeTime);
        double? watts = null;
        if (snapshot is { HasBattery: true, Connection: PowerConnection.Battery, Charging: false } &&
            CallNtPowerInformation(5, IntPtr.Zero, 0, out var battery, (uint)Marshal.SizeOf<SystemBatteryState>()) == 0 &&
            battery.BatteryPresent != 0 && battery.AcOnLine == 0 && battery.Charging == 0)
            watts = DecodeDischargeRate(battery.Rate, battery.Discharging != 0);
        return snapshot with { DischargeWatts = watts };
    }

    internal static BatterySnapshot DecodeStatus(byte acLine, byte flags, byte lifePercent, uint seconds)
    {
        var connection = acLine switch { 0 => PowerConnection.Battery, 1 => PowerConnection.External, _ => PowerConnection.Unknown };
        bool? present = flags == 255 ? null : (flags & 128) == 0;
        int? percent = present == true && lifePercent <= 100 ? lifePercent : null;
        bool charging = present == true && (flags & 8) != 0;
        TimeSpan? remaining = present == true && !charging && connection == PowerConnection.Battery && seconds is > 0 and < uint.MaxValue
            ? TimeSpan.FromSeconds(seconds) : null;
        return new(present, connection, percent, charging, remaining, null);
    }

    internal static double? DecodeDischargeRate(uint rawRate, bool discharging)
    {
        // Windows declares DWORD but documents a signed LONG: discharge is negative.
        double watts = -(double)unchecked((int)rawRate) / 1000;
        return discharging && watts is > 0 and <= 1000 ? watts : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        internal uint BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemBatteryState
    {
        internal byte AcOnLine, BatteryPresent, Charging, Discharging, Spare1, Spare2, Spare3, Tag;
        internal uint MaxCapacity, RemainingCapacity, Rate, EstimatedTime, DefaultAlert1, DefaultAlert2;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("powrprof.dll")]
    private static extern int CallNtPowerInformation(int level, IntPtr input, uint inputLength,
        out SystemBatteryState state, uint outputLength);
}
