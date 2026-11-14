using ecomode;

internal static partial class Program
{
    private static void RunTrayStatusTests()
    {
        Run("tray status follows confirmed manual mode, failed restoration and retry", TrayManualStatus);
        Run("failed Eco activation never shows the active tray badge", TrayFailedActivation);
        Run("hidden sign-in automation updates the tray without changing the window icon", TrayAutomaticStatus);
        Run("tray recovery and state tooltips follow all four interface languages", TrayRecoveryLanguages);
        Run("embedded tray badges have distinct transparent artwork at every tray size", TrayIconResources);
    }

    private static void CheckTrayStatus(MainForm form, EcoSwitchState expected)
    {
        var tray = Field<NotifyIcon>(form, "trayIcon");
        var icons = Field<TrayStatusIcons>(form, "_trayStatusIcons");
        string caption = expected switch
        {
            EcoSwitchState.On => "Eco mode is on",
            EcoSwitchState.RestorePending => "Restore pending",
            _ => "Eco mode is off"
        };
        Check(ReferenceEquals(tray.Icon, icons[expected]) && tray.Text == "EcoMode — " + L.T(caption),
            "Tray icon or translated tooltip disagrees with the confirmed Eco state.");
        Check(tray.Text.Length <= 63, "Tray tooltip exceeds the legacy notification-area limit.");
    }

    private static void TrayManualStatus()
    {
        var (system, manager, _) = Setup(Target("setting"));
        using var form = new TestMainForm(false, manager, SwitchConfig());
        var windowIcon = form.Icon;
        CheckTrayStatus(form, EcoSwitchState.Off);
        var off = Field<NotifyIcon>(form, "trayIcon").Icon;
        ClickEcoSwitch(form);
        CheckTrayStatus(form, EcoSwitchState.On);
        var on = Field<NotifyIcon>(form, "trayIcon").Icon;
        Check(!ReferenceEquals(off, on), "Turning Eco Mode on kept the inactive icon.");
        system.FailWrite = "setting";
        ClickEcoSwitch(form);
        CheckTrayStatus(form, EcoSwitchState.RestorePending);
        Check(manager.HasPendingChanges, "Failed restoration was incorrectly reported as clean.");
        system.FailWrite = null;
        ClickEcoSwitch(form);
        CheckTrayStatus(form, EcoSwitchState.Off);
        Check(ReferenceEquals(off, Field<NotifyIcon>(form, "trayIcon").Icon), "Returning to Off allocated another icon.");
        ClickEcoSwitch(form);
        Check(ReferenceEquals(on, Field<NotifyIcon>(form, "trayIcon").Icon), "Returning to On allocated another icon.");
        ClickEcoSwitch(form);
        Check(ReferenceEquals(form.Icon, windowIcon), "Tray changes replaced the window/taskbar icon.");
    }

    private static void TrayFailedActivation()
    {
        var (system, manager, _) = Setup(Target("setting"));
        system.FailRead = "setting";
        using var form = new TestMainForm(false, manager, SwitchConfig());
        ClickEcoSwitch(form);
        CheckTrayStatus(form, EcoSwitchState.Off);
        Check(!Field<bool>(form, "_ecoEnabled") && system.Writes == 0, "Failed capture enabled Eco Mode.");
        system.FailRead = null;
        ClickEcoSwitch(form);
        CheckTrayStatus(form, EcoSwitchState.On);
        ClickEcoSwitch(form);
    }

    private static void TrayAutomaticStatus()
    {
        var (_, manager, _) = Setup(Target("setting"));
        var power = new FakePowerSource();
        string preferences = Path.Combine(_root, "tray-automatic.json");
        new AutomationPreferences(preferences).Save(true);
        using var form = new TestMainForm(false, manager, SwitchConfig(), powerSource: power,
            automationPreferencesPath: preferences, startMonitoring: true, startInTray: true)
        { FakeMonitoring = true };
        var windowIcon = form.Icon;
        CheckTrayStatus(form, EcoSwitchState.Off);
        form.Show();
        Application.DoEvents();
        CheckTrayStatus(form, EcoSwitchState.On);
        Check(!form.Visible && Field<bool>(form, "_automaticProcessMode"), "Hidden automation did not activate or exposed a window.");
        power.Snapshot = TestBattery with { Connection = PowerConnection.External };
        form.PollPowerAndProcesses();
        CheckTrayStatus(form, EcoSwitchState.Off);
        Check(!form.Visible && ReferenceEquals(windowIcon, form.Icon), "Power-source updates changed the window icon or showed the window.");
    }

