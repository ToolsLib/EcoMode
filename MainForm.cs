namespace ecomode;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Reflection;
using ecomode.Interop;

public partial class MainForm : Form
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
        lblStatus.Text = ecoPlannedOn ? "Eco is active" : "Eco is off";
        lblStatus.ForeColor = ecoPlannedOn ? UtilityTheme.Accent : UtilityTheme.Text;
        lblStatusDetail.Text = ecoPlannedOn ? "Background savings enabled." : "Ready to save power.";
    }

    #region UI wiring
    private Button btnEcoOn = new UtilityButton();
    private Button btnEcoOff = new UtilityButton();
    private Button btnSaveOriginal = new UtilityButton();
    private Button btnRestoreOriginal = new UtilityButton();
    private CheckBox chkPowerPlan = new();
    private CheckBox chkEdge = new();
    private CheckBox chkChrome = new();
    private CheckBox chkGpuPref = new();
    private CheckBox chkEcoQos = new();
    private Button btnBrowseGpu = new UtilityButton();
    private ComboBox cboPresets = new UtilityComboBox();
    private Button btnLoadPreset = new UtilityButton();
    private Label lblPresetDescription = new();

    private RichTextBox txtGpuApps = new UtilityTextEditor();
    private RichTextBox txtEcoQosProcs = new UtilityTextEditor();
    private Label lblStatus = new();
    private Button btnSave = new UtilityButton();
    private Button btnDonate = new UtilityButton();
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
            bool hasSnapshot = _originalSettings.HasSnapshot;
            bool pending = _originalSettings.HasPendingChanges || (EnergyManager.HasTrackedProcesses || _ecoHookActive) && !_ecoEnabled;
            btnSaveOriginal.Enabled = !_ecoEnabled && !pending && !EnergyManager.HasTrackedProcesses;
            btnRestoreOriginal.Enabled = hasSnapshot || EnergyManager.HasTrackedProcesses || _ecoHookActive;
            lblOriginalStatus.Text = pending ? _ecoEnabled ? "ORIGINALS  /  ECO CHANGES ACTIVE" : "ORIGINALS  /  RESTORE PENDING" :
                hasSnapshot ? "ORIGINALS  /  SAVED" : "ORIGINALS  /  NOT SAVED YET";
            lblOriginalStatus.ForeColor = pending && !_ecoEnabled ? UtilityTheme.Warning : UtilityTheme.Muted;
            UpdateStatus(_ecoEnabled);
            if (pending && !_ecoEnabled)
            {
                lblStatus.Text = "Restore pending";
                lblStatus.ForeColor = UtilityTheme.Warning;
                lblStatusDetail.Text = "Use Eco OFF to finish recovery.";
                LogWarn("Saved changes are waiting to be restored. Use Eco OFF or Restore Original Settings.");
            }
        }
        catch (Exception ex)
        {
            btnSaveOriginal.Enabled = false;
            btnRestoreOriginal.Enabled = _originalSettings.HasSnapshot;
            lblOriginalStatus.Text = "ORIGINALS  /  COULD NOT READ";
            lblOriginalStatus.ForeColor = UtilityTheme.Warning;
            LogError($"Cannot read original settings: {ex.Message}");
        }
    }

    #region Helpers

    private void OnLoadPreset(object? sender, EventArgs e)
    {
        if (cboPresets.SelectedItem is not ProfilePreset preset) return;
        try
        {
            EnsurePresetCanLoad();
            if (MessageBox.Show(this,
                $"Load the {preset.Name} preset? This replaces your current profile and any unsaved edits.\r\n\r\n" +
                preset.Description + "\r\n\r\nReview the app paths and bypass list, then click Eco ON to apply it.",
                "Load Preset", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            LoadPreset(preset.Id);
        }
        catch (Exception ex)
        {
            LogError($"Load preset failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Cannot Load Preset", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EnsurePresetCanLoad()
    {
        if (_ecoEnabled || _ecoHookActive || EnergyManager.HasTrackedProcesses || _originalSettings.HasPendingChanges)
            throw new InvalidOperationException("Use Eco OFF and finish restoring pending changes before loading a preset.");
    }

    internal void LoadPreset(string id)
    {
        var preset = ProfilePresets.Get(id);
        EnsurePresetCanLoad();
        ReplaceProfile(preset.CreateState());
        LogInfo($"{preset.Name} preset loaded and saved. Review the app paths and bypass list, then click Eco ON.");
    }

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
        ReplaceProfile(AppState.Parse(json));
    }

    private void ReplaceProfile(AppState imported)
    {
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

        txtLog.SelectionColor = color ?? UtilityTheme.Muted;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionColor = txtLog.ForeColor;
    }
    
    private void LogInfo(string msg)  => Log(msg, UtilityTheme.Text);
    private void LogWarn(string msg)  => Log(msg, UtilityTheme.Warning);
    private void LogError(string msg) => Log(msg, UtilityTheme.Error);

    #endregion
}
