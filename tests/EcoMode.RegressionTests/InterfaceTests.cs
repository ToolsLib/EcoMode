using ecomode;
using System.Reflection;

internal static partial class Program
{
    private static void RunInterfaceTests()
    {
        Run("Eco switch applies and restores settings while reflecting the confirmed state", EcoSwitchTransitions);
        Run("first startup saves originals without enabling Eco Mode or changing the profile", StartupCapturesOriginals);
        Run("failed capture leaves the Eco switch off and usable", EcoSwitchCaptureFailure);
        Run("pending recovery uses RETRY and cannot accidentally enable Eco Mode", EcoSwitchRecovery);
        Run("Eco switch exposes distinct accessible states and does not auto-check", EcoSwitchAccessibility);
        Run("header text grows without overlapping at larger font sizes", HeaderFontLayout);
        Run("option switches support direct and label clicks and persist profile selections", OptionSwitchSelections);
    }

    private static string SwitchConfig()
    {
        string path = Path.Combine(_root, "switch-" + Guid.NewGuid().ToString("N") + ".json");
        // Exercise the controller with a fake settings backend and no native process scan.
        new AppState
        {
            IncludePowerPlan = false,
            IncludeEdge = false,
            IncludeChrome = false,
            IncludeGpuPref = false,
            IncludeEcoQos = false,
            GpuPrefApps = []
        }.Save(path);
        return path;
    }

    private static void ClickEcoSwitch(MainForm form) => typeof(Control)
        .GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(Field<EcoModeSwitch>(form, "ecoSwitch"), new object[] { EventArgs.Empty });

    private static void EcoSwitchTransitions()
    {
        var (system, manager, path) = Setup(Target("setting"));
        using var form = new TestMainForm(false, manager, SwitchConfig());
        string originals = File.ReadAllText(path);
        var toggle = Field<EcoModeSwitch>(form, "ecoSwitch");
        Check(toggle.State == EcoSwitchState.Off, "Switch did not start off.");
        ClickEcoSwitch(form);
        Check(toggle.State == EcoSwitchState.On && toggle.CheckState == CheckState.Checked && Field<bool>(form, "_ecoEnabled"),
            "Successful apply did not show ON.");
        Check(manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 1, "Switch did not apply the selected settings.");
        SaveInterfaceSnapshot(form, "ui-switch-on.png");
        ClickEcoSwitch(form);
        Check(toggle.State == EcoSwitchState.Off && !manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 37,
            "Switching off did not restore the original settings.");
        ClickEcoSwitch(form);
        ClickEcoSwitch(form);
        Check(File.ReadAllText(path) == originals && toggle.Enabled, "Switching again replaced the saved baseline.");
    }

    private static void StartupCapturesOriginals()
    {
        var (system, manager, _) = Setup(Target("setting"));
        string config = SwitchConfig();
        string profile = File.ReadAllText(config);
        using var form = new TestMainForm(false, manager, config);
        var toggle = Field<EcoModeSwitch>(form, "ecoSwitch");
        Check(toggle.State == EcoSwitchState.Off && toggle.CheckState == CheckState.Unchecked && toggle.Enabled,
            "Saving originals on startup enabled Eco Mode or disabled the switch.");
        Check(manager.HasSnapshot && !manager.HasPendingChanges && system.Writes == 0, "Startup did not save originals or changed system settings.");
        Check(Field<Label>(form, "lblOriginalStatus").Text == "ORIGINALS  /  SAVED" && File.ReadAllText(config) == profile,
            "Startup did not show saved originals or changed the profile selections.");
    }

