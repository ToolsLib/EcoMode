using System.Diagnostics;
using System.Text.Json;
using System.Reflection;
using ecomode.Interop;

namespace ecomode;

public partial class MainForm : Form
{
    private readonly bool _isAdmin;
    private readonly AppState _state;
    private readonly IOriginalSettingsManager _originalSettings;
    private readonly string? _configPath;

    private bool _ecoHookActive = false;
    private bool _ecoEnabled;
    private bool _changingEcoMode;
    private bool _originalSettingsCaptureFailed;
    private string _originalStatusKey = "SAVED ORIGINALS";

    public MainForm(bool isAdmin) : this(isAdmin, null) { }

    internal MainForm(bool isAdmin, IOriginalSettingsManager? originalSettings, string? configPath = null,
        string? viewPreferencesPath = null, AppView? initialView = null, string? languagePreferencesPath = null,
        IPowerSource? powerSource = null, string? automationPreferencesPath = null, bool startMonitoring = true)
    {
        try
        {
            _isAdmin = isAdmin;
            _configPath = configPath;
            _viewPreferences = new ViewPreferences(viewPreferencesPath);
            _languagePreferences = new LanguagePreferences(languagePreferencesPath);
            _powerSource = powerSource ?? new WindowsPowerSource();
            _automationPreferences = new AutomationPreferences(automationPreferencesPath);
            _automaticPolicy.Enabled = _automationPreferences.Load();
            _monitoringRequested = startMonitoring;
            _view = initialView ?? _viewPreferences.Load() ?? AppView.Simple;
            BuildUi();
            L.TranslateTree(this);
            L.TranslateItems(trayMenu.Items);
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
            EnsureOriginalSettingsSaved();
            RefreshOriginalSettingsStatus();
            ApplyViewLayout(resize: true);
            L.LanguageChanged += OnLanguageChanged;
        }
        catch
        {
            trayIcon.Dispose();
            trayMenu.Dispose();
            throw;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && Visible)
            HideToTray();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRuntimeMonitoring();
            _liveAppsForm?.Dispose();
            _monitorTimer.Dispose();
            L.LanguageChanged -= OnLanguageChanged;
            trayIcon.Dispose();
            trayMenu.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdateStatus(bool ecoPlannedOn)
    {
        ecoSwitch.State = ecoPlannedOn ? EcoSwitchState.On : EcoSwitchState.Off;
        lblStatus.Text = ecoPlannedOn ? L.T("Eco mode is on") : L.T("Eco mode is off");
        lblStatus.ForeColor = ecoPlannedOn ? UtilityTheme.Accent : UtilityTheme.Text;
        lblStatusDetail.Text = ecoPlannedOn ? L.T("Switch off to restore originals.") : L.T("Switch on to save power.");
    }

    #region UI wiring
    private EcoModeSwitch ecoSwitch = new();
    private Button btnSaveOriginal = new UtilityButton();
    private Button btnRestoreOriginal = new UtilityButton();
    private CheckBox chkPowerPlan = new UtilitySwitch();
    private CheckBox chkEdge = new UtilitySwitch();
    private CheckBox chkChrome = new UtilitySwitch();
    private CheckBox chkGpuPref = new UtilitySwitch();
    private CheckBox chkEcoQos = new UtilitySwitch();
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
            Title = L.T("Select application(s) to set GPU preference"),
            Filter = L.T("Applications (*.exe)|*.exe|All files (*.*)|*.*"),
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
                    Log(L.T("Browse: skipped missing file: {0}", p));
            }
            catch (Exception ex)
            {
                Log(L.T("Browse: error adding path -> {0} ({1})", raw, ex.Message));
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
        UpdateSimpleSummary();
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

        Log(L.T("Settings saved."));
    }
    #endregion

    private void OnEcoSwitchClick(object? sender, EventArgs e)
    {
        if (_changingEcoMode) return;
        _changingEcoMode = true;
        _automaticPolicy.Override();
        ecoSwitch.Enabled = false;
        try { ToggleEco(ecoSwitch.State == EcoSwitchState.Off); }
        finally
        {
            _changingEcoMode = false;
            ecoSwitch.Enabled = true;
            UpdateBatteryDashboard();
        }
    }

