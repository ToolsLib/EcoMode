namespace ecomode;

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Reflection;
using ecomode;
using ecomode.Interop;

public partial class MainForm : Form
{
    private readonly bool _isAdmin;
    private readonly AppState _state;

    private bool _ecoHookActive = false;

    public MainForm(bool isAdmin)
    {
        try
        {
            _isAdmin = isAdmin;
            _InitializeComponent();

            // this.Icon = new Icon("app_simplified.ico");
            var icon = LoadEmbeddedIcon("ecomode.Assets.app_simplified.ico");
            if (icon != null)
            {
                this.Icon = icon;
                trayIcon.Icon = icon;
            }

            EnergyManager.Logger = Log;

            _state = AppState.Load();
            EnergyManager.SetBypass(_state.BypassProcesses); // Set bypass processes after loading state

            ApplyStateToUi();
            // UpdateAdminBanner(_ecoHookActive);
            UpdateStatus(_ecoHookActive); // starts OFF visually
        }
        catch (Exception ex)
        {
            MessageBox.Show($"InitializeComponent/MainForm error:\r\n\r\n{ex}", "EcoModeGUI — Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            throw; // let Program.cs handler log it too
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // If user clicked the X (or Alt+F4), minimize to tray instead of exiting
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && Visible)
            HideToTray();
    }

    // Ensure hook gets cleaned up on exit
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_ecoHookActive)
        {
            try { HookManager.UnsubscribeWindowEvents(); } catch { }
            _ecoHookActive = false;
        }
        base.OnFormClosed(e);
    }

    // private void UpdateAdminBanner(bool eco)
    // {
    //     lblAdmin.Text = _isAdmin
    //         ? "Running as Administrator: full features enabled."
    //         : "Not elevated: system power plan + HKLM policies will be skipped.";
    // }
    private void UpdateStatus(bool ecoPlannedOn)
    {
        var adminText = _isAdmin
            ? "Running as Administrator: full features enabled."
            : "Not elevated: system power plan + HKLM policies will be skipped.";

        lblStatus.Text = $"Eco Mode: {(ecoPlannedOn ? "ON" : "OFF")} — {adminText}";
        lblStatus.ForeColor = ecoPlannedOn ? Color.Green : Color.DarkRed;
    }

    #region UI wiring
    private Button btnEcoOn = new();
    private Button btnEcoOff = new();
    // private Label lblEcoState = new();
    private CheckBox chkPowerPlan = new();
    private CheckBox chkEdge = new();
    private CheckBox chkChrome = new();
    private CheckBox chkGpuPref = new();
    private CheckBox chkEcoQos = new();
    private Button btnBrowseGpu = new();

    private TextBox txtGpuApps = new();
    private TextBox txtEcoQosProcs = new();
    // private Label lblAdmin = new();
    private Label lblStatus = new();
    private Button btnSave = new();
    private Button btnDonate = new();
    private RichTextBox txtLog = new();

    // Menu
    private MenuStrip menu = new();
    private ToolStripMenuItem fileMenu = new("&File");
    private ToolStripMenuItem editMenu = new("&Edit");
    private ToolStripMenuItem miSave = new("&Save");
    private ToolStripMenuItem miQuit = new("&Quit");
    private ToolStripMenuItem miImport = new("&Import Profile…");
    private ToolStripMenuItem miExport = new("&Export Profile…");
    private ToolStripMenuItem miReset = new("&Reset to Defaults");

    // Tray
    private NotifyIcon trayIcon = new();
    private ContextMenuStrip trayMenu = new();
    private ToolStripMenuItem miTrayShow = new("Show");
    private ToolStripMenuItem miTraySave = new("Save");
    private ToolStripMenuItem miTrayQuit = new("Quit");
    private bool _reallyExit = false; // set true only when user chooses Quit

    private SplitContainer splitMain;
    private SplitContainer splitTop;
    private SplitContainer splitRight;

    private void _InitializeComponent()
    {
        Text = "EcoMode GUI";
        Width = 1200;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(1100, 700);

        // ===== Root panel with padding =====
        var rootPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10) // <-- adjust to taste (all sides)
        };
        Controls.Add(rootPanel);

        // ===== Menu =====
        miSave.ShortcutKeys = Keys.Control | Keys.S;
        miSave.Click += OnMenuSave;

        miQuit.ShortcutKeys = Keys.Control | Keys.Q;
        miQuit.Click += OnMenuQuit;

        fileMenu.DropDownItems.AddRange(new ToolStripItem[] { miSave, new ToolStripSeparator(), miQuit });

        // Edit menu
        miImport.Click += OnMenuImportProfile;
        miExport.Click += OnMenuExportProfile;
        miReset.Click  += OnMenuResetDefaults;

        editMenu.DropDownItems.Clear();
        editMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            miImport,
            miExport,
            new ToolStripSeparator(),
            miReset
        });
        editMenu.Enabled = true; // now active

        menu.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu });
        menu.Dock = DockStyle.Top;

        MainMenuStrip = menu;
        Controls.Add(menu);

        // ===== System tray =====
        trayMenu.Items.AddRange(new ToolStripItem[]
        {
            miTrayShow, new ToolStripSeparator(), miTraySave, new ToolStripSeparator(), miTrayQuit
        });
        miTrayShow.Click += (_, __) => RestoreFromTray();
        miTraySave.Click += (_, __) => SaveFromUi();
        miTrayQuit.Click += OnMenuQuit;

        // trayIcon.Icon = SystemIcons.Application; // replace with your own .ico if you have one
        // trayIcon.Icon = new Icon("app_simplified.ico");
        trayIcon.Text = "EcoMode";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, __) => RestoreFromTray();

        // ===== Main vertical split: Top (controls) / Bottom (log) =====
        splitMain = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        rootPanel.Controls.Add(splitMain);


        // ===== Top horizontal split: Left (buttons+checkboxes) / Right (text areas) =====
        splitTop = new SplitContainer
        {
            Orientation = Orientation.Vertical,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        splitMain.Panel1.Controls.Add(splitTop);

        // ---------- Left Panel ----------
        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        splitTop.Panel1.Controls.Add(leftPanel);

        lblStatus.AutoSize = false;
        lblStatus.Height = 20; // instead of 36
        lblStatus.Dock = DockStyle.Top;
        lblStatus.Padding = new Padding(0, 2, 0, 0); // small breathing room
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.ForeColor = Color.DarkGreen;
        lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        lblStatus.Text = "Eco Mode: OFF — Not elevated: system power plan + HKLM policies will be skipped.";
        leftPanel.Controls.Add(lblStatus);

        btnEcoOn.Text = "🔋 Eco ON";
        btnEcoOn.SetBounds(0, 36, 140, 40);
        btnEcoOn.Click += (_, __) => ToggleEco(true);

        btnEcoOff.Text = "Eco OFF";
        btnEcoOff.SetBounds(150, 36, 140, 40);
        btnEcoOff.Click += (_, __) => ToggleEco(false);

        btnSave.Text = "Save Settings";
        btnSave.SetBounds(300, 36, 160, 40);
        btnSave.Click += (_, __) => SaveFromUi();

        btnDonate.Text = "Donate ❤️";
        btnDonate.SetBounds(470, 36, 140, 40);
        btnDonate.Click += (_, __) => OpenDonatePage();

        leftPanel.Controls.Add(btnDonate);

        // Checkboxes
        chkPowerPlan.Text = "Windows power plan (EPP/Boost via powercfg)";
        chkPowerPlan.SetBounds(0, 88, 520, 26);

        chkEdge.Text = "Edge policy (Hardware Accel OFF / Efficiency Mode ON)";
        chkEdge.SetBounds(0, 120, 540, 26);

        chkChrome.Text = "Chrome policy (Hardware Accel OFF / Battery Saver)";
        chkChrome.SetBounds(0, 152, 520, 26);

        chkGpuPref.Text = "Per-app GPU preference (HKCU)";
        chkGpuPref.SetBounds(0, 184, 320, 26);

        chkEcoQos.Text = "EcoQoS for running processes";
        chkEcoQos.SetBounds(0, 216, 320, 26);

        leftPanel.Controls.AddRange(new Control[]
        {
            btnEcoOn, btnEcoOff, btnSave,
            chkPowerPlan, chkEdge, chkChrome, chkGpuPref, chkEcoQos
        });

        // ---------- Right Panel (two stacked text areas) ----------
        splitRight = new SplitContainer
        {
            Orientation = Orientation.Horizontal,
            Dock = DockStyle.Fill
            // SplitterDistance will be set in OnFormShown
        };
        splitTop.Panel2.Controls.Add(splitRight);

        // ==========================================================
        // GPU Pref Apps (top)
        var panelGpu = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // header row: label (left) + Browse... (right)
        var headerGpu = new Panel { Dock = DockStyle.Top, Height = 28 };
        var lblGpu = new Label
        {
            Text = "GPU Pref Apps (EXE paths, one per line):",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };
        btnBrowseGpu.Text = "Browse…";
        btnBrowseGpu.AutoSize = true;
        btnBrowseGpu.Dock = DockStyle.Right;
        btnBrowseGpu.Margin = new Padding(6, 2, 0, 2);
        btnBrowseGpu.Click += (_, __) => BrowseAndAddGpuApps();

        headerGpu.Controls.Add(lblGpu);
        headerGpu.Controls.Add(btnBrowseGpu);

        // textbox
        txtGpuApps.Multiline = true;
        txtGpuApps.ScrollBars = ScrollBars.Vertical;
        txtGpuApps.Dock = DockStyle.Fill;

        // assemble
        panelGpu.Controls.Add(txtGpuApps);
        panelGpu.Controls.Add(headerGpu);
        splitRight.Panel1.Controls.Add(panelGpu);
        // ==========================================================

        // EcoQoS Processes (bottom)
        var panelQos = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var lblQos = new Label { Text = "EcoQoS Bypass Processes (names, one per line):", Dock = DockStyle.Top, Height = 20 };
        txtEcoQosProcs.Multiline = true;
        txtEcoQosProcs.ScrollBars = ScrollBars.Vertical;
        txtEcoQosProcs.Dock = DockStyle.Fill;
        panelQos.Controls.Add(txtEcoQosProcs);
        panelQos.Controls.Add(lblQos);
        splitRight.Panel2.Controls.Add(panelQos);

        // ===== Bottom log =====
        txtLog.ReadOnly = true;
        txtLog.BackColor = Color.Black;
        txtLog.ForeColor = Color.LightGreen;
        txtLog.Font = new Font("Consolas", 9.75f);
        txtLog.Dock = DockStyle.Fill;
        txtLog.BorderStyle = BorderStyle.FixedSingle;
        txtLog.WordWrap = false;
        txtLog.ScrollBars = RichTextBoxScrollBars.Vertical;
        splitMain.Panel2.Controls.Add(txtLog);

        // Defer splitter setup until form is actually shown
        this.Shown += OnFormShown;
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
        // Now the SplitContainers have real sizes. Set min sizes first, then distances.
        TrySetMinSizes(splitMain, 240, 120);
        TrySetMinSizes(splitTop, 360, 280);
        TrySetMinSizes(splitRight, 140, 140);

        SafeSetSplitter(splitMain, (int)(splitMain.Height * 0.65));  // ~65% to top controls
        SafeSetSplitter(splitTop, (int)(splitTop.Width * 0.55));   // ~55% to left panel
        SafeSetSplitter(splitRight, (int)(splitRight.Height * 0.50)); // half top/bottom
    }

    private static void TrySetMinSizes(SplitContainer sc, int min1, int min2)
    {
        // Guard: if not sized yet, skip quietly
        int length = sc.Orientation == Orientation.Horizontal ? sc.Height : sc.Width;
        if (length <= 0) return;

        // Make sure the combined mins fit; if not, shrink proportionally
        int maxTotal = Math.Max(0, length - sc.SplitterWidth - 1); // leave 1px headroom
        int totalMin = Math.Max(0, min1) + Math.Max(0, min2);

        if (totalMin > maxTotal && totalMin > 0)
        {
            // Scale them down while preserving the ratio
            double scale = (double)maxTotal / totalMin;
            min1 = (int)Math.Floor(min1 * scale);
            min2 = Math.Max(0, maxTotal - min1);
        }

        // Apply safely
        try { sc.Panel1MinSize = Math.Max(0, min1); } catch { /* ignore */ }
        try { sc.Panel2MinSize = Math.Max(0, min2); } catch { /* ignore */ }
    }

    private static void SafeSetSplitter(SplitContainer sc, int proposed)
    {
        int length = sc.Orientation == Orientation.Horizontal ? sc.Height : sc.Width;
        if (length <= 0) return;

        int min = Math.Max(0, sc.Panel1MinSize);
        int max = Math.Max(0, length - sc.Panel2MinSize - sc.SplitterWidth);

        if (min > max)
        {
            // If mins still don't fit, collapse to a workable point
            min = Math.Max(0, Math.Min(min, length - sc.SplitterWidth));
            max = min;
        }

        int clamped = Math.Max(min, Math.Min(proposed, max));

        try { sc.SplitterDistance = clamped; } catch { /* ignore if container is mid-layout */ }
    }

    private void BrowseAndAddGpuApps()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select application(s) to set GPU preference",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        // Try to start in Program Files (64-bit first, then 32-bit)
        try
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf) && Directory.Exists(pf)) ofd.InitialDirectory = pf;
            else
            {
                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(pf86) && Directory.Exists(pf86)) ofd.InitialDirectory = pf86;
            }
        }
        catch { /* ignore */ }

        if (ofd.ShowDialog(this) == DialogResult.OK && ofd.FileNames?.Length > 0)
        {
            AppendGpuApps(ofd.FileNames);
        }
    }

    private void AppendGpuApps(IEnumerable<string> paths)
    {
        // existing lines
        var lines = txtGpuApps.Lines?.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim())
                    ?? Enumerable.Empty<string>();

        // normalize to full paths and dedup (case-insensitive)
        var current = new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in paths)
        {
            try
            {
                var p = raw.Trim().Trim('"');
                // Make absolute if possible
                if (!Path.IsPathRooted(p))
                    p = Path.GetFullPath(p);

                if (File.Exists(p))
                    current.Add(p);
                else
                    Log($"Browse: skipped missing file: {p}");
            }
            catch (Exception ex)
            {
                Log($"Browse: error adding path -> {raw} ({ex.Message})");
            }
        }

        // Write back (sorted for consistency)
        txtGpuApps.Lines = current.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void ApplyStateToUi()
    {
        chkPowerPlan.Checked = _state.IncludePowerPlan;
        chkEdge.Checked = _state.IncludeEdge;
        chkChrome.Checked = _state.IncludeChrome;
        chkGpuPref.Checked = _state.IncludeGpuPref;
        chkEcoQos.Checked = _state.IncludeEcoQos;
        txtGpuApps.Text = string.Join(Environment.NewLine, _state.GpuPrefApps);

        // txtEcoQosProcs.Text = string.Join(Environment.NewLine, _state.EcoQosProcesses);
        txtEcoQosProcs.Text = string.Join(Environment.NewLine, _state.BypassProcesses);
    }

    private void SaveFromUi()
    {
        _state.IncludePowerPlan = chkPowerPlan.Checked;
        _state.IncludeEdge = chkEdge.Checked;
        _state.IncludeChrome = chkChrome.Checked;
        _state.IncludeGpuPref = chkGpuPref.Checked;
        _state.IncludeEcoQos = chkEcoQos.Checked;

        _state.GpuPrefApps = txtGpuApps.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();
        // _state.EcoQosProcesses = txtEcoQosProcs.Lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();
        // save bypass list from the same textbox
        _state.BypassProcesses = txtEcoQosProcs.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();

        _state.Save();

        // push the new bypass to EnergyManager immediately
        EnergyManager.SetBypass(_state.BypassProcesses);

        Log("Settings saved.");
    }
    #endregion

    private void ToggleEco(bool eco)
    {
        Log($"=== {(eco ? "ECO ON" : "ECO OFF")} ===");

        // lblEcoState.Text = eco ? "Eco Mode: ON" : "Eco Mode: OFF";
        // lblEcoState.ForeColor = eco ? Color.Green : Color.DarkRed;

        // Show planned state immediately (doesn't depend on _ecoHookActive)
        UpdateStatus(eco);

        if (_state.IncludePowerPlan)
        {
            if (_isAdmin) ApplyPowerPlan(eco);
            else Log("Skipped power plan (needs elevation).");
        }

        if (_state.IncludeEdge)
        {
            if (_isAdmin) ApplyEdgePolicy(eco);
            else Log("Skipped Edge policy (needs elevation).");
        }

        if (_state.IncludeChrome)
        {
            if (_isAdmin) ApplyChromePolicy(eco);
            else Log("Skipped Chrome policy (needs elevation).");
        }

        if (_state.IncludeGpuPref) ApplyGpuPreferences(eco);

        if (_state.IncludeEcoQos)
        {
            if (eco)
            {
                // Start foreground hook and throttle background immediately
                if (!_ecoHookActive)
                {
                    HookManager.SubscribeToWindowEvents();
                    _ecoHookActive = true;
                }
                EnergyManager.ThrottleAllUserBackgroundProcesses();
                Log("EcoQoS: throttled background processes; foreground will auto-boost.");
            }
            else
            {
                // Stop hook and unthrottle everything
                if (_ecoHookActive)
                {
                    HookManager.UnsubscribeWindowEvents();
                    _ecoHookActive = false;
                }
                EnergyManager.UnthrottleAllUserProcesses();
                Log("EcoQoS: restored normal priority to user processes.");
            }
        }

        // Optionally, refresh with the *actual* final state if you want to be extra-precise:
        UpdateStatus(_ecoHookActive);

        Log("Done. Restart browsers to apply policy changes.");
    }

    #region Actions

    private void ApplyPowerPlan(bool eco)
    {
        // EPP + Boost (AC/DC); adapt values as desired
        // Eco: EPP high (85/90), Boost off (0)
        // Perf: EPP low (10/25), Boost aggressive/default (2/1)
        var cmds = eco
            ? new[]
            {
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 85",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 90",
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 0",
                "powercfg -setactive SCHEME_CURRENT"
            }
            : new[]
            {
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 10",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFEPP 25",
                "powercfg -setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 2",
                "powercfg -setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE 1",
                "powercfg -setactive SCHEME_CURRENT"
            };

        foreach (var c in cmds) RunCmd(c);
    }

    private void ApplyEdgePolicy(bool eco)
    {
        // HKLM\SOFTWARE\Policies\Microsoft\Edge
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge", true);
        if (key is null) { Log("Edge policy key unavailable."); return; }

        key.SetValue("HardwareAccelerationModeEnabled", eco ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("EfficiencyModeEnabled", eco ? 1 : 0, RegistryValueKind.DWord);
        Log("Edge policy updated.");
    }

    private void ApplyChromePolicy(bool eco)
    {
        // HKLM\SOFTWARE\Policies\Google\Chrome
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome", true);
        if (key is null) { Log("Chrome policy key unavailable."); return; }

        key.SetValue("HardwareAccelerationModeEnabled", eco ? 0 : 1, RegistryValueKind.DWord);
        key.SetValue("BatterySaverModeAvailability", eco ? 1 : 0, RegistryValueKind.DWord);
        Log("Chrome policy updated.");
    }

    private void ApplyGpuPreferences(bool eco)
    {
        // HKCU\Software\Microsoft\DirectX\UserGpuPreferences
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
        if (key is null) { Log("GPU preference key unavailable."); return; }

        foreach (var exe in _state.GpuPrefApps)
        {
            try
            {
                var path = exe.Trim('"');
                if (!File.Exists(path))
                {
                    Log($"GPU Pref: file not found: {path}");
                    continue;
                }
                var val = eco ? "GpuPreference=1;" : "GpuPreference=2;"; // 1=power-saving, 2=high-perf
                key.SetValue(path, val, RegistryValueKind.String);
                Log($"GPU Pref set: {(eco ? "power-saving" : "high-perf")} -> {path}");
            }
            catch (Exception ex)
            {
                Log($"GPU Pref error for {exe}: {ex.Message}");
            }
        }
    }

    #endregion

    #region Helpers

    private static Icon? LoadEmbeddedIcon(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        // e.g. "ecomode.Assets.app_simplified.ico"
        using var s = asm.GetManifestResourceStream(resourceName);
        return s != null ? new Icon(s) : null;
    }

    private void OnMenuResetDefaults(object? sender, EventArgs e)
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to reset EcoMode settings to defaults?\r\n" +
            "This will overwrite your current profile.",
            "Reset to Defaults",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
            return;

        try
        {
            // Replace current state with brand new defaults
            var fresh = new AppState();

            CopyState(fresh, _state);  // reuse helper from import
            _state.Save();

            ApplyStateToUi();
            EnergyManager.SetBypass(_state.BypassProcesses);

            LogInfo("Settings reset to defaults.");
        }
        catch (Exception ex)
        {
            LogError($"Reset failed: {ex.Message}");
        }
    }

    private void OnMenuExportProfile(object? sender, EventArgs e)
    {
        try
        {
            // Ensure we capture current edits from UI
            SaveFromUi();

            using var sfd = new SaveFileDialog
            {
                Title = "Export EcoMode Profile",
                Filter = "EcoMode profile (*.json)|*.json|All files (*.*)|*.*",
                FileName = "EcoModeProfile.json",
                OverwritePrompt = true,
                AddExtension = true
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                LogInfo($"Profile exported to: {sfd.FileName}");
            }
        }
        catch (Exception ex)
        {
            LogError($"Export failed: {ex.Message}");
        }
    }

    private void OnMenuImportProfile(object? sender, EventArgs e)
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = "Import EcoMode Profile",
                Filter = "EcoMode profile (*.json)|*.json|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                var json = File.ReadAllText(ofd.FileName);
                var imported = JsonSerializer.Deserialize<AppState>(json);

                if (imported is null)
                {
                    LogWarn("Import aborted: file did not contain a valid profile.");
                    return;
                }

                // Replace current state with imported settings
                // (If you prefer merge, we can do field-by-field.)
                CopyState(imported, _state);

                // Persist to disk
                _state.Save();

                // Re-apply to UI and runtime
                ApplyStateToUi();
                EnergyManager.SetBypass(_state.BypassProcesses);

                LogInfo($"Profile imported from: {ofd.FileName}");
            }
        }
        catch (JsonException jex)
        {
            LogError($"Import failed: invalid JSON ({jex.Message})");
        }
        catch (Exception ex)
        {
            LogError($"Import failed: {ex.Message}");
        }
    }

    // Helper to copy fields from src -> dst (keeps reference to existing _state)
    private static void CopyState(AppState src, AppState dst)
    {
        dst.IncludePowerPlan = src.IncludePowerPlan;
        dst.IncludeEdge      = src.IncludeEdge;
        dst.IncludeChrome    = src.IncludeChrome;
        dst.IncludeGpuPref   = src.IncludeGpuPref;
        dst.IncludeEcoQos    = src.IncludeEcoQos;

        dst.GpuPrefApps      = src.GpuPrefApps ?? Array.Empty<string>();
        dst.BypassProcesses  = src.BypassProcesses ?? Array.Empty<string>();

        // legacy field intentionally ignored on write; AppState.Save() nulls it anyway
    }

    private void HideToTray()
    {
        if (!Visible) return;
        Hide();
        ShowTrayTip("EcoMode is still running here.");
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ShowTrayTip(string text)
    {
        try
        {
            trayIcon.BalloonTipTitle = "EcoMode";
            trayIcon.BalloonTipText = text;
            trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            trayIcon.ShowBalloonTip(2000);
        }
        catch { /* ignore if OS blocks tips */ }
    }

    private void OnMenuSave(object? sender, EventArgs e)
    {
        SaveFromUi();
    }

    private void OnMenuQuit(object? sender, EventArgs e)
    {
        // Close();
        _reallyExit = true;
        try { trayIcon.Visible = false; } catch { }
        Close();
    }

    private void OpenDonatePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://urls.up2sha.re/ecomodedonate",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogError($"Failed to open donate link: {ex.Message}");
        }
    }

    private void RunCmd(string cmdLine)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c " + cmdLine)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Log($"> {cmdLine}");
            if (!string.IsNullOrWhiteSpace(stdout)) Log(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr)) Log(stderr.Trim());
        }
        catch (Exception ex)
        {
            Log($"> {cmdLine} -> ERROR: {ex.Message}");
        }
    }

    private void Log(string msg, Color? color = null)
    {
        // txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionLength = 0;

        txtLog.SelectionColor = color ?? Color.LightGreen;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionColor = txtLog.ForeColor;
    }
    
    private void LogInfo(string msg)  => Log(msg, Color.LightGreen);
    private void LogWarn(string msg)  => Log(msg, Color.Yellow);
    private void LogError(string msg) => Log(msg, Color.Red);

    #endregion
}

public class AppState
{
    public bool IncludePowerPlan { get; set; } = true;
    public bool IncludeEdge { get; set; } = true;
    public bool IncludeChrome { get; set; } = true;
    public bool IncludeGpuPref { get; set; } = true;
    public bool IncludeEcoQos { get; set; } = true;

    public string[] GpuPrefApps { get; set; } = new[]
    {
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    };

    // NEW: configurable bypass list (process names, with or without .exe)
    public string[] BypassProcesses { get; set; } = Array.Empty<string>();

    // OLD (for backward-compat with existing settings.json). Not used anymore.
    public string[]? EcoQosProcesses { get; set; }
    // public string[] EcoQosProcesses { get; set; } = new[] { "chrome", "msedge", "Code", "Discord" };

    public static string ConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "settings.json");

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        // Stop writing the legacy field
        EcoQosProcesses = null;

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AppState Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var s = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppState>(s) ?? new AppState();
            }
        }
        catch { /* ignore and fallback */ }
        return new AppState();
    }
}