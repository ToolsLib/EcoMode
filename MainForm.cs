namespace ecomode;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Reflection;
using ecomode.Interop;

public class MainForm : Form
{
    private readonly bool _isAdmin;
    private readonly AppState _state;
    private readonly IOriginalSettingsManager _originalSettings;
    private readonly string? _configPath;

    private bool _ecoHookActive = false;
    private bool _ecoEnabled;

    public MainForm(bool isAdmin) : this(isAdmin, null) { }

    internal MainForm(bool isAdmin, IOriginalSettingsManager? originalSettings, string? configPath = null)
    {
        try
        {
            _isAdmin = isAdmin;
            _configPath = configPath;
            BuildUi();
            var icon = LoadEmbeddedIcon("ecomode.Assets.app_simplified.ico");
            if (icon != null)
            {
                this.Icon = icon;
                trayIcon.Icon = icon;
            }

            EnergyManager.Logger = Log;

            _state = AppState.Load(_configPath);
            _originalSettings = originalSettings ?? new SettingsCoordinator(new WindowsSystemSettings(), message => Log(message));
            EnergyManager.SetBypass(_state.BypassProcesses); // Set bypass processes after loading state

            ApplyStateToUi();
            UpdateStatus(false);
            RefreshOriginalSettingsStatus();
        }
        catch
        {
            trayIcon.Dispose();
            trayMenu.Dispose();
            throw;
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
        if (!e.Cancel)
        {
            // Restore every change EcoMode applied, regardless of the current
            // checkboxes or app lists. Avoid modal prompts during Windows shutdown.
            if (!RestoreOriginalSettings(restoreAll: false) && e.CloseReason == CloseReason.UserClosing)
            {
                if (!_isAdmin && !EnergyManager.HasTrackedProcesses && !_ecoHookActive && ShowExitWarning(
                    "Some system settings need Administrator privileges to restore. Process throttling has been cleaned up.\r\n\r\n" +
                    "Choose OK to exit and keep the pending recovery record, then reopen EcoMode as Administrator and restore the settings. " +
                    "Choose Cancel to keep EcoMode open.", "Administrator Recovery Required", MessageBoxButtons.OKCancel) == DialogResult.OK)
                {
                    trayIcon.Visible = false;
                    return;
                }
                e.Cancel = true;
                _reallyExit = false;
                RestoreFromTray();
                ShowExitWarning("Some original settings could not be restored. EcoMode will stay open.\r\n\r\n" +
                    "Check the activity log and retry Quit. Restoring the power plan or browser policies may require Administrator privileges.",
                    "Restore Incomplete", MessageBoxButtons.OK);
                return;
            }
            trayIcon.Visible = false;
        }
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
        StopEcoQos(); // retry cleanup if a process was temporarily unavailable
        EnergyManager.Logger = null;
        trayIcon.Dispose();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            trayIcon.Dispose();
            trayMenu.Dispose();
        }
        base.Dispose(disposing);
    }

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
    private Button btnSaveOriginal = new();
    private Button btnRestoreOriginal = new();
    private CheckBox chkPowerPlan = new();
    private CheckBox chkEdge = new();
    private CheckBox chkChrome = new();
    private CheckBox chkGpuPref = new();
    private CheckBox chkEcoQos = new();
    private Button btnBrowseGpu = new();

    private TextBox txtGpuApps = new();
    private TextBox txtEcoQosProcs = new();
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

    private SplitContainer splitMain = null!;
    private SplitContainer splitTop = null!;
    private SplitContainer splitRight = null!;