    private void ToggleEco(bool eco)
    {
        if (!eco)
        {
            RestoreOriginalSettings(restoreAll: false);
            return;
        }

        try
        {
            _automaticProcessMode = false;
            SaveFromUi();
            if (!EnsureOriginalSettingsSaved()) return;

            Log(L.T("=== ECO ON ==="));
            bool applied = _originalSettings.ApplyEco(_state, _isAdmin);
            _ecoEnabled = true;
            if (!_isAdmin && (_state.IncludePowerPlan || _state.IncludeEdge || _state.IncludeChrome))
                LogWarn(L.T("Power plan and browser policies skipped: restart as Administrator to use them."));

            if (_state.IncludeEcoQos)
            {
                if (!_ecoHookActive)
                {
                    StartForegroundMonitoring();
                    _ecoHookActive = true;
                }
                ScanBackgroundProcesses(newProcessesOnly: false);
                _sweepSchedule.Mark(_runtimeClock.Elapsed);
                Log(L.T("EcoQoS enabled. Foreground apps keep their original priority."));
            }
            else
            {
                StopEcoQos();
            }

            if (!applied) LogWarn(L.T("Some settings could not be applied. See the log; switch Eco Mode off to restore any partial changes."));
            Log(L.T("Restart browsers to apply policy changes."));
        }
        catch (Exception ex)
        {
            LogError(L.T("Eco ON failed: {0}", ex.Message));
        }
        finally
        {
            UpdateStatus(_ecoEnabled);
            RefreshOriginalSettingsStatus();
            RecordPowerModeChange();
        }
    }

    private bool EnsureOriginalSettingsSaved()
    {
        try
        {
            bool hadSnapshot = _originalSettings.HasSnapshot;
            _originalSettings.EnsureCaptured(_state, _isAdmin);
            _originalSettingsCaptureFailed = false;
            if (!hadSnapshot && !_isAdmin)
                LogInfo(L.T("User originals saved. Power plan and browser policy originals will be saved when started as Administrator."));
            return true;
        }
        catch (Exception ex)
        {
            _originalSettingsCaptureFailed = true;
            LogError(L.T("Could not save original settings: {0}. Eco Mode remains off. Retry Save originals or switch on to retry the backup.", ex.Message));
            return false;
        }
    }

