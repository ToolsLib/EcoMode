namespace ecomode;

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
