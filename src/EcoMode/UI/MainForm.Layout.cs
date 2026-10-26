namespace ecomode;

public partial class MainForm
{
    private readonly Label lblStatusDetail = UtilityTheme.Label("Ready to save power.", muted: true);
    private readonly Label lblOriginalStatus = UtilityTheme.Label("SAVED ORIGINALS", muted: true);
    private readonly Label lblAppTitle = UtilityTheme.Label("EcoMode");
    private readonly Label lblAppSubtitle = UtilityTheme.Label("POWER CONTROL  /  Keep background work in check.", muted: true);
    private TableLayoutPanel? _powerControls;
    private bool _fittingControlPanel;
    private bool _fitControlPanelQueued;

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
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 10,
            BackColor = UtilityTheme.Background,
            FixedPanel = FixedPanel.Panel2
        };
        root.Controls.Add(splitMain);
        root.Controls.Add(header);
        root.BringToFront();

        splitTop = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = 14,
            BackColor = UtilityTheme.Background
        };
        splitMain.Panel1.Controls.Add(splitTop);
        BuildControlPanel();
        BuildApplicationRules();
        BuildBatteryDashboard();
        splitTop.Panel2.Controls.Add(_advancedBatteryHost);
        BuildActivityPanel();
        splitMain.SizeChanged += (_, _) => FitControlPanel();
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
        miSimpleView.Click += (_, _) => SetView(AppView.Simple);
        miAdvancedView.Click += (_, _) => SetView(AppView.Advanced);
        viewMenu.DropDownItems.AddRange([miSimpleView, miAdvancedView]);
        miLiveApps.Click += (_, _) => ShowLiveApps();
        viewMenu.DropDownItems.AddRange([new ToolStripSeparator(), miLiveApps]);
        fileMenu.DropDownItems.AddRange([miSave, new ToolStripSeparator(), miQuit]);
        editMenu.DropDownItems.AddRange([miImport, miExport, new ToolStripSeparator(), miReset]);
        menu.Items.AddRange([fileMenu, editMenu, viewMenu]);
        BuildLanguageMenu();
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
        BuildStartupMenus();
    }

    private Panel BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Name = "appHeader",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 0, 0, 12),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var brand = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        brand.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lblAppTitle.Font = UtilityTheme.TitleFont;
        lblAppTitle.Dock = DockStyle.Fill;
        lblAppTitle.Margin = new Padding(0, 0, 0, 3);
        lblAppSubtitle.Dock = DockStyle.Fill;
        brand.Controls.Add(lblAppTitle, 0, 0);
        brand.Controls.Add(lblAppSubtitle, 0, 1);
        var access = UtilityTheme.Label(_isAdmin ? "ADMINISTRATOR ACCESS" : "STANDARD ACCESS", muted: true);
        access.Font = UtilityTheme.StrongFont;
        access.TextAlign = ContentAlignment.MiddleRight;
        access.Anchor = AnchorStyles.Right;
        var scope = UtilityTheme.Label(_isAdmin ? "All settings available" : "System settings need Administrator", muted: true);
        scope.TextAlign = ContentAlignment.MiddleRight;
        scope.Anchor = AnchorStyles.Right;
        var accessPanel = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(16, 0, 16, 0)
        };
        accessPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        accessPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        accessPanel.Controls.Add(access, 0, 0);
        accessPanel.Controls.Add(scope, 0, 1);
        _accessPanel = accessPanel;
        cboView.Name = "viewPicker";
        cboView.DropDownStyle = ComboBoxStyle.DropDownList;
        cboView.Font = UtilityTheme.StrongFont;
        cboView.BackColor = UtilityTheme.Input;
        cboView.ForeColor = UtilityTheme.Text;
        cboView.AccessibleName = "View";
        cboView.Width = 160;
        cboView.Anchor = AnchorStyles.Right;
        cboView.Margin = new Padding(0, 0, 10, 0);
        cboView.Items.AddRange(new object[]
        {
            new ViewOption(AppView.Simple, "Standard view"),
            new ViewOption(AppView.Advanced, "Advanced view")
        });
        cboView.SelectedIndexChanged += (_, _) =>
        {
            if (!_applyingViewLayout && cboView.SelectedItem is ViewOption option) SetView(option.View);
        };
        UtilityTheme.StyleButton(btnDonate);
        btnDonate.Text = "Support";
        btnDonate.AutoSize = true;
        btnDonate.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        btnDonate.MinimumSize = new Size(82, 34);
        btnDonate.Padding = new Padding(10, 5, 10, 5);
        btnDonate.Anchor = AnchorStyles.Right;
        btnDonate.Margin = Padding.Empty;
        btnDonate.Click += (_, _) => OpenDonatePage();
        header.Controls.Add(brand, 0, 0);
        header.Controls.Add(accessPanel, 1, 0);
        header.Controls.Add(cboView, 2, 0);
        header.Controls.Add(btnDonate, 3, 0);
        return header;
    }

    private void BuildControlPanel()
    {
        var card = new UtilityCard { Dock = DockStyle.Fill, AutoScroll = true };
        splitTop.Panel1.Controls.Add(card);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1 };
        _powerControls = content;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(content);
        content.Layout += (_, _) =>
        {
            FitControlPanel();
            QueueFitControlPanel();
        };
        AddSection(content, BuildSimpleIntroduction());

        var mode = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        mode.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mode.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var status = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        status.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lblStatus.AutoSize = true;
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Font = UtilityTheme.StatusFont;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Margin = new Padding(0, 0, 0, 3);
        lblStatusDetail.AutoSize = true;
        lblStatusDetail.Dock = DockStyle.Fill;
        status.Controls.Add(lblStatus, 0, 0);
        status.Controls.Add(lblStatusDetail, 0, 1);
        ecoSwitch.Size = ecoSwitch.GetPreferredSize(Size.Empty);
        ecoSwitch.Anchor = AnchorStyles.Right;
        ecoSwitch.Margin = new Padding(12, 4, 0, 4);
        ecoSwitch.Click += OnEcoSwitchClick;
        mode.Controls.Add(status, 0, 0);
        mode.Controls.Add(ecoSwitch, 1, 0);
        AddSection(content, mode);
        AddDivider(content);
        AddSection(content, _standardBatteryHost);
        AddSection(content, BuildSimpleDetails());

        var presets = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty
        };
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        var presetHeading = UtilityTheme.Label("PRESET PROFILE", muted: true);
        presetHeading.Font = UtilityTheme.StrongFont;
        presetHeading.Margin = new Padding(0, 0, 0, 5);
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
        {
            lblPresetDescription.Text = (cboPresets.SelectedItem as ProfilePreset)?.Description ?? "";
            FitControlPanel();
        };
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
        AddAdvancedSection(content, presets);
        _advancedSections.Add(AddDivider(content));

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
        AddAdvancedSection(content, optionHeader);
        var options = new TableLayoutPanel
        {
            Name = "optionSettings",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var settings = new (CheckBox Box, string Text)[]
        {
            (chkPowerPlan, "Power plan · EPP and CPU boost"),
            (chkEdge, "Edge · efficiency + hardware acceleration off"),
            (chkChrome, "Chrome · battery saver + hardware acceleration off"),
            (chkGpuPref, "GPU preference · power-saving graphics"),
            (chkEcoQos, "Background processes · EcoQoS")
        };
        for (int row = 0; row < settings.Length; row++)
        {
            var (box, text) = settings[row];
            var label = UtilityTheme.Label(text);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = new Padding(0, 0, 12, 0);
            label.Cursor = Cursors.Hand;
            label.Click += (_, _) => { if (box.Enabled) box.Checked = !box.Checked; };
            box.AccessibleName = text;
            box.AccessibleDescription = "Include this option when Eco Mode is enabled.";
            box.Size = box.GetPreferredSize(Size.Empty);
            box.Anchor = AnchorStyles.Right;
            box.Margin = Padding.Empty;
            box.CheckedChanged += (_, _) => UpdateSimpleSummary();
            options.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            options.Controls.Add(label, 0, row);
            options.Controls.Add(box, 1, row);
        }
        AddAdvancedSection(content, options);
        _advancedSections.Add(AddDivider(content));

        var originals = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        originals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        originals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        lblOriginalStatus.Font = UtilityTheme.StrongFont;
        lblOriginalStatus.Margin = new Padding(0, 0, 0, 6);
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
        AddAdvancedSection(content, originals);
    }

    private static void AddSection(TableLayoutPanel layout, Control section)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(section, 0, layout.Controls.Count);
    }

    private static Control AddDivider(TableLayoutPanel layout)
    {
        var gap = new Panel { Dock = DockStyle.Top, Height = 9, Padding = new Padding(0, 4, 0, 4), Margin = Padding.Empty };
        gap.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UtilityTheme.Border });
        AddSection(layout, gap);
        return gap;
    }

    private void BuildApplicationRules()
    {
        splitRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 12,
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
        LayoutCurrentView();
        StartRuntimeMonitoring();
    }

    private void QueueFitControlPanel()
    {
        if (_fitControlPanelQueued || !Visible || !IsHandleCreated || IsDisposed || Disposing) return;
        _fitControlPanelQueued = true;
        BeginInvoke((Action)(() =>
        {
            try { if (!IsDisposed) FitControlPanel(); }
            finally { _fitControlPanelQueued = false; }
        }));
    }

    private void FitControlPanel()
    {
        if (_applyingViewLayout || _updatingLanguage || _fittingControlPanel || !Visible || _powerControls?.Parent is not Control card || splitMain.Height <= 0) return;
        _fittingControlPanel = true;
        try
        {
            int width = Math.Max(1, card.ClientSize.Width - card.Padding.Horizontal);
            // On small displays, keep the app editors usable below expanded
            // history and let the right-hand panel scroll instead of crushing them.
            splitTop.Panel2.AutoScroll = true;
            splitTop.Panel2.AutoScrollMinSize = _view == AppView.Advanced && _historyVisible
                ? new Size(0, _advancedBatteryHost.Height + splitRight.SplitterWidth + 340) : Size.Empty;
            int required = Math.Max(_powerControls.GetPreferredSize(new Size(width, 0)).Height,
                _powerControls.Height) + card.Padding.Vertical;
            int controlsShortfall = required - card.ClientSize.Height;
            if (_view == AppView.Advanced)
                required = Math.Max(required, _advancedBatteryHost.Height + splitRight.SplitterWidth + (_historyVisible ? 340 : 220));
            int activityHeight = splitMain.Panel2Collapsed ? 0 : splitMain.SplitterWidth + 76;
            int shortfall = Math.Max(required + activityHeight - splitMain.Height, controlsShortfall);
            // Longer translations can wrap. Grow the minimum height instead of
            // hiding restoration controls below a scrollbar at the window minimum.
            if (shortfall > 0 && WindowState == FormWindowState.Normal)
                MinimumSize = new Size(MinimumSize.Width, Math.Min(Screen.FromControl(this).WorkingArea.Height - 40,
                    Math.Max(MinimumSize.Height, Height + shortfall)));
            if (splitMain.Panel2Collapsed) return;
            if (splitMain.Panel1MinSize == required && splitMain.SplitterDistance >= required) return;
            TrySetMinSizes(splitMain, 0, 76);
            SafeSetSplitter(splitMain, Math.Max(required, splitMain.SplitterDistance));
            TrySetMinSizes(splitMain, required, 76);
            splitMain.PerformLayout();
            splitMain.Panel1.PerformLayout();
            splitTop.PerformLayout();
            splitTop.Panel1.PerformLayout();
            card.PerformLayout();
            if (_view == AppView.Advanced && _historyVisible)
            {
                TrySetMinSizes(splitRight, 160, 160);
                SafeSetSplitter(splitRight, splitRight.Height / 2);
            }
        }
        finally { _fittingControlPanel = false; }
    }
}