    private static void EcoSwitchCaptureFailure()
    {
        var (system, manager, _) = Setup(Target("setting"));
        system.FailRead = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig());
        Check(!manager.HasSnapshot && Field<Label>(form, "lblOriginalStatus").Text == "ORIGINALS  /  SAVE FAILED",
            "Startup capture failure was not visible.");
        ClickEcoSwitch(form);
        var toggle = Field<EcoModeSwitch>(form, "ecoSwitch");
        Check(toggle.State == EcoSwitchState.Off && toggle.Enabled && !manager.HasPendingChanges && system.Writes == 0,
            "Failed capture falsely enabled Eco Mode or disabled the switch.");
        system.FailRead = null;
        ClickEcoSwitch(form);
        Check(toggle.State == EcoSwitchState.On && manager.HasSnapshot, "Capture could not be retried when switching on.");
        ClickEcoSwitch(form);
    }

    private static void EcoSwitchRecovery()
    {
        var (system, manager, _) = Setup(Target("setting"));
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        system.FailWrite = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig());
        var toggle = Field<EcoModeSwitch>(form, "ecoSwitch");
        Check(toggle.State == EcoSwitchState.RestorePending && toggle.Text == "RETRY" && toggle.CheckState == CheckState.Indeterminate,
            "Startup recovery was presented as a clean OFF state.");
        ClickEcoSwitch(form);
        Check(toggle.State == EcoSwitchState.RestorePending && manager.HasPendingChanges && !Field<bool>(form, "_ecoEnabled") && toggle.Enabled,
            "Failed retry enabled Eco Mode or hid pending recovery.");
        SaveInterfaceSnapshot(form, "ui-switch-recovery.png");
        system.FailWrite = null;
        ClickEcoSwitch(form);
        Check(toggle.State == EcoSwitchState.Off && !manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 37,
            "Successful retry did not return to a confirmed OFF state.");
    }

    private static void EcoSwitchAccessibility()
    {
        using var toggle = new EcoModeSwitch();
        Check(!toggle.AutoCheck && toggle.ThreeState && toggle.TabStop && toggle.AccessibleRole == AccessibleRole.CheckButton,
            "The switch lost keyboard navigation or its native checkbox semantics.");
        foreach (var (state, native, caption) in new[]
        {
            (EcoSwitchState.Off, CheckState.Unchecked, "OFF"),
            (EcoSwitchState.On, CheckState.Checked, "ON"),
            (EcoSwitchState.RestorePending, CheckState.Indeterminate, "RETRY")
        })
        {
            toggle.State = state;
            Check(toggle.CheckState == native && toggle.Text == caption && toggle.AccessibleName == "Eco Mode" &&
                !string.IsNullOrWhiteSpace(toggle.AccessibleDescription), "Visible and accessible switch states disagree.");
        }
    }

    private static void HeaderFontLayout()
    {
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig());
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        using var titleFont = new Font(UtilityTheme.TitleFont.FontFamily, 30F, FontStyle.Bold);
        using var subtitleFont = new Font(UtilityTheme.BodyFont.FontFamily, 12F);
        Field<Label>(form, "lblAppTitle").Font = titleFont;
        Field<Label>(form, "lblAppSubtitle").Font = subtitleFont;
        form.Size = new Size(1200, 900);
        form.PerformLayout();
        CheckHeaderLayout(form);
    }

    private static void OptionSwitchSelections()
    {
        var (system, manager, _) = Setup(Target("setting"));
        string config = SwitchConfig();
        using var form = new TestMainForm(false, manager, config);
        var options = (TableLayoutPanel)form.Controls.Find("optionSettings", true).Single();
        for (int row = 0; row < 5; row++)
        {
            var toggle = (UtilitySwitch)options.GetControlFromPosition(1, row)!;
            var label = (Label)options.GetControlFromPosition(0, row)!;
            Check(toggle.AutoCheck && !toggle.ThreeState && toggle.TabStop && toggle.AccessibleName == label.Text,
                "An option switch lost its keyboard or accessible label support.");
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(toggle, new object[] { EventArgs.Empty });
            Check(toggle.Checked && toggle.Text == "ON", "Clicking an option switch did not select it.");
            typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(label, new object[] { EventArgs.Empty });
            Check(!toggle.Checked && toggle.Text == "OFF", "Clicking an option label did not deselect it.");
            toggle.Checked = row % 2 == 0;
        }
        Invoke(form, "SaveFromUi");
        var saved = AppState.Load(config);
        Check(saved.IncludePowerPlan && !saved.IncludeEdge && saved.IncludeChrome && !saved.IncludeGpuPref && saved.IncludeEcoQos,
            "Saving the profile lost the option switch selections.");
        Check(system.Writes == 0 && !manager.HasPendingChanges && !Field<bool>(form, "_ecoEnabled"),
            "Editing profile options unexpectedly applied Eco Mode.");
        using var reopened = new TestMainForm(false, manager, config);
        foreach (string name in new[] { "chkPowerPlan", "chkEdge", "chkChrome", "chkGpuPref", "chkEcoQos" })
            Check(Field<CheckBox>(reopened, name).Checked == Field<CheckBox>(form, name).Checked,
                "Reopening the profile did not restore the option switch selections.");
    }

    private static void CheckOptionLayout(MainForm form)
    {
        var options = (TableLayoutPanel)form.Controls.Find("optionSettings", true).Single();
        var area = Field<SplitContainer>(form, "splitTop").Panel1;
        int? right = null;
        for (int row = 0; row < 5; row++)
        {
            var label = (Label)options.GetControlFromPosition(0, row)!;
            var toggle = (UtilitySwitch)options.GetControlFromPosition(1, row)!;
            Check(right is null || toggle.Right == right, "Option switches are not aligned on the right.");
            right = toggle.Right;
            Check(label.Right <= toggle.Left && label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height,
                $"Option label is clipped or overlaps its switch: {label.Text}.");
            foreach (Control control in new Control[] { label, toggle })
                Check(area.ClientRectangle.Contains(area.RectangleToClient(control.RectangleToScreen(control.ClientRectangle))),
                    "An option is outside the visible control area.");
        }
    }

    private static void CheckHeaderLayout(MainForm form)
    {
        var title = Field<Label>(form, "lblAppTitle");
        var subtitle = Field<Label>(form, "lblAppSubtitle");
        Check(title.Parent == subtitle.Parent && title.Bottom <= subtitle.Top, "App title overlaps the subtitle.");
        foreach (var label in new[] { title, subtitle, Field<Label>(form, "lblStatus"), Field<Label>(form, "lblStatusDetail") })
        {
            Check(label.Parent!.ClientRectangle.Contains(label.Bounds), $"{label.Text} is outside its header container.");
            Check(label.Height >= label.GetPreferredSize(new Size(label.Width, 0)).Height, $"{label.Text} is vertically clipped.");
        }
        var header = form.Controls.Find("appHeader", true).Single();
        var body = Field<SplitContainer>(form, "splitMain");
        Check(header.Bottom <= body.Top, "Main controls cover the app subtitle.");
        var status = Field<Label>(form, "lblStatus");
        var toggle = Field<EcoModeSwitch>(form, "ecoSwitch");
        Check(!status.RectangleToScreen(status.ClientRectangle).IntersectsWith(toggle.RectangleToScreen(toggle.ClientRectangle)),
            "Eco switch overlaps its status text.");
    }

    private static void SaveInterfaceSnapshot(MainForm form, string name)
    {
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        form.ShowInTaskbar = false;
        form.Show();
        Invoke(form, "OnFormShown", form, EventArgs.Empty);
        form.PerformLayout();
        CheckHeaderLayout(form);
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, name));
    }
}