    private void SaveOriginalSettings()
    {
        try
        {
            if (_ecoEnabled || EnergyManager.HasTrackedProcesses || _originalSettings.HasPendingChanges)
            {
                MessageBox.Show(this, L.T("Switch Eco Mode off and finish restoring pending changes before saving a new original."),
                    L.T("Original Settings"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_originalSettings.HasSnapshot && MessageBox.Show(this,
                L.T("Replace the saved original settings with the current settings for the selected options?"),
                L.T("Replace Original Settings"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            SaveFromUi();
            _originalSettings.Capture(_state, _isAdmin);
            _originalSettingsCaptureFailed = false;
            if (!_isAdmin) LogWarn(L.T("Original settings include user GPU preferences only. Administrator-only settings will be captured before they are first applied."));
        }
        catch (Exception ex)
        {
            _originalSettingsCaptureFailed = true;
            LogError(L.T("Save original settings failed: {0}", ex.Message));
        }
        RefreshOriginalSettingsStatus();
    }

    private bool RestoreOriginalSettings(bool restoreAll)
    {
        if (!_applyingAutomatic) _automaticPolicy.Override();
        _automaticProcessMode = false;
        Log(restoreAll ? L.T("=== RESTORE ORIGINAL SETTINGS ===") : L.T("=== ECO OFF ==="));
        bool processesRestored = StopEcoQos();
        bool restored = false;
        _ecoEnabled = false;
        try
        {
            restored = _originalSettings.Restore(_isAdmin, restoreAll);
            if (restored && processesRestored)
                LogInfo(L.T("Original settings restored. Restart browsers to apply policy changes."));
            else
                LogWarn(L.T("Restore incomplete. See the log, then retry Restore originals (as Administrator if required)."));
        }
        catch (Exception ex)
        {
            LogError(L.T("Restore failed: {0}", ex.Message));
        }
        UpdateStatus(false);
        RefreshOriginalSettingsStatus();
        RecordPowerModeChange();
        return restored && processesRestored;
    }

    private bool StopEcoQos()
    {
        bool hookRemoved = true;
        if (_ecoHookActive)
        {
            try { HookManager.UnsubscribeWindowEvents(); }
            catch (Exception ex) { LogWarn(L.T("Could not remove the foreground hook: {0}", ex.Message)); hookRemoved = false; }
            if (hookRemoved) _ecoHookActive = false;
        }
        bool restored;
        try { restored = RestoreProcessSettings(); }
        catch (Exception ex)
        {
            LogError(L.T("Process cleanup failed: {0}", ex.Message));
            restored = false;
        }
        if (!restored) LogWarn(L.T("Some process settings could not be restored; cleanup will be retried."));
        return restored && hookRemoved;
    }

    internal virtual bool RestoreProcessSettings() => EnergyManager.UnthrottleAllUserProcesses();

    private void RefreshOriginalSettingsStatus()
    {
        try
        {
            bool hasSnapshot = _originalSettings.HasSnapshot;
            bool pending = _originalSettings.HasPendingChanges || (EnergyManager.HasTrackedProcesses || _ecoHookActive) && !_ecoEnabled;
            btnSaveOriginal.Enabled = !_ecoEnabled && !pending && !EnergyManager.HasTrackedProcesses;
            btnRestoreOriginal.Enabled = hasSnapshot || EnergyManager.HasTrackedProcesses || _ecoHookActive;
            _originalStatusKey = pending ? _ecoEnabled ? "ORIGINALS  /  ECO CHANGES ACTIVE" : "ORIGINALS  /  RESTORE PENDING" :
                hasSnapshot ? "ORIGINALS  /  SAVED" : "ORIGINALS  /  NOT SAVED YET";
            lblOriginalStatus.Text = L.T(_originalStatusKey);
            lblOriginalStatus.ForeColor = pending && !_ecoEnabled ? UtilityTheme.Warning : UtilityTheme.Muted;
            UpdateStatus(_ecoEnabled);
            if (pending && !_ecoEnabled)
            {
                lblStatus.Text = L.T("Restore pending");
                lblStatus.ForeColor = UtilityTheme.Warning;
                ecoSwitch.State = EcoSwitchState.RestorePending;
                lblStatusDetail.Text = L.T("Click RETRY to finish restoring.");
                LogWarn(L.T("Saved changes are waiting to be restored. Use RETRY or Restore originals."));
            }
            else if (_originalSettingsCaptureFailed)
            {
                _originalStatusKey = "ORIGINALS  /  SAVE FAILED";
                lblOriginalStatus.Text = L.T(_originalStatusKey);
                lblOriginalStatus.ForeColor = UtilityTheme.Warning;
                lblStatusDetail.Text = L.T("Backup failed. Check the log and retry.");
            }
        }
        catch (Exception ex)
        {
            btnSaveOriginal.Enabled = false;
            // The store itself may be unreadable. Keep recovery available without
            // querying it again and preventing startup or the exit override.
            btnRestoreOriginal.Enabled = true;
            _originalStatusKey = "ORIGINALS  /  COULD NOT READ";
            lblOriginalStatus.Text = L.T(_originalStatusKey);
            lblOriginalStatus.ForeColor = UtilityTheme.Warning;
            ecoSwitch.State = EcoSwitchState.RestorePending;
            lblStatus.Text = L.T("Recovery needs attention");
            lblStatus.ForeColor = UtilityTheme.Warning;
            lblStatusDetail.Text = L.T("Check the activity log and retry.");
            LogError(L.T("Cannot read original settings: {0}", ex.Message));
        }
        RefreshSimpleStatus();
    }

    #region Helpers

    private void OnLoadPreset(object? sender, EventArgs e)
    {
        if (cboPresets.SelectedItem is not ProfilePreset preset) return;
        try
        {
            EnsurePresetCanLoad();
            if (MessageBox.Show(this,
                L.T("Load the {0} preset? This replaces your current profile and any unsaved edits.\r\n\r\n", preset.Name) +
                preset.Description + L.T("\r\n\r\nReview the app paths and bypass list, then switch Eco Mode on to apply it."),
                L.T("Load Preset"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            LoadPreset(preset.Id);
        }
        catch (Exception ex)
        {
            LogError(L.T("Load preset failed: {0}", ex.Message));
            MessageBox.Show(this, ex.Message, L.T("Cannot Load Preset"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EnsurePresetCanLoad()
    {
        if (_ecoEnabled || _ecoHookActive || EnergyManager.HasTrackedProcesses || _originalSettings.HasPendingChanges)
            throw new InvalidOperationException(L.T("Switch Eco Mode off and finish restoring pending changes before loading a preset."));
    }

    internal void LoadPreset(string id)
    {
        var preset = ProfilePresets.Get(id);
        EnsurePresetCanLoad();
        ReplaceProfile(preset.CreateState());
        LogInfo(L.T("{0} preset loaded and saved. Review the app paths and bypass list, then switch Eco Mode on.", preset.Name));
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
            L.T("Are you sure you want to reset EcoMode settings to defaults?\r\n") +
            L.T("This will overwrite your current profile."),
            L.T("Reset to Defaults"),
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

            LogInfo(L.T("Settings reset to defaults."));
        }
        catch (Exception ex)
        {
            LogError(L.T("Reset failed: {0}", ex.Message));
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
                Title = L.T("Export EcoMode Profile"),
                Filter = L.T("EcoMode profile (*.json)|*.json|All files (*.*)|*.*"),
                FileName = "EcoModeProfile.json",
                OverwritePrompt = true,
                AddExtension = true
            };

            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(sfd.FileName, json);
                LogInfo(L.T("Profile exported to: {0}", sfd.FileName));
            }
        }
        catch (Exception ex)
        {
            LogError(L.T("Export failed: {0}", ex.Message));
        }
    }

    private void OnMenuImportProfile(object? sender, EventArgs e)
    {
        try
        {
            using var ofd = new OpenFileDialog
            {
                Title = L.T("Import EcoMode Profile"),
                Filter = L.T("EcoMode profile (*.json)|*.json|All files (*.*)|*.*"),
                Multiselect = false,
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                var json = File.ReadAllText(ofd.FileName);
                ImportProfileJson(json);

                LogInfo(L.T("Profile imported from: {0}", ofd.FileName));
            }
        }
        catch (JsonException jex)
        {
            LogError(L.T("Import failed: invalid JSON ({0})", jex.Message));
        }
        catch (Exception ex)
        {
            LogError(L.T("Import failed: {0}", ex.Message));
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
        dst.IncludeEdge = src.IncludeEdge;
        dst.IncludeChrome = src.IncludeChrome;
        dst.IncludeGpuPref = src.IncludeGpuPref;
        dst.IncludeEcoQos = src.IncludeEcoQos;

        dst.GpuPrefApps = src.GpuPrefApps.ToArray();
        dst.BypassProcesses = src.BypassProcesses.ToArray();
    }

    private void HideToTray()
    {
        if (!Visible) return;
        if (_liveAppsForm is { IsDisposed: false }) _liveAppsForm.Hide();
        Hide();
        ShowTrayTip(L.T("EcoMode is still running here."));
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
            L.T("Quitting turns Eco Mode off and restores the original power plan settings, browser policies, GPU preferences, ") +
            L.T("and process priority/throttling settings changed by EcoMode.\r\n\r\n") +
            L.T("Restart browsers for restored policies to take effect.\r\n\r\nChoose OK to restore settings and quit."),
            L.T("Quit and Restore Settings"), MessageBoxButtons.OKCancel) != DialogResult.OK)
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
            LogError(L.T("Failed to open donate link: {0}", ex.Message));
        }
    }


    private void Log(string msg, Color? color = null)
    {
        if (color == UtilityTheme.Warning || color == UtilityTheme.Error)
        {
            _activityNeedsAttention = true;
            UpdateActivityButton();
        }
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionLength = 0;

        txtLog.SelectionColor = color ?? UtilityTheme.Muted;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionColor = txtLog.ForeColor;
    }

    private void LogInfo(string msg) => Log(msg, UtilityTheme.Text);
    private void LogWarn(string msg) => Log(msg, UtilityTheme.Warning);
    private void LogError(string msg) => Log(msg, UtilityTheme.Error);

    #endregion
}
