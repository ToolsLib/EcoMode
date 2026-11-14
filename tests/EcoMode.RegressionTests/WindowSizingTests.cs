using ecomode;

internal static partial class Program
{
    private static void RunWindowSizingTests()
    {
        Run("repeated resizing and layout keep stable minimums and visible recovery controls in every language", StableWindowSizing);
        Run("shorter content releases extra minimum height so the window can shrink again", WindowMinimumCanShrink);
    }

    private static void SettleWindowLayout(MainForm form)
    {
        Invoke(form, "LayoutCurrentView");
        form.PerformLayout();
        Application.DoEvents();
    }

    private static void StableWindowSizing()
    {
        try
        {
            foreach (var language in L.Languages)
                foreach (var view in new[] { AppView.Simple, AppView.Advanced })
                {
                    L.SetLanguage(language.Code);
                    var (_, manager, _) = Setup();
                    using var form = new TestMainForm(false, manager, SwitchConfig(), view);
                    ShowOffscreen(form);
                    Size requested = view == AppView.Simple ? new Size(700, 700) : new Size(1100, 700);
                    Size? settledMinimum = null;
                    for (int cycle = 0; cycle < 3; cycle++)
                    {
                        form.Size = new Size(1400, 1000);
                        SettleWindowLayout(form);
                        form.Size = requested;
                        SettleWindowLayout(form);
                        Check(settledMinimum == null || settledMinimum == form.MinimumSize,
                            $"{language.Code}/{view}: repeated resizing inflated the minimum from {settledMinimum} to {form.MinimumSize}.");
                        settledMinimum = form.MinimumSize;
                        for (int repeat = 0; repeat < 3; repeat++) SettleWindowLayout(form);
                        Check(settledMinimum == form.MinimumSize, $"{language.Code}/{view}: layout alone changed the minimum.");
                        var card = Field<TableLayoutPanel>(form, "_powerControls").Parent!;
                        foreach (string name in view == AppView.Simple ? new[] { "btnSimpleRestore", "btnSimpleActivity" } : new[] { "btnSaveOriginal", "btnRestoreOriginal" })
                        {
                            var button = Field<Button>(form, name);
                            Check(button.Visible && card.ClientRectangle.Contains(card.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                                $"{language.Code}/{view}: resizing clipped {name}.");
                        }
                    }
                }
        }
        finally { L.SetLanguage("en"); }
    }

    private static void WindowMinimumCanShrink()
    {
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig());
        ShowOffscreen(form);
        form.Size = new Size(1100, 700);
        SettleWindowLayout(form);
        int originalMinimum = form.MinimumSize.Height;
        var description = Field<Label>(form, "lblPresetDescription");
        string original = description.Text;
        description.Text = string.Join(Environment.NewLine, Enumerable.Repeat("Additional profile information", 18));
        SettleWindowLayout(form);
        int expandedMinimum = form.MinimumSize.Height;
        description.Text = original;
        SettleWindowLayout(form);
        // A small hosted display can cap both minimums at the same screen height.
        // Returning to the baseline still proves the window did not retain extra height.
        Check(form.MinimumSize.Height == originalMinimum,
            $"Shorter content did not restore the original minimum: before={originalMinimum}, expanded={expandedMinimum}, after={form.MinimumSize.Height}, working={Screen.FromControl(form).WorkingArea}.");
        form.Size = new Size(1100, 700);
        SettleWindowLayout(form);
        Check(form.Height <= 732, "The window could not return to its original minimum after content shrank.");
    }
}
