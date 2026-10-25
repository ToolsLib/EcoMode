using ecomode;
using System.Reflection;

internal static partial class Program
{
    private static void RunViewTests()
    {
        Run("first launch asks once and remembers either view independently of profiles", FirstViewChoice);
        Run("missing or invalid view preferences fall back to Simple without breaking startup", ViewPreferenceFallback);
        Run("view preference write failures keep the selected view available", ViewPreferenceSaveFailure);
        Run("switching views preserves unsaved controls, originals and active Eco Mode", ViewSwitchPreservesSettings);
        Run("Simple view exposes pending recovery and restores through the shared controller", SimpleViewRecovery);
        Run("Simple view and the welcome chooser fit their content and expose keyboard controls", SimpleViewLayout);
        Run("the header and menu switch both ways between Standard and Advanced in every language", ViewPickerRoundTrips);
    }

    private static void FirstViewChoice()
    {
        foreach (var selected in new[] { AppView.Simple, AppView.Advanced })
        {
            string path = Path.Combine(_root, "choice-" + selected + ".json");
            var preferences = new ViewPreferences(path);
            int prompts = 0;
            Check(preferences.GetOrChoose(() => { prompts++; return selected; }, _ => throw new Exception("Save failed")) == selected,
                "The first-launch choice was ignored.");
            Check(new ViewPreferences(path).GetOrChoose(() => { prompts++; return AppView.Simple; }, _ => { }) == selected && prompts == 1,
                "A saved choice prompted again or was forgotten.");
            var (_, manager, _) = Setup();
            using var form = new TestMainForm(false, manager, SwitchConfig(), initialView: null, viewPreferencesPath: path);
            Check(form.CurrentView == selected, "Startup did not use the saved view.");
            form.ImportProfileJson("{\"GpuPrefApps\":[],\"IncludeEcoQos\":false}");
            form.LoadPreset("balanced");
            Check(form.CurrentView == selected && preferences.Load() == selected, "Loading a profile or preset changed the view choice.");
        }
    }

    private static void ViewPreferenceFallback()
    {
        string path = Path.Combine(_root, "invalid-view.json");
        foreach (string json in new[] { "{broken", "null", "{}", "{\"View\":\"Unknown\"}", "{\"View\":99}" })
        {
            File.WriteAllText(path, json);
            var preferences = new ViewPreferences(path);
            Check(preferences.Load() == null, "An invalid view choice was accepted.");
            var (_, manager, _) = Setup();
            using var form = new TestMainForm(false, manager, SwitchConfig(), initialView: null, viewPreferencesPath: path);
            Check(form.CurrentView == AppView.Simple && manager.HasSnapshot, "Invalid view preferences prevented safe Simple startup.");
            int prompts = 0;
            preferences.GetOrChoose(() => { prompts++; return AppView.Simple; }, _ => { });
            Check(prompts == 1 && preferences.Load() == AppView.Simple, "Invalid preferences did not offer a fresh choice.");
        }
        using var choice = new ViewChoiceDialog();
        Check(choice.SelectedView == AppView.Simple && choice.AcceptButton is Button,
            "The recommended/default welcome choice is not Simple.");
    }

