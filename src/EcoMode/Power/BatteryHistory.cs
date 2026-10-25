namespace ecomode;

internal sealed record BatterySample(TimeSpan Elapsed, DateTimeOffset Time, BatterySnapshot Battery, bool EcoOn, bool BreakBefore);

internal sealed class BatteryHistory
{
    private readonly List<BatterySample> _samples = new();
    private bool _breakNext;
    internal IReadOnlyList<BatterySample> Samples => _samples;
    internal void Break() => _breakNext = true;

    internal void Add(TimeSpan elapsed, DateTimeOffset time, BatterySnapshot battery, bool ecoOn)
    {
        var previous = _samples.LastOrDefault();
        if (previous != null && elapsed < previous.Elapsed) { _samples.Clear(); previous = null; }
        bool gap = _breakNext || previous != null && (elapsed - previous.Elapsed > TimeSpan.FromMinutes(2) ||
            previous.Battery.Connection != battery.Connection || previous.Battery.HasBattery != battery.HasBattery ||
            previous.Battery.Percent == null || battery.Percent == null);
        _breakNext = false;
        var sample = new BatterySample(elapsed, time, battery, ecoOn, gap);
        if (previous?.Elapsed == elapsed) _samples[^1] = sample with { BreakBefore = previous.BreakBefore || gap };
        else _samples.Add(sample);
        _samples.RemoveAll(value => elapsed - value.Elapsed > TimeSpan.FromHours(1));
        while (_samples.Count > 512) _samples.RemoveAt(0);
    }

    internal double? DrainPercentPerHour
    {
        get
        {
            var last = _samples.LastOrDefault();
            if (last?.Battery is not { HasBattery: true, Connection: PowerConnection.Battery, Charging: false, Percent: not null }) return null;
            var first = last;
            for (int index = _samples.Count - 2; index >= 0; index--)
            {
                var candidate = _samples[index];
                if (first.BreakBefore || last.Elapsed - candidate.Elapsed > TimeSpan.FromMinutes(15) ||
                    candidate.Battery is not { HasBattery: true, Connection: PowerConnection.Battery, Charging: false, Percent: not null } ||
                    candidate.Battery.Percent < first.Battery.Percent) break;
                first = candidate;
            }
            double hours = (last.Elapsed - first.Elapsed).TotalHours;
            int difference = first.Battery.Percent!.Value - last.Battery.Percent!.Value;
            return hours >= 5d / 60 && difference > 0 ? difference / hours : null;
        }
    }
}