    private void BuildUi()
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
        var leftPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
        splitTop.Panel1.Controls.Add(leftPanel);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false
        };

        lblStatus.AutoSize = false;
        lblStatus.Height = 44;
        lblStatus.Dock = DockStyle.Top;
        lblStatus.Padding = new Padding(0, 2, 0, 0); // small breathing room
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.ForeColor = Color.DarkGreen;
        lblStatus.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        lblStatus.Text = "Eco Mode: OFF — Not elevated: system power plan + HKLM policies will be skipped.";

        btnEcoOn.Text = "🔋 Eco ON";
        btnEcoOn.SetBounds(0, 36, 140, 40);
        btnEcoOn.Click += (_, __) => ToggleEco(true);

        btnEcoOff.Text = "Eco OFF";
        btnEcoOff.SetBounds(150, 36, 140, 40);
        btnEcoOff.Click += (_, __) => ToggleEco(false);

        btnSave.Text = "Save Profile Settings";
        btnSave.SetBounds(300, 36, 160, 40);
        btnSave.Click += (_, __) => SaveFromUi();

        btnDonate.Text = "Donate ❤️";
        btnDonate.SetBounds(470, 36, 140, 40);
        btnDonate.Click += (_, __) => OpenDonatePage();

        btnSaveOriginal.Text = "Save Original Settings";
        btnSaveOriginal.Size = new Size(210, 36);
        btnSaveOriginal.Click += (_, __) => SaveOriginalSettings();
        btnRestoreOriginal.Text = "Restore Original Settings";
        btnRestoreOriginal.Size = new Size(210, 36);
        btnRestoreOriginal.Click += (_, __) => RestoreOriginalSettings(restoreAll: true);

        actions.Controls.AddRange(new Control[] { btnEcoOn, btnEcoOff, btnSave, btnSaveOriginal, btnRestoreOriginal, btnDonate });
        actions.SetFlowBreak(btnSave, true);

        // Checkboxes
        chkPowerPlan.Text = "Windows power plan (EPP/Boost)";
        chkPowerPlan.SetBounds(0, 88, 520, 26);

        chkEdge.Text = "Edge policy (Hardware Accel OFF / Efficiency Mode ON)";
        chkEdge.SetBounds(0, 120, 540, 26);

        chkChrome.Text = "Chrome policy (Hardware Accel OFF / Battery Saver)";
        chkChrome.SetBounds(0, 152, 520, 26);

        chkGpuPref.Text = "Per-app GPU preference (HKCU)";
        chkGpuPref.SetBounds(0, 184, 320, 26);

        chkEcoQos.Text = "EcoQoS for running processes";
        chkEcoQos.SetBounds(0, 216, 320, 26);

        options.Controls.AddRange(new Control[] { chkPowerPlan, chkEdge, chkChrome, chkGpuPref, chkEcoQos });
        leftPanel.Controls.Add(options);
        leftPanel.Controls.Add(actions);
        leftPanel.Controls.Add(lblStatus);

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
        // save bypass list from the same textbox
        _state.BypassProcesses = txtEcoQosProcs.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToArray();

        _state.Save(_configPath);

        // push the new bypass to EnergyManager immediately
        EnergyManager.SetBypass(_state.BypassProcesses);

        Log("Settings saved.");
    }
    #endregion

    private void ToggleEco(bool eco)
    {
        if (!eco)
        {
            RestoreOriginalSettings(restoreAll: false);
            return;
        }

        try
        {
            SaveFromUi();
            if (!_originalSettings.HasSnapshot)
            {
                var answer = MessageBox.Show(this,
                    "Save the original settings before enabling Eco Mode?\r\n\r\n" +
                    "EcoMode will save the settings affected by the selected options so Eco OFF can restore them.\r\n" +
                    "New options and apps will be backed up before they are changed.\r\n\r\n" +
                    (_isAdmin ? "" : "Power plan and browser policies require Administrator privileges and will be skipped.\r\n\r\n") +
                    "Choose No to leave Eco Mode off.",
                    "Save Original Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                _originalSettings.Capture(_state, _isAdmin);
            }

            Log("=== ECO ON ===");
            bool applied = _originalSettings.ApplyEco(_state, _isAdmin);
            _ecoEnabled = true;
            if (!_isAdmin && (_state.IncludePowerPlan || _state.IncludeEdge || _state.IncludeChrome))
                LogWarn("Power plan and browser policies skipped: restart as Administrator to use them.");

            if (_state.IncludeEcoQos)
            {
                if (!_ecoHookActive)
                {
                    HookManager.SubscribeToWindowEvents();
                    _ecoHookActive = true;
                }
                EnergyManager.ThrottleAllUserBackgroundProcesses();
                Log("EcoQoS enabled. Foreground apps keep their original priority.");
            }
            else
            {
                StopEcoQos();
            }

            if (!applied) LogWarn("Some settings could not be applied. See the log; Eco OFF can restore any partial changes.");
            Log("Restart browsers to apply policy changes.");
        }
        catch (Exception ex)
        {
            LogError($"Eco ON failed: {ex.Message}");
        }
        finally
        {
            UpdateStatus(_ecoEnabled);
            RefreshOriginalSettingsStatus();
        }
    }

    private void SaveOriginalSettings()
    {
        try
        {
            if (_ecoEnabled || EnergyManager.HasTrackedProcesses || _originalSettings.HasPendingChanges)
            {
                MessageBox.Show(this, "Use Eco OFF to restore the pending changes before saving a new original.",
                    "Original Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_originalSettings.HasSnapshot && MessageBox.Show(this,
                "Replace the saved original settings with the current settings for the selected options?",
                "Replace Original Settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SaveFromUi();
            _originalSettings.Capture(_state, _isAdmin);
            if (!_isAdmin) LogWarn("Original settings include user GPU preferences only. Administrator-only settings will be captured before they are first applied.");
        }
        catch (Exception ex)
        {
            LogError($"Save original settings failed: {ex.Message}");
        }
        RefreshOriginalSettingsStatus();
    }

    private bool RestoreOriginalSettings(bool restoreAll)
    {
        Log(restoreAll ? "=== RESTORE ORIGINAL SETTINGS ===" : "=== ECO OFF ===");
        bool processesRestored = StopEcoQos();
        bool restored = false;
        _ecoEnabled = false;
        try
        {
            restored = _originalSettings.Restore(_isAdmin, restoreAll);
            if (restored && processesRestored)
                LogInfo("Original settings restored. Restart browsers to apply policy changes.");
            else
                LogWarn("Restore incomplete. See the log, then retry Restore Original Settings (as Administrator if required).");
        }
        catch (Exception ex)
        {
            LogError($"Restore failed: {ex.Message}");
        }
        UpdateStatus(false);
        RefreshOriginalSettingsStatus();
        return restored && processesRestored;
    }

    private bool StopEcoQos()
    {
        bool hookRemoved = true;
        if (_ecoHookActive)
        {
            try { HookManager.UnsubscribeWindowEvents(); }
            catch (Exception ex) { LogWarn($"Could not remove the foreground hook: {ex.Message}"); hookRemoved = false; }
            if (hookRemoved) _ecoHookActive = false;
        }
        bool restored = EnergyManager.UnthrottleAllUserProcesses();
        if (!restored) LogWarn("Some process settings could not be restored; cleanup will be retried.");
        return restored && hookRemoved;
    }

    private void RefreshOriginalSettingsStatus()
    {
        try
        {
            bool pending = _originalSettings.HasPendingChanges || (EnergyManager.HasTrackedProcesses || _ecoHookActive) && !_ecoEnabled;
            btnSaveOriginal.Enabled = !_ecoEnabled && !pending && !EnergyManager.HasTrackedProcesses;
            btnRestoreOriginal.Enabled = _originalSettings.HasSnapshot || EnergyManager.HasTrackedProcesses || _ecoHookActive;
            UpdateStatus(_ecoEnabled);
            if (pending && !_ecoEnabled)
            {
                lblStatus.Text += " — Restore pending.";
                lblStatus.ForeColor = Color.DarkOrange;
                LogWarn("Saved changes are waiting to be restored. Use Eco OFF or Restore Original Settings.");
            }
        }
        catch (Exception ex)
        {
            btnSaveOriginal.Enabled = false;
            btnRestoreOriginal.Enabled = _originalSettings.HasSnapshot;
            LogError($"Cannot read original settings: {ex.Message}");
        }
    }

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
            _state.Save(_configPath);

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
                ImportProfileJson(json);

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

    internal void ImportProfileJson(string json)
    {
        // Parse and validate before atomically replacing the profile. A bad import
        // must leave both the current runtime configuration and saved file intact.
        var imported = AppState.Parse(json);
        imported.Save(_configPath);
        CopyState(imported, _state);
        ApplyStateToUi();
        EnergyManager.SetBypass(_state.BypassProcesses);
    }

    private static void CopyState(AppState src, AppState dst)
    {
        src.Normalize();
        dst.IncludePowerPlan = src.IncludePowerPlan;
        dst.IncludeEdge      = src.IncludeEdge;
        dst.IncludeChrome    = src.IncludeChrome;
        dst.IncludeGpuPref   = src.IncludeGpuPref;
        dst.IncludeEcoQos    = src.IncludeEcoQos;

        dst.GpuPrefApps      = src.GpuPrefApps.ToArray();
        dst.BypassProcesses  = src.BypassProcesses.ToArray();
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
        if (ShowExitWarning(
            "Quitting turns Eco Mode off and restores the original power plan settings, browser policies, GPU preferences, " +
            "and process priority/throttling settings changed by EcoMode.\r\n\r\n" +
            "Restart browsers for restored policies to take effect.\r\n\r\nChoose OK to restore settings and quit.",
            "Quit and Restore Settings", MessageBoxButtons.OKCancel) != DialogResult.OK)
            return;

        _reallyExit = true;
        Close();
    }

    internal virtual DialogResult ShowExitWarning(string message, string title, MessageBoxButtons buttons) =>
        MessageBox.Show(this, message, title, buttons, MessageBoxIcon.Warning);

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


    private void Log(string msg, Color? color = null)
    {
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
