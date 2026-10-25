using System.Drawing.Drawing2D;

namespace ecomode;

internal sealed class BatteryHistoryChart : Control
{
    private readonly BatteryHistory _history;
    internal BatteryHistoryChart(BatteryHistory history)
    {
        _history = history;
        Height = 120;
        Dock = DockStyle.Top;
        BackColor = UtilityTheme.Input;
        ForeColor = UtilityTheme.Text;
        Font = UtilityTheme.BodyFont;
        AccessibleName = "Battery level history";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var samples = _history.Samples.Where(sample => sample.Battery.Percent is >= 0 and <= 100).ToArray();
        if (samples.Length < 2)
        {
            TextRenderer.DrawText(e.Graphics, L.T("History appears while EcoMode is running."), Font, ClientRectangle,
                UtilityTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = new Rectangle(40, 10, Math.Max(1, Width - 50), Math.Max(1, Height - 36));
        int low = Math.Max(0, samples.Min(sample => sample.Battery.Percent!.Value) / 10 * 10);
        int high = Math.Min(100, Math.Max(low + 10, (samples.Max(sample => sample.Battery.Percent!.Value) + 9) / 10 * 10));
        if (high == low) low = Math.Max(0, high - 10);
        double start = samples[0].Elapsed.TotalSeconds, duration = Math.Max(1, samples[^1].Elapsed.TotalSeconds - start);
        PointF Point(BatterySample sample) => new(plot.Left + (float)((sample.Elapsed.TotalSeconds - start) / duration * plot.Width),
            plot.Bottom - (float)((sample.Battery.Percent!.Value - low) / (double)Math.Max(1, high - low) * plot.Height));
        using var grid = new Pen(UtilityTheme.Border);
        using var on = new Pen(UtilityTheme.Accent, 2);
        using var off = new Pen(UtilityTheme.Muted, 2);
        using var marker = new Pen(UtilityTheme.Warning) { DashStyle = DashStyle.Dot };
        e.Graphics.DrawRectangle(grid, plot);
        TextRenderer.DrawText(e.Graphics, high + "%", Font, new Rectangle(0, plot.Top - 5, 38, Font.Height), UtilityTheme.Muted);
        TextRenderer.DrawText(e.Graphics, low + "%", Font, new Rectangle(0, plot.Bottom - Font.Height, 38, Font.Height), UtilityTheme.Muted);
        for (int index = 1; index < samples.Length; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            if (!current.BreakBefore && current.Elapsed - previous.Elapsed <= TimeSpan.FromMinutes(2))
                e.Graphics.DrawLine(previous.EcoOn ? on : off, Point(previous), Point(current));
            if (current.EcoOn != previous.EcoOn)
                e.Graphics.DrawLine(marker, Point(current).X, plot.Top, Point(current).X, plot.Bottom);
        }
        TextRenderer.DrawText(e.Graphics, samples[0].Time.ToString("t"), Font,
            new Rectangle(plot.Left, plot.Bottom + 3, plot.Width / 2, Font.Height), UtilityTheme.Muted);
        TextRenderer.DrawText(e.Graphics, samples[^1].Time.ToString("t"), Font,
            new Rectangle(plot.Left + plot.Width / 2, plot.Bottom + 3, plot.Width / 2, Font.Height), UtilityTheme.Muted, TextFormatFlags.Right);
    }
}
