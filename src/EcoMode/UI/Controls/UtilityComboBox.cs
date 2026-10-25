namespace ecomode;

internal sealed class UtilityComboBox : ComboBox
{
    internal UtilityComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = Font.Height + 6;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Font.Height + 6;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        Color background = e.State.HasFlag(DrawItemState.Selected) && !e.State.HasFlag(DrawItemState.ComboBoxEdit)
            ? UtilityTheme.Hover : UtilityTheme.Input;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = Rectangle.Inflate(e.Bounds, -6, 0);
        string caption = e.Index >= 0 ? GetItemText(Items[e.Index]) ?? "" : Text;
        TextRenderer.DrawText(e.Graphics, caption, Font, bounds, Enabled ? UtilityTheme.Text : UtilityTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
        base.OnDrawItem(e);
    }
}
