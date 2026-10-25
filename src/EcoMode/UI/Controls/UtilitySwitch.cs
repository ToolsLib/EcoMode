using System.Drawing.Drawing2D;

namespace ecomode;

// Native checkbox input/accessibility with a shared, high-contrast switch renderer.
internal class UtilitySwitch : CheckBox
{
    private bool _hovered;
    protected virtual bool IsCompact => true;
    protected virtual bool IsSwitchOn => Checked;
    protected virtual bool IsRecoveryPending => false;
    protected string Caption => IsRecoveryPending ? L.T("RETRY") : IsSwitchOn ? L.T("ON") : L.T("OFF");

    internal UtilitySwitch()
    {
        AutoSize = false;
        Font = UtilityTheme.StrongFont;
        BackColor = UtilityTheme.Surface;
        ForeColor = UtilityTheme.Text;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = L.T("OFF");
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Text = Caption;
        Invalidate();
        base.OnCheckedChanged(e);
    }

    internal void RefreshLanguage()
    {
        Text = Caption;
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int height = IsCompact ? Math.Max(20, Font.Height + 2) : Math.Max(ScalePixels(44), Font.Height + ScalePixels(18));
        int captionWidth = new[] { L.T("ON"), L.T("OFF"), IsCompact ? "" : L.T("RETRY") }
            .Max(caption => TextRenderer.MeasureText(caption, Font).Width);
        int width = Math.Max(ScalePixels(IsCompact ? 72 : 112), captionWidth + height + ScalePixels(8));
        return new Size(width, height);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 8 || Height < 8) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var background = IsSwitchOn ? UtilityTheme.Accent : UtilityTheme.Input;
        if (_hovered && Enabled) background = IsSwitchOn ? ControlPaint.Light(UtilityTheme.Accent) : UtilityTheme.Hover;
        var foreground = IsSwitchOn ? UtilityTheme.AccentText : IsRecoveryPending ? UtilityTheme.Warning : UtilityTheme.Text;
        var border = IsRecoveryPending ? UtilityTheme.Warning : IsSwitchOn ? UtilityTheme.Accent : IsCompact ? UtilityTheme.Muted : UtilityTheme.Border;
        var thumbColor = IsSwitchOn ? UtilityTheme.AccentText : IsRecoveryPending ? UtilityTheme.Warning : IsCompact ? UtilityTheme.Text : UtilityTheme.Muted;
        if (!Enabled) { background = UtilityTheme.Surface; foreground = thumbColor = UtilityTheme.Muted; border = UtilityTheme.Border; }

        int inset = IsCompact ? 1 : ScalePixels(3);
        int height = Math.Min(IsCompact ? Font.Height : ScalePixels(38), Height - inset * 2);
        var track = new Rectangle(inset, (Height - height) / 2, Width - inset * 2, height);
        using var path = Capsule(track);
        using var fill = new SolidBrush(background);
        using var outline = new Pen(border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(outline, path);

        int gap = IsCompact ? Math.Max(2, Font.Height / 9) : ScalePixels(4);
        int diameter = track.Height - gap * 2;
        var thumb = new Rectangle(IsSwitchOn ? track.Right - gap - diameter : track.Left + gap, track.Top + gap, diameter, diameter);
        using var thumbBrush = new SolidBrush(thumbColor);
        e.Graphics.FillEllipse(thumbBrush, thumb);
        var caption = IsSwitchOn
            ? Rectangle.FromLTRB(track.Left + gap, track.Top, thumb.Left - gap, track.Bottom)
            : Rectangle.FromLTRB(thumb.Right + gap, track.Top, track.Right - gap, track.Bottom);
        TextRenderer.DrawText(e.Graphics, Caption, Font, caption, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, UtilityTheme.Text, BackColor);
    }

    private int ScalePixels(int value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96F));

    private static GraphicsPath Capsule(Rectangle bounds)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, bounds.Height, bounds.Height, 90, 180);
        path.AddArc(bounds.Right - bounds.Height, bounds.Top, bounds.Height, bounds.Height, 270, 180);
        path.CloseFigure();
        return path;
    }
}
