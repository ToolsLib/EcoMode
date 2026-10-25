namespace ecomode;

internal enum RestoreExitChoice { KeepOpen, Retry, QuitAnyway }

internal sealed class RestoreExitDialog : Form
{
    internal RestoreExitChoice Choice { get; private set; } = RestoreExitChoice.KeepOpen;

    internal const string Explanation = "Some settings could not be restored. Windows may be blocking a process or require Administrator access.";
    internal const string Consequences = "If you quit anyway, blocked process limits may remain until those processes restart. Process recovery cannot be retried after exit. Saved power plan and registry recovery records are kept for the next launch.";
    internal const string RecoveryHint = "Reopen EcoMode as Administrator if system settings still need restoring. Restart browsers after restoring their policies.";

    internal RestoreExitDialog()
    {
        Text = "Restoration incomplete";
        Font = UtilityTheme.BodyFont;
        BackColor = UtilityTheme.Background;
        ForeColor = UtilityTheme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = CurrentAutoScaleDimensions;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24)
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (string message in new[] { "Some settings still need to be restored.", Explanation, Consequences, RecoveryHint })
        {
            var label = UtilityTheme.Label(message);
            label.MaximumSize = new Size(560, 0);
            label.Dock = DockStyle.Fill;
            label.Margin = new Padding(0, 0, 0, 16);
            if (message == "Some settings still need to be restored.")
            {
                label.Font = UtilityTheme.StatusFont;
                label.ForeColor = UtilityTheme.Warning;
            }
            content.Controls.Add(label);
        }
        var retry = ActionButton("Retry restoration", RestoreExitChoice.Retry);
        var keepOpen = ActionButton("Keep EcoMode open", RestoreExitChoice.KeepOpen);
        var quit = ActionButton("Quit anyway", RestoreExitChoice.QuitAnyway);
        UtilityTheme.StyleButton(retry, primary: true);
        UtilityTheme.StyleButton(keepOpen);
        UtilityTheme.StyleButton(quit);
        quit.ForeColor = UtilityTheme.Warning;
        quit.FlatAppearance.BorderColor = UtilityTheme.Warning;
        foreach (var button in new[] { retry, keepOpen, quit })
        {
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(560, 38);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 8);
            button.Padding = new Padding(12, 6, 12, 6);
            content.Controls.Add(button);
        }
        Controls.Add(content);
        AcceptButton = retry;
        CancelButton = keepOpen;
        L.TranslateTree(this);
    }

    private UtilityButton ActionButton(string text, RestoreExitChoice choice)
    {
        var button = new UtilityButton { Text = text };
        button.Click += (_, _) =>
        {
            Choice = choice;
            DialogResult = choice == RestoreExitChoice.KeepOpen ? DialogResult.Cancel : DialogResult.OK;
            Close();
        };
        return button;
    }
}
