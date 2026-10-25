namespace ecomode;

public partial class MainForm
{
    private readonly LanguagePreferences _languagePreferences;
    private readonly ToolStripMenuItem languageMenu = new("&Language");
    private bool _updatingLanguage;

    private void BuildLanguageMenu()
    {
        foreach (var language in L.Languages)
        {
            var item = new ToolStripMenuItem(language.NativeName) { Tag = language.Code, Checked = language.Code == L.Language };
            item.Click += (_, _) => SetLanguage(language.Code);
            languageMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(languageMenu);
    }

    internal void SetLanguage(string code)
    {
        L.SetLanguage(code);
        try { _languagePreferences.Save(code); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWarn(L.T("Your language choice could not be saved. You may be asked again next time. {0}", ex.Message));
        }
    }

    private void OnLanguageChanged()
    {
        _updatingLanguage = true;
        SuspendLayout();
        try
        {
            L.TranslateTree(this);
            L.TranslateItems(trayMenu.Items);
            foreach (ToolStripMenuItem item in languageMenu.DropDownItems)
                item.Checked = (string)item.Tag! == L.Language;
            foreach (var box in new[] { chkPowerPlan, chkEdge, chkChrome, chkGpuPref, chkEcoQos })
            {
                var toggle = (UtilitySwitch)box;
                toggle.RefreshLanguage();
                var options = (TableLayoutPanel)box.Parent!;
                box.AccessibleName = options.GetControlFromPosition(0, options.GetRow(box))!.Text;
            }
            ecoSwitch.RefreshLanguage();
            chkAutomatic.RefreshLanguage();
            ecoSwitch.AccessibleName = L.T("Eco Mode");
            ecoSwitch.State = ecoSwitch.State;
            cboPresets.Refresh();
            lblPresetDescription.Text = (cboPresets.SelectedItem as ProfilePreset)?.Description ?? "";
            UpdateActivityButton();
            ApplyViewLayout(resize: false);
            RefreshOriginalSettingsStatus();
        }
        finally { ResumeLayout(performLayout: true); _updatingLanguage = false; }
        LayoutCurrentView();
    }
}
