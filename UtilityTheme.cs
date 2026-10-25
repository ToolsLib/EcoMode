namespace ecomode;

internal static class UtilityTheme
{
    private static readonly bool HighContrast = SystemInformation.HighContrast;
    internal static readonly Color Background = HighContrast ? SystemColors.Control : Color.FromArgb(17, 23, 26);
    internal static readonly Color Surface = HighContrast ? SystemColors.Control : Color.FromArgb(25, 33, 37);
    internal static readonly Color Input = HighContrast ? SystemColors.Window : Color.FromArgb(19, 27, 31);
    internal static readonly Color Border = HighContrast ? SystemColors.ControlText : Color.FromArgb(54, 67, 72);
    internal static readonly Color Text = HighContrast ? SystemColors.ControlText : Color.FromArgb(232, 239, 235);
    internal static readonly Color Muted = HighContrast ? SystemColors.ControlText : Color.FromArgb(163, 179, 174);
    internal static readonly Color Accent = HighContrast ? SystemColors.Highlight : Color.FromArgb(154, 218, 175);
    internal static readonly Color AccentText = HighContrast ? SystemColors.HighlightText : Color.FromArgb(17, 39, 26);
    internal static readonly Color Hover = HighContrast ? SystemColors.Highlight : Color.FromArgb(43, 57, 61);
    internal static readonly Color Warning = HighContrast ? SystemColors.ControlText : Color.FromArgb(239, 196, 124);
    internal static readonly Color Error = HighContrast ? SystemColors.ControlText : Color.FromArgb(245, 157, 153);

    internal static readonly Font BodyFont = new("Segoe UI", 9F);
    internal static readonly Font StrongFont = new("Segoe UI", 9F, FontStyle.Bold);
    internal static readonly Font TitleFont = new("Segoe UI", 22F, FontStyle.Bold);
    internal static readonly Font StatusFont = new("Segoe UI", 15F, FontStyle.Bold);
    internal static readonly Font MonoFont = new("Consolas", 9.25F);

    internal static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent) : Hover;
        button.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Accent) : Background;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? AccentText : Text;
        button.Font = StrongFont;
        button.UseVisualStyleBackColor = false;
        button.Cursor = Cursors.Hand;
        button.Height = 34;
        button.Margin = new Padding(4, 0, 0, 0);
    }

    internal static Label Label(string text, bool muted = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = muted ? Muted : Text,
        BackColor = Color.Transparent, Font = BodyFont, Margin = Padding.Empty
    };

    internal static void StyleMenu(ToolStrip strip)
    {
        strip.Renderer = new UtilityMenuRenderer();
        strip.BackColor = Background;
        strip.ForeColor = Text;
        strip.Font = BodyFont;
    }

    private sealed class UtilityMenuRenderer : ToolStripProfessionalRenderer
    {
        internal UtilityMenuRenderer() : base(new MenuColors()) => RoundedEdges = false;

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientMiddle => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}

internal sealed class UtilityCard : Panel
{
    internal UtilityCard()
    {
        BackColor = UtilityTheme.Surface;
        Padding = new Padding(14);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UtilityTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}

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

internal sealed class UtilityTextEditor : RichTextBox
{
    internal UtilityTextEditor()
    {
        DetectUrls = false;
        AutoWordSelection = false;
        RichTextShortcutsEnabled = false;
        MaxLength = 32767;
    }

    protected override void WndProc(ref Message m)
    {
        const int WmPaste = 0x0302;
        if (m.Msg == WmPaste)
        {
            // Keep app rules as plain text even when copied from a formatted document.
            var format = DataFormats.GetFormat(DataFormats.UnicodeText);
            if (!ReadOnly && CanPaste(format)) Paste(format);
            return;
        }
        base.WndProc(ref m);
    }
}
