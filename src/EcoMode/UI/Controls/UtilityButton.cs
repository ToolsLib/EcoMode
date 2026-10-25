namespace ecomode;

internal sealed class UtilityButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Enabled) return;
        // The native flat-button renderer uses a dark disabled caption on dark surfaces.
        using var background = new SolidBrush(UtilityTheme.Surface);
        e.Graphics.FillRectangle(background, ClientRectangle);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, UtilityTheme.Border, ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, UtilityTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
