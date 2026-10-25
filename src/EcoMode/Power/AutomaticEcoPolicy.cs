namespace ecomode;

internal sealed class AutomaticEcoPolicy
{
    internal bool Enabled { get; set; }
    internal bool ManualOverride { get; private set; }
    private PowerConnection _lastKnownSource;

    internal void Observe(BatterySnapshot battery)
    {
        if (battery.Connection == PowerConnection.Unknown) return;
        if (_lastKnownSource != PowerConnection.Unknown && battery.Connection != _lastKnownSource) ManualOverride = false;
        _lastKnownSource = battery.Connection;
    }

    internal void Override() => ManualOverride = true;
    internal void Resume() => ManualOverride = false;

    internal bool? DesiredState(BatterySnapshot battery, bool manualProfileActive)
    {
        Observe(battery);
        if (!Enabled || ManualOverride || manualProfileActive) return null;
        if (battery.HasBattery == false) return false;
        if (battery.HasBattery != true || battery.Connection == PowerConnection.Unknown) return null;
        return battery.Connection == PowerConnection.Battery;
    }
}

internal sealed class ProcessSweepSchedule
{
    private TimeSpan? _lastSweep;
    internal bool Due(TimeSpan elapsed, bool enabled, bool force = false)
    {
        if (!enabled) { _lastSweep = null; return false; }
        if (!force && _lastSweep is TimeSpan last && elapsed >= last && elapsed - last < TimeSpan.FromMinutes(1)) return false;
        _lastSweep = elapsed;
        return true;
    }

    internal void Mark(TimeSpan elapsed) => _lastSweep = elapsed;
}
