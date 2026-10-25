using ecomode;
using System.Collections;
using System.Globalization;
using System.Text;

internal static partial class Program
{
    private static void RunLocalizationTests()
    {
        try
        {
            Run("all four resource catalogs are complete and preserve format placeholders", ResourceCatalogs);
            Run("language detection and preferences handle regional, unsupported and invalid choices", LanguageFallbacks);
            Run("live language changes preserve profile edits, originals and active Eco Mode", LiveLanguageChanges);
            Run("language save failures keep the selected language and show a warning", LanguageSaveFailure);
            Run("welcome language selection updates the choices and remembers the language", WelcomeLanguages);
            Run("both views and recovery controls fit in every supported language", LocalizedLayouts);
        }
        finally { L.SetLanguage("en"); }
    }

    private static void ResourceCatalogs()
    {
        var english = L.Resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)!;
        Check(english.Cast<DictionaryEntry>().Count() >= 200, "The fallback catalog is missing.");
        foreach (var language in L.Languages)
        {
            var localized = L.Resources.GetResourceSet(CultureInfo.GetCultureInfo(language.Code), true, false)!;
            Check(localized != null && localized.Cast<DictionaryEntry>().Count() == english.Cast<DictionaryEntry>().Count(),
                $"The {language.Code} catalog is incomplete or not packaged.");
            foreach (DictionaryEntry entry in english)
            {
                string? value = localized!.GetString((string)entry.Key);
                Check(!string.IsNullOrWhiteSpace(value), $"Missing {language.Code} translation: {entry.Key}");
                var sourceFormat = CompositeFormat.Parse((string)entry.Value!);
                var translatedFormat = CompositeFormat.Parse(value!);
                Check(sourceFormat.MinimumArgumentCount == translatedFormat.MinimumArgumentCount,
                    $"Format arguments changed in {language.Code}: {entry.Key}");
                var arguments = Enumerable.Range(0, sourceFormat.MinimumArgumentCount).Select(index => (object)$"ARG{index}").ToArray();
                string formatted = string.Format(CultureInfo.GetCultureInfo(language.Code), value!, arguments);
                foreach (var argument in arguments)
                    Check(formatted.Contains(argument.ToString()!), $"A format argument disappeared in {language.Code}: {entry.Key}");
            }
        }
        Check(L.T("An unknown optional resource") == "An unknown optional resource", "Unknown resources do not fall back to their source.");
    }

    private static void LanguageFallbacks()
    {
        foreach (var (culture, expected) in new[] { ("fr-CA", "fr"), ("es-MX", "es"), ("zh-TW", "zh-Hans"), ("en-GB", "en"), ("hi-IN", "en") })
            Check(L.DetectLanguage(CultureInfo.GetCultureInfo(culture)) == expected, "Regional/unsupported Windows language was not resolved correctly.");
        string path = Path.Combine(_root, "language-preference.json");
        var preference = new LanguagePreferences(path);
        Check(preference.Load() == null, "A missing preference did not use the Windows default.");
        foreach (string invalid in new[] { "{broken", "null", "{}", "{\"Language\":\"xx\"}", "{\"Language\":42}" })
        {
            File.WriteAllText(path, invalid);
            Check(preference.Load() == null, "Invalid language preferences were accepted.");
        }
        preference.Save("fr");
        Check(new LanguagePreferences(path).Load() == "fr", "The language preference did not survive reopening.");
        Throws(() => preference.Save("de"));
        Throws(() => L.SetLanguage("de"));
        Check(preference.Load() == "fr", "An unsupported language replaced the saved choice.");
    }

    private static void LiveLanguageChanges()
    {
        var (system, manager, originalPath) = Setup(Target("setting"));
        string config = SwitchConfig();
        string languagePath = Path.Combine(_root, "live-language.json");
        using var form = new TestMainForm(false, manager, config, languagePreferencesPath: languagePath);
        string originals = File.ReadAllText(originalPath);
        string savedProfile = File.ReadAllText(config);
        Field<CheckBox>(form, "chkEdge").Checked = true;
        Field<TextBoxBase>(form, "txtEcoQosProcs").Text = "Save\r\nmy-app";
        foreach (var language in L.Languages)
        {
            form.SetLanguage(language.Code);
            Check(new LanguagePreferences(languagePath).Load() == language.Code && Field<Label>(form, "lblStatus").Text == L.T("Eco mode is off"),
                "A language change was not applied or saved.");
            Check(Field<Button>(form, "btnSaveOriginal").Text == L.T("Save originals") && Field<ToolStripMenuItem>(form, "fileMenu").Text == L.T("&File"),
                "Static controls or menus did not update to the selected language.");
            Check(File.ReadAllText(config) == savedProfile && File.ReadAllText(originalPath) == originals && system.Writes == 0 &&
                Field<TextBoxBase>(form, "txtEcoQosProcs").Text.ReplaceLineEndings("\n") == "Save\nmy-app" && Field<CheckBox>(form, "chkEdge").Checked,
                "Language switching translated editable data, discarded edits, or changed settings.");
        }
        ClickEcoSwitch(form);
        int writes = system.Writes;
        foreach (var language in L.Languages)
        {
            form.SetLanguage(language.Code);
            Check(Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.On && Field<EcoModeSwitch>(form, "ecoSwitch").Text == L.T("ON") &&
                system.Writes == writes && manager.HasPendingChanges, "Language switching disturbed active Eco Mode or its switch state.");
        }
        ClickEcoSwitch(form);
        Check(!manager.HasPendingChanges && system.Values["setting"].Data.GetInt32() == 37, "A localized switch could not restore originals.");
        form.ImportProfileJson("{\"GpuPrefApps\":[],\"IncludeEcoQos\":false}");
        form.LoadPreset("balanced");
        form.SetView(AppView.Simple);
        Check(new LanguagePreferences(languagePath).Load() == L.Language, "A profile or view change replaced the language choice.");
        L.SetLanguage("en");
    }

    private static void LanguageSaveFailure()
    {
        string path = Path.Combine(_root, "unwritable-language.json");
        Directory.CreateDirectory(path);
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, SwitchConfig(), AppView.Simple, languagePreferencesPath: path);
        form.SetLanguage("fr");
        Check(L.Language == "fr" && Field<Button>(form, "btnSimpleActivity").Text == L.T("View activity (needs attention)"),
            "An unwritable language preference blocked language switching or hid the failure.");
        L.SetLanguage("en");
    }

    private static void WelcomeLanguages()
    {
        string path = Path.Combine(_root, "welcome-language.json");
        using var dialog = new ViewChoiceDialog(new LanguagePreferences(path));
        var picker = (ComboBox)dialog.Controls.Find("welcomeLanguage", true).Single();
        foreach (var language in L.Languages)
        {
            picker.SelectedItem = language;
            Check(L.Language == language.Code && dialog.Text == L.T("Welcome to EcoMode") && dialog.SelectedView == AppView.Simple,
                "The welcome language picker did not translate the choices or changed the default view.");
            if (language.Code != "en") Check(new LanguagePreferences(path).Load() == language.Code, "The welcome language choice was not remembered.");
        }
        L.SetLanguage("en");
    }

    private static void LocalizedLayouts()
    {
        foreach (var language in L.Languages)
        {
            L.SetLanguage(language.Code);
            var (system, manager, _) = Setup(Target("setting"));
            string config = SwitchConfig();
            new AppState { GpuPrefApps = [] }.Save(config);
            using var form = new TestMainForm(false, manager, config, AppView.Simple);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
            foreach (var view in new[] { AppView.Simple, AppView.Advanced })
            {
                form.SetView(view);
                form.Size = view == AppView.Simple ? new Size(700, 700) : new Size(1100, 700);
                Invoke(form, "OnFormShown", form, EventArgs.Empty);
                form.PerformLayout();
                Application.DoEvents();
                SaveLocalizationSnapshot(form, $"ui-{language.Code}-{view}.png");
                CheckHeaderLayout(form);
                if (view == AppView.Advanced) CheckOptionLayout(form);
                foreach (string name in view == AppView.Simple ? new[] { "btnSimpleRestore", "btnSimpleActivity" } : new[] { "btnSaveOriginal", "btnRestoreOriginal" })
                {
                    var button = Field<Button>(form, name);
                    var card = Field<TableLayoutPanel>(form, "_powerControls").Parent!;
                    Check(button.Visible && card.ClientRectangle.Contains(card.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))),
                        $"{language.Code}/{view}: {name} is clipped at the minimum window size. Bounds={card.RectangleToClient(button.RectangleToScreen(button.ClientRectangle))}, card={card.ClientRectangle}, form={form.Size}, minimum={form.MinimumSize}, working={Screen.FromControl(form).WorkingArea}, content={Field<TableLayoutPanel>(form, "_powerControls").Bounds}, split={Field<SplitContainer>(form, "splitMain").Height}, preferred={Field<TableLayoutPanel>(form, "_powerControls").GetPreferredSize(new Size(card.ClientSize.Width - card.Padding.Horizontal, 0))}.");
                    Check(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width,
                        $"{language.Code}/{view}: {name} caption is clipped.");
                }
                if (view == AppView.Advanced)
                {
                    var picker = Field<ComboBox>(form, "cboPresets");
                    foreach (var preset in ProfilePresets.All)
                    {
                        picker.SelectedItem = preset;
                        form.PerformLayout();
                        CheckHeaderLayout(form);
                        CheckOptionLayout(form);
                    }
                }
                manager.ApplyEco(Profile, false);
                Invoke(form, "RefreshOriginalSettingsStatus");
                form.PerformLayout();
                CheckHeaderLayout(form);
                Check(Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.RestorePending &&
                    Field<EcoModeSwitch>(form, "ecoSwitch").Text == L.T("RETRY"), "Localized recovery did not expose the retry state.");
                SaveLocalizationSnapshot(form, $"ui-{language.Code}-{view}-Recovery.png");
                Check(manager.Restore(false) && system.Values["setting"].Data.GetInt32() == 37, "Localized layout test did not restore its fake setting.");
                Invoke(form, "RefreshOriginalSettingsStatus");
            }
            using var welcome = new ViewChoiceDialog(new LanguagePreferences(Path.Combine(_root, "layout-language.json")));
            welcome.StartPosition = FormStartPosition.Manual;
            welcome.Location = new Point(-32000, -32000);
            welcome.Show();
            welcome.PerformLayout();
            foreach (var button in welcome.Controls[0].Controls.OfType<Button>())
            {
                int height = TextRenderer.MeasureText(button.Text, button.Font,
                    new Size(button.Width - button.Padding.Horizontal - 12, int.MaxValue), TextFormatFlags.WordBreak).Height;
                Check(button.Height >= height + button.Padding.Vertical, $"{language.Code}: welcome button is clipped.");
            }
            SaveLocalizationSnapshot(welcome, $"ui-{language.Code}-Welcome.png");
        }
    }

    private static void SaveLocalizationSnapshot(Form form, string file)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, file));
    }
}
