namespace ecomode;

public partial class MainForm
{
    private readonly Label lblStatusDetail = UtilityTheme.Label("Ready to save power.", muted: true);
    private readonly Label lblOriginalStatus = UtilityTheme.Label("SAVED ORIGINALS", muted: true);

    private void BuildUi()
    {
        Text = "EcoMode";
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = UtilityTheme.BodyFont;
        BackColor = UtilityTheme.Background;
        ForeColor = UtilityTheme.Text;
        AutoScaleMode = AutoScaleMode.Dpi;
        // Controls are laid out at runtime, rather than loaded from a 96-DPI designer.
        AutoScaleDimensions = CurrentAutoScaleDimensions;

        BuildMenus();
        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 18, 18) };
        Controls.Add(root);
        var header = BuildHeader();
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 10,
            BackColor = UtilityTheme.Background, FixedPanel = FixedPanel.Panel2
        };
        root.Controls.Add(splitMain);
        root.Controls.Add(header);
        root.BringToFront();

        splitTop = new SplitContainer
        {
            Dock = DockStyle.Fill, SplitterWidth = 14, BackColor = UtilityTheme.Background
        };
        splitMain.Panel1.Controls.Add(splitTop);
        BuildControlPanel();
        BuildApplicationRules();
        BuildActivityPanel();
        Shown += OnFormShown;
    }

    private void BuildMenus()
    {
        miSave.ShortcutKeys = Keys.Control | Keys.S;
        miSave.Click += OnMenuSave;
        miQuit.ShortcutKeys = Keys.Control | Keys.Q;
        miQuit.Click += OnMenuQuit;
        miImport.Click += OnMenuImportProfile;
        miExport.Click += OnMenuExportProfile;
        miReset.Click += OnMenuResetDefaults;
        fileMenu.DropDownItems.AddRange([miSave, new ToolStripSeparator(), miQuit]);
        editMenu.DropDownItems.AddRange([miImport, miExport, new ToolStripSeparator(), miReset]);
        menu.Items.AddRange([fileMenu, editMenu]);
        menu.Dock = DockStyle.Top;
        UtilityTheme.StyleMenu(menu);
        MainMenuStrip = menu;
        Controls.Add(menu);

        trayMenu.Items.AddRange([miTrayShow, new ToolStripSeparator(), miTraySave, new ToolStripSeparator(), miTrayQuit]);
        miTrayShow.Click += (_, _) => RestoreFromTray();
        miTraySave.Click += (_, _) => SaveFromUi();
        miTrayQuit.Click += OnMenuQuit;
        UtilityTheme.StyleMenu(trayMenu);
        trayIcon.Text = "EcoMode";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 58 };
        var title = UtilityTheme.Label("EcoMode");
        title.Font = UtilityTheme.TitleFont;
        title.Location = new Point(0, 0);
        var subtitle = UtilityTheme.Label("POWER CONTROL  /  Keep background work in check.", muted: true);
        subtitle.Location = new Point(2, 36);
        var access = UtilityTheme.Label(_isAdmin ? "ADMINISTRATOR ACCESS" : "STANDARD ACCESS", muted: true);
        access.Font = UtilityTheme.StrongFont;
        access.Dock = DockStyle.Top;
        access.TextAlign = ContentAlignment.MiddleRight;
        access.AutoSize = false;
        access.Height = 23;
        var scope = UtilityTheme.Label(_isAdmin ? "All settings available" : "System settings need Administrator", muted: true);
        scope.AutoSize = false;
        scope.Dock = DockStyle.Top;
        scope.Height = 22;
        scope.TextAlign = ContentAlignment.MiddleRight;
        var accessPanel = new Panel { Dock = DockStyle.Right, Width = 320, Padding = new Padding(0, 6, 16, 0) };
        accessPanel.Controls.Add(scope);
        accessPanel.Controls.Add(access);
        UtilityTheme.StyleButton(btnDonate);
        btnDonate.Text = "Support";
        btnDonate.Width = 82;
        btnDonate.Dock = DockStyle.Right;
        btnDonate.Click += (_, _) => OpenDonatePage();
        var supportPanel = new Panel { Dock = DockStyle.Right, Width = 82, Padding = new Padding(0, 10, 0, 22) };
        supportPanel.Controls.Add(btnDonate);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(accessPanel);
        header.Controls.Add(supportPanel);
        return header;
    }

    private void BuildControlPanel()
    {
        var card = new UtilityCard { Dock = DockStyle.Fill, AutoScroll = true };
        splitTop.Panel1.Controls.Add(card);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(content);

        var mode = new TableLayoutPanel { Dock = DockStyle.Top, Height = 58, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        mode.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        var status = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        lblStatus.Dock = DockStyle.Top;
        lblStatus.Height = 30;
        lblStatus.Font = UtilityTheme.StatusFont;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatusDetail.Dock = DockStyle.Top;
        lblStatusDetail.AutoSize = false;
        lblStatusDetail.Height = 24;
        status.Controls.Add(lblStatusDetail);
        status.Controls.Add(lblStatus);
        UtilityTheme.StyleButton(btnEcoOn, primary: true);
        UtilityTheme.StyleButton(btnEcoOff);
        btnEcoOn.Text = "Eco ON";
        btnEcoOff.Text = "Eco OFF";
        btnEcoOn.Click += (_, _) => ToggleEco(true);
        btnEcoOff.Click += (_, _) => ToggleEco(false);
        btnEcoOn.Dock = btnEcoOff.Dock = DockStyle.Fill;
        btnEcoOn.Margin = btnEcoOff.Margin = new Padding(6, 8, 0, 10);
        mode.Controls.Add(status, 0, 0);
        mode.Controls.Add(btnEcoOn, 1, 0);
        mode.Controls.Add(btnEcoOff, 2, 0);
        AddSection(content, mode);
        AddDivider(content);

        var presets = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty
        };
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        var presetHeading = UtilityTheme.Label("PRESET PROFILE", muted: true);
        presetHeading.Font = UtilityTheme.StrongFont;
        presetHeading.Margin = new Padding(0, 0, 0, 7);
        presets.Controls.Add(presetHeading, 0, 0);
        presets.SetColumnSpan(presetHeading, 2);
        cboPresets.DropDownStyle = ComboBoxStyle.DropDownList;
        cboPresets.FlatStyle = FlatStyle.Flat;
        cboPresets.BackColor = UtilityTheme.Input;
        cboPresets.ForeColor = UtilityTheme.Text;
        cboPresets.Dock = DockStyle.Fill;
        cboPresets.AccessibleName = "Preset profile";
        cboPresets.Margin = new Padding(0, 3, 0, 3);
        cboPresets.Items.AddRange(ProfilePresets.All.Cast<object>().ToArray());
        cboPresets.SelectedIndexChanged += (_, _) =>
            lblPresetDescription.Text = (cboPresets.SelectedItem as ProfilePreset)?.Description ?? "";
        cboPresets.SelectedIndex = 0;
        UtilityTheme.StyleButton(btnLoadPreset);
        btnLoadPreset.Text = "Load";
        btnLoadPreset.Dock = DockStyle.Fill;
        btnLoadPreset.Click += OnLoadPreset;
        presets.Controls.Add(cboPresets, 0, 1);
        presets.Controls.Add(btnLoadPreset, 1, 1);
        lblPresetDescription.AutoSize = true;
        lblPresetDescription.Dock = DockStyle.Fill;
        lblPresetDescription.ForeColor = UtilityTheme.Muted;
        lblPresetDescription.Margin = new Padding(0, 7, 0, 0);
        presets.Controls.Add(lblPresetDescription, 0, 2);
        presets.SetColumnSpan(lblPresetDescription, 2);
        AddSection(content, presets);
        AddDivider(content);

        var optionHeader = new Panel { Dock = DockStyle.Top, Height = 29, Margin = Padding.Empty };
        var optionTitle = UtilityTheme.Label("APPLY TO", muted: true);
        optionTitle.Font = UtilityTheme.StrongFont;
        optionTitle.Dock = DockStyle.Left;
        UtilityTheme.StyleButton(btnSave);
        btnSave.Text = "Save profile";
        btnSave.Dock = DockStyle.Right;
        btnSave.Width = 104;
        btnSave.Height = 25;
        btnSave.Click += (_, _) => SaveFromUi();
        optionHeader.Controls.Add(optionTitle);
        optionHeader.Controls.Add(btnSave);
        AddSection(content, optionHeader);
        var options = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var settings = new (CheckBox Box, string Text)[]
        {
            (chkPowerPlan, "Power plan · EPP and CPU boost"),
            (chkEdge, "Edge · efficiency + hardware acceleration off"),
            (chkChrome, "Chrome · battery saver + hardware acceleration off"),
            (chkGpuPref, "GPU preference · power-saving graphics"),
            (chkEcoQos, "Background processes · EcoQoS")
        };
        foreach (var (box, text) in settings)
        {
            box.Text = text;
            box.FlatStyle = FlatStyle.Flat;
            box.BackColor = UtilityTheme.Surface;
            box.ForeColor = UtilityTheme.Text;
            box.Dock = DockStyle.Fill;
            box.Height = 25;
            box.Margin = Padding.Empty;
            options.Controls.Add(box, 0, options.Controls.Count);
        }
        AddSection(content, options);
        AddDivider(content);

        var originals = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        originals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        originals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lblOriginalStatus.Font = UtilityTheme.StrongFont;
        lblOriginalStatus.Margin = new Padding(0, 0, 0, 8);
        originals.Controls.Add(lblOriginalStatus, 0, 0);
        originals.SetColumnSpan(lblOriginalStatus, 2);
        UtilityTheme.StyleButton(btnSaveOriginal);
        UtilityTheme.StyleButton(btnRestoreOriginal);
        btnSaveOriginal.Text = "Save originals";
        btnRestoreOriginal.Text = "Restore originals";
        btnSaveOriginal.Dock = btnRestoreOriginal.Dock = DockStyle.Fill;
        btnSaveOriginal.Margin = new Padding(0, 0, 4, 0);
        btnRestoreOriginal.Margin = new Padding(4, 0, 0, 0);
        btnSaveOriginal.Click += (_, _) => SaveOriginalSettings();
        btnRestoreOriginal.Click += (_, _) => RestoreOriginalSettings(restoreAll: true);
        originals.Controls.Add(btnSaveOriginal, 0, 1);
        originals.Controls.Add(btnRestoreOriginal, 1, 1);
        AddSection(content, originals);
    }

    private static void AddSection(TableLayoutPanel layout, Control section)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(section, 0, layout.Controls.Count);
    }

    private static void AddDivider(TableLayoutPanel layout)
    {
        var gap = new Panel { Dock = DockStyle.Top, Height = 11, Padding = new Padding(0, 5, 0, 5), Margin = Padding.Empty };
        gap.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UtilityTheme.Border });
        AddSection(layout, gap);
    }

    private void BuildApplicationRules()
    {
        splitRight = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 12,
            BackColor = UtilityTheme.Background
        };
        splitTop.Panel2.Controls.Add(splitRight);
        var gpuCard = BuildEditor(txtGpuApps, "GPU APPLICATIONS", "Full .exe paths · one per line", btnBrowseGpu);
        UtilityTheme.StyleButton(btnBrowseGpu);
        btnBrowseGpu.Text = "Add app";
        btnBrowseGpu.Click += (_, _) => BrowseAndAddGpuApps();
        splitRight.Panel1.Controls.Add(gpuCard);
        splitRight.Panel2.Controls.Add(BuildEditor(txtEcoQosProcs, "NEVER THROTTLE", "Process names · one per line"));
    }

    private static UtilityCard BuildEditor(RichTextBox editor, string title, string hint, Button? action = null)
    {
        var card = new UtilityCard { Dock = DockStyle.Fill };
        var heading = new Panel { Dock = DockStyle.Top, Height = 29 };
        var label = UtilityTheme.Label(title);
        label.Font = UtilityTheme.StrongFont;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        heading.Controls.Add(label);
        if (action != null)
        {
            action.Dock = DockStyle.Right;
            action.Width = 88;
            heading.Controls.Add(action);
        }
        var subtitle = UtilityTheme.Label(hint, muted: true);
        subtitle.Dock = DockStyle.Top;
        subtitle.AutoSize = false;
        subtitle.Height = 27;
        subtitle.Padding = new Padding(0, 3, 0, 0);
        var input = new Panel { Dock = DockStyle.Fill, Padding = new Padding(9), BackColor = UtilityTheme.Input };
        editor.Multiline = true;
        editor.WordWrap = false;
        editor.ScrollBars = RichTextBoxScrollBars.Both;
        editor.BorderStyle = BorderStyle.None;
        editor.Font = UtilityTheme.MonoFont;
        editor.BackColor = UtilityTheme.Input;
        editor.ForeColor = UtilityTheme.Text;
        editor.Dock = DockStyle.Fill;
        editor.AccessibleName = title;
        editor.AccessibleDescription = hint;
        input.Controls.Add(editor);
        card.Controls.Add(input);
        card.Controls.Add(subtitle);
        card.Controls.Add(heading);
        return card;
    }

    private void BuildActivityPanel()
    {
        var card = new UtilityCard { Dock = DockStyle.Fill, Padding = new Padding(14, 9, 14, 10) };
        var heading = UtilityTheme.Label("ACTIVITY", muted: true);
        heading.Dock = DockStyle.Top;
        heading.AutoSize = false;
        heading.Height = 25;
        heading.Font = UtilityTheme.StrongFont;
        txtLog.ReadOnly = true;
        txtLog.BackColor = UtilityTheme.Surface;
        txtLog.ForeColor = UtilityTheme.Muted;
        txtLog.Font = UtilityTheme.MonoFont;
        txtLog.Dock = DockStyle.Fill;
        txtLog.BorderStyle = BorderStyle.None;
        txtLog.WordWrap = false;
        txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
        txtLog.AccessibleName = "Activity log";
        var empty = UtilityTheme.Label("Activity will appear here when you save settings or enable Eco Mode.", muted: true);
        empty.Dock = DockStyle.Fill;
        empty.AutoSize = false;
        txtLog.TextChanged += (_, _) => empty.Visible = txtLog.TextLength == 0;
        empty.Click += (_, _) => txtLog.Focus();
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(txtLog);
        body.Controls.Add(empty);
        card.Controls.Add(body);
        card.Controls.Add(heading);
        splitMain.Panel2.Controls.Add(card);
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
        TrySetMinSizes(splitMain, 380, 94);
        TrySetMinSizes(splitTop, 440, 330);
        TrySetMinSizes(splitRight, 150, 150);
        SafeSetSplitter(splitMain, splitMain.Height - 102);
        SafeSetSplitter(splitTop, (int)(splitTop.Width * 0.49));
        SafeSetSplitter(splitRight, (int)(splitRight.Height * 0.50));
    }
}
