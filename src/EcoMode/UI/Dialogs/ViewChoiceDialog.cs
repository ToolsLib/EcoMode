namespace ecomode;

internal sealed class ViewChoiceDialog : Form
{
    private readonly LanguagePreferences _languagePreferences;
    private readonly Label _languageError = UtilityTheme.Label("", muted: true);
    internal AppView SelectedView { get; private set; } = AppView.Simple;

    internal ViewChoiceDialog(LanguagePreferences? languagePreferences = null)
    {
        _languagePreferences = languagePreferences ?? new LanguagePreferences();
        Text = "Welcome to EcoMode";
        Font = UtilityTheme.BodyFont;
        BackColor = UtilityTheme.Background;
        ForeColor = UtilityTheme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = CurrentAutoScaleDimensions;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var title = UtilityTheme.Label("How would you like to use EcoMode?");
        title.Font = UtilityTheme.StatusFont;
        title.MaximumSize = new Size(560, 0);
        title.Margin = new Padding(0, 0, 0, 12);
        content.Controls.Add(title);
        var explanation = UtilityTheme.Label(
            "Choose the view that feels right for you. You can change it anytime from the View menu.", muted: true);
        explanation.MaximumSize = new Size(560, 0);
        explanation.Margin = new Padding(0, 0, 0, 20);
        content.Controls.Add(explanation);
        var languageLabel = UtilityTheme.Label("Language");
        languageLabel.Margin = new Padding(0, 0, 0, 6);
        content.Controls.Add(languageLabel);
        var languagePicker = new UtilityComboBox
        {
            Name = "welcomeLanguage",
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = UtilityTheme.BodyFont,
            BackColor = UtilityTheme.Input,
            ForeColor = UtilityTheme.Text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 16),
            AccessibleName = "Language"
        };
        languagePicker.Items.AddRange(L.Languages.Cast<object>().ToArray());
        languagePicker.SelectedItem = L.Languages.Single(language => language.Code == L.Language);
        languagePicker.SelectedIndexChanged += (_, _) =>
        {
            string code = ((AppLanguage)languagePicker.SelectedItem!).Code;
            L.SetLanguage(code);
            try { _languagePreferences.Save(code); _languageError.Text = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _languageError.Text = L.T("Your language choice could not be saved. You may be asked again next time. {0}", ex.Message);
            }
        };
        content.Controls.Add(languagePicker);
        _languageError.MaximumSize = new Size(560, 0);
        _languageError.ForeColor = UtilityTheme.Warning;
        content.Controls.Add(_languageError);
        var simple = Choice("Standard view (recommended)\r\nThe Eco Mode switch, clear status and an easy way to restore settings.", AppView.Simple);
        var advanced = Choice("Advanced view\r\nFine-tune power-saving options, profiles and rules for individual apps.", AppView.Advanced);
        UtilityTheme.StyleButton(simple, primary: true);
        UtilityTheme.StyleButton(advanced);
        foreach (var button in new[] { simple, advanced })
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(560, 82);
            button.MaximumSize = new Size(560, 0);
            button.Padding = new Padding(16, 12, 16, 12);
            button.Margin = new Padding(0, 0, 0, 12);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Dock = DockStyle.Fill;
            content.Controls.Add(button);
        }
        AcceptButton = simple;
        Controls.Add(content);
        L.TranslateTree(this);
        FitChoices();
        L.LanguageChanged += UpdateLanguage;
    }

    private void UpdateLanguage() { L.TranslateTree(this); FitChoices(); PerformLayout(); }

    private void FitChoices()
    {
        foreach (var button in Controls[0].Controls.OfType<Button>())
        {
            int textHeight = TextRenderer.MeasureText(button.Text, button.Font,
                new Size(560 - button.Padding.Horizontal - 12, int.MaxValue), TextFormatFlags.WordBreak).Height;
            button.MinimumSize = new Size(560, Math.Max(82, textHeight + button.Padding.Vertical + 12));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) L.LanguageChanged -= UpdateLanguage;
        base.Dispose(disposing);
    }

    private UtilityButton Choice(string caption, AppView view)
    {
        var button = new UtilityButton { Text = caption, AccessibleName = caption.Replace("\r\n", ". ") };
        button.Click += (_, _) => { SelectedView = view; DialogResult = DialogResult.OK; Close(); };
        return button;
    }
}