    private static void TrayRecoveryLanguages()
    {
        try
        {
            var (system, manager, _) = Setup(Target("setting"));
            manager.Capture(Profile, false);
            manager.ApplyEco(Profile, false);
            using var form = new TestMainForm(false, manager, SwitchConfig());
            CheckTrayStatus(form, EcoSwitchState.RestorePending);
            foreach (var language in L.Languages)
            {
                form.SetLanguage(language.Code);
                CheckTrayStatus(form, EcoSwitchState.RestorePending);
                ClickEcoSwitch(form);
                CheckTrayStatus(form, EcoSwitchState.Off);
                ClickEcoSwitch(form);
                CheckTrayStatus(form, EcoSwitchState.On);
                system.FailWrite = "setting";
                ClickEcoSwitch(form);
                CheckTrayStatus(form, EcoSwitchState.RestorePending);
                system.FailWrite = null;
            }
            ClickEcoSwitch(form);
            using var unreadable = new TestMainForm(false, new InaccessibleRecovery(), SwitchConfig());
            CheckTrayStatus(unreadable, EcoSwitchState.RestorePending);
        }
        finally { L.SetLanguage("en"); }
    }

    private static void TrayIconResources()
    {
        using var sheet = new Bitmap(560, 210);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(Color.FromArgb(17, 23, 26));
        graphics.FillRectangle(Brushes.WhiteSmoke, 0, 105, sheet.Width, 105);
        using var font = new Font("Segoe UI", 10);
        string[] resources = ["app_simplified.ico", "tray_eco_on.ico", "tray_restore_pending.ico"];
        string[] captions = ["Off", "On", "Restore pending"];
        foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64 })
        {
            var pixels = new List<int[]>();
            for (int state = 0; state < resources.Length; state++)
            {
                using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("ecomode.Assets." + resources[state])!;
                Check(stream != null, "A standalone tray resource is missing.");
                using var icon = new Icon(stream!, size, size);
                if (state > 0) Check(icon.Size == new Size(size, size), "Tray badge is missing a requested DPI size.");
                using var bitmap = new Bitmap(size, size);
                using (var canvas = Graphics.FromImage(bitmap)) canvas.DrawIcon(icon, new Rectangle(0, 0, size, size));
                if (state > 0) Check(bitmap.GetPixel(0, 0).A == 0, "Tray badge has an opaque background.");
                var colors = new List<int>();
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++) colors.Add(bitmap.GetPixel(x, y).ToArgb());
                pixels.Add(colors.ToArray());
                if (size is 16 or 24 or 32)
                {
                    int x = 18 + state * 185 + (size == 16 ? 0 : size == 24 ? 45 : 98);
                    graphics.DrawIcon(icon, new Rectangle(x, 42, size, size));
                    graphics.DrawIcon(icon, new Rectangle(x, 147, size, size));
                }
                if (size == 32)
                {
                    graphics.DrawString(captions[state], font, Brushes.WhiteSmoke, 18 + state * 185, 12);
                    graphics.DrawString(captions[state], font, Brushes.Black, 18 + state * 185, 117);
                }
            }
            Check(!pixels[0].SequenceEqual(pixels[1]) && !pixels[0].SequenceEqual(pixels[2]) && !pixels[1].SequenceEqual(pixels[2]),
                $"Tray states are visually identical at {size}px.");
        }
        sheet.Save(Path.Combine(AppContext.BaseDirectory, "tray-status-preview.png"));
    }
}
