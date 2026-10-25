namespace ecomode;

internal enum PowerConnection { Unknown, Battery, External }

internal sealed record BatterySnapshot(bool? HasBattery, PowerConnection Connection, int? Percent,
    bool Charging, TimeSpan? Remaining, double? DischargeWatts)
{
    internal static BatterySnapshot Unknown { get; } = new(null, PowerConnection.Unknown, null, false, null, null);
}

internal interface IPowerSource
{
    BatterySnapshot Read();
}