    private static void ViewPreferenceSaveFailure()
    {
        string path = Path.Combine(_root, "unwritable-view.json");
        Directory.CreateDirectory(path);
        string? failure = null;
        Check(new ViewPreferences(path).GetOrChoose(() => AppView.Advanced, message => failure = message) == AppView.Advanced && failure != null,
            "An unwritable preference prevented the chosen startup view.");
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig(), viewPreferencesPath: path);
        form.SetView(AppView.Simple);
        Check(form.CurrentView == AppView.Simple && Field<Button>(form, "btnSimpleActivity").Text.Contains("attention"),
            "An unwritable preference blocked switching views or hid the failure.");
    }

    private static void ViewSwitchPreservesSettings()
    {
        var (system, manager, path) = Setup(Target("setting"));
        string config = SwitchConfig();
        string viewPath = Path.Combine(_root, "switch-view.json");
        using var form = new TestMainForm(false, manager, config, viewPreferencesPath: viewPath);
        string originals = File.ReadAllText(path);
        string savedProfile = File.ReadAllText(config);
        Field<CheckBox>(form, "chkEdge").Checked = true;
        Field<TextBoxBase>(form, "txtEcoQosProcs").Text = "my-app";
        form.SetView(AppView.Simple);
        Check(new ViewPreferences(viewPath).Load() == AppView.Simple && File.ReadAllText(config) == savedProfile &&
            Field<CheckBox>(form, "chkEdge").Checked && Field<TextBoxBase>(form, "txtEcoQosProcs").Text == "my-app" &&
            File.ReadAllText(path) == originals && system.Writes == 0, "Switching views saved/discarded edits or changed originals/system settings.");
        ClickEcoSwitch(form);
        int writes = system.Writes;
        form.SetView(AppView.Advanced);
        form.SetView(AppView.Simple);
        Check(manager.HasPendingChanges && system.Writes == writes && Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.On,
            "Switching views disturbed active Eco Mode.");
        ClickEcoSwitch(form);
        Check(!manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 37, "The Simple switch did not restore the original settings.");
    }

    private static void SimpleViewRecovery()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig(), AppView.Simple);
        Check(Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.RestorePending &&
            Field<Label>(form, "lblSimpleOriginals").Text.Contains("RETRY") && Field<Button>(form, "btnSimpleRestore").Enabled,
            "Simple view hid pending recovery or the restore action.");
        // Invoke the click event without displaying this test form.
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Field<Button>(form, "btnSimpleRestore"), new object[] { EventArgs.Empty });
        Check(manager.HasPendingChanges && Field<Button>(form, "btnSimpleActivity").Text.Contains("attention"),
            "Simple view hid a failed restoration.");
        system.FailWrite = null;
        ClickEcoSwitch(form);
        Check(!manager.HasPendingChanges && Field<Label>(form, "lblSimpleOriginals").Text.Contains("are saved"),
            "Simple retry could not complete restoration.");
    }

    private static void SimpleViewLayout()
    {
        var (_, manager, _) = Setup();
        string config = SwitchConfig();
        new AppState { GpuPrefApps = [] }.Save(config);
        using var form = new TestMainForm(false, manager, config, AppView.Simple);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        foreach (var size in new[] { new Size(780, 760), new Size(700, 700) })
        {
            form.Size = size;
            Invoke(form, "OnFormShown", form, EventArgs.Empty);
            form.PerformLayout();
            CheckHeaderLayout(form);
            Check(Field<SplitContainer>(form, "splitTop").Panel2Collapsed && Field<SplitContainer>(form, "splitMain").Panel2Collapsed &&
                !Field<CheckBox>(form, "chkPowerPlan").Visible, "Simple view still exposes the advanced settings panels.");
            var card = Field<TableLayoutPanel>(form, "_powerControls").Parent!;
            foreach (string name in new[] { "ecoSwitch", "lblSimpleOriginals", "btnSimpleRestore", "btnSimpleActivity" })
            {
                var control = Field<Control>(form, name);
                Check(control.Visible && card.ClientRectangle.Contains(card.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))),
                    $"{name} is clipped in Simple view at {size}.");
            }
            foreach (string name in new[] { "lblSimpleSummary", "lblSimpleOriginals", "lblSimpleAccess" })
            {
                var label = Field<Label>(form, name);
                Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"{name} is vertically clipped.");
            }
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(Path.Combine(AppContext.BaseDirectory, $"ui-simple-{size.Width}.png"));
        }
        Field<Button>(form, "btnSimpleActivity").PerformClick();
        Check(!Field<SplitContainer>(form, "splitMain").Panel2Collapsed && form.CurrentView == AppView.Simple,
            "Opening activity unexpectedly changed views or left activity hidden.");
        Field<ComboBox>(form, "cboView").SelectedIndex = 1;
        Check(form.CurrentView == AppView.Advanced, "The header cannot open Advanced view.");
        Field<ToolStripMenuItem>(form, "miSimpleView").PerformClick();
        Check(form.CurrentView == AppView.Simple, "The View menu cannot return to Simple view.");

        using var choice = new ViewChoiceDialog();
        choice.StartPosition = FormStartPosition.Manual;
        choice.Location = new Point(-32000, -32000);
        choice.Show();
        choice.PerformLayout();
        foreach (var button in choice.Controls[0].Controls.OfType<Button>())
            Check(button.Height >= button.GetPreferredSize(new Size(button.Width, 0)).Height && button.TabStop,
                "A welcome choice is clipped or unavailable from the keyboard.");
        using var welcome = new Bitmap(choice.Width, choice.Height);
        choice.DrawToBitmap(welcome, new Rectangle(Point.Empty, choice.Size));
        welcome.Save(Path.Combine(AppContext.BaseDirectory, "ui-welcome.png"));
        ((Button)choice.AcceptButton!).PerformClick();
        Check(choice.SelectedView == AppView.Simple && choice.DialogResult == DialogResult.OK, "The recommended welcome action did not select Simple.");
        using var advancedChoice = new ViewChoiceDialog();
        var advanced = advancedChoice.Controls[0].Controls.OfType<Button>().Single(button => button.Text.StartsWith("Advanced"));
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(advanced, new object[] { EventArgs.Empty });
        Check(advancedChoice.SelectedView == AppView.Advanced, "The welcome dialog cannot choose Advanced view.");
    }

    private static void ViewPickerRoundTrips()
    {
        string viewPath = Path.Combine(_root, "view-picker.json");
        string config = SwitchConfig();
        string profile = File.ReadAllText(config);
        var (system, manager, originalPath) = Setup(Target("setting"));
        using var form = new TestMainForm(false, manager, config, AppView.Simple, viewPath);
        string originals = File.ReadAllText(originalPath);
        var picker = Field<ComboBox>(form, "cboView");
        var standard = Field<ToolStripMenuItem>(form, "miSimpleView");
        var advanced = Field<ToolStripMenuItem>(form, "miAdvancedView");
        try
        {
            foreach (var language in L.Languages)
            {
                L.SetLanguage(language.Code);
                Check(picker.DropDownStyle == ComboBoxStyle.DropDownList && picker.TabStop &&
                    picker.Items.Cast<ViewOption>().Select(option => option.ToString()).SequenceEqual(new[] { L.T("Standard view"), L.T("Advanced view") }),
                    "The header does not offer both localized view choices.");
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    picker.SelectedIndex = 1;
                    Check(form.CurrentView == AppView.Advanced && advanced.Checked && !standard.Checked &&
                        new ViewPreferences(viewPath).Load() == AppView.Advanced, "The header could not select or remember Advanced view.");
                    standard.PerformClick();
                    Check(form.CurrentView == AppView.Simple && picker.SelectedIndex == 0 && standard.Checked && !advanced.Checked,
                        "The menu could not return to Standard view or update the header.");
                    advanced.PerformClick();
                    picker.SelectedIndex = 0;
                    Check(form.CurrentView == AppView.Simple && standard.Checked && !advanced.Checked &&
                        new ViewPreferences(viewPath).Load() == AppView.Simple, "The header could not return to or remember Standard view.");
                }
            }
            Check(File.ReadAllText(config) == profile && File.ReadAllText(originalPath) == originals && system.Writes == 0,
                "Changing the view modified the profile, originals or system settings.");
            using var reopened = new TestMainForm(false, manager, config, initialView: null, viewPreferencesPath: viewPath);
            Check(reopened.CurrentView == AppView.Simple && Field<ComboBox>(reopened, "cboView").SelectedIndex == 0,
                "Reopening did not restore the selected view in the header.");
        }
        finally { L.SetLanguage("en"); }
    }
}
