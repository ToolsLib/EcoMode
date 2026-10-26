using Microsoft.Win32;
using ecomode.Interop;
using System.Diagnostics;

namespace ecomode;

public partial class MainForm
{
    private readonly IPowerSource _powerSource;
    private readonly AutomationPreferences _automationPreferences;
    private readonly AutomaticEcoPolicy _automaticPolicy = new();
    private readonly BatteryHistory _batteryHistory = new();
    private readonly ProcessSweepSchedule _sweepSchedule = new();
    private readonly Stopwatch _runtimeClock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _monitorTimer = new() { Interval = 15_000 };
    private readonly bool _monitoringRequested;
    private bool _monitoringStarted, _exitInProgress, _automaticProcessMode, _applyingAutomatic, _automationBlocked;
    private BatterySnapshot _battery = BatterySnapshot.Unknown;

    private void StartRuntimeMonitoring()
    {
        if (!_monitoringRequested || _monitoringStarted) return;
        _monitoringStarted = true;
        _monitorTimer.Tick += OnMonitorTick;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        PollPowerAndProcesses(forceSweep: true);
        _monitorTimer.Start();
    }

    private void StopRuntimeMonitoring()
    {
        _monitorTimer.Stop();
        if (_monitoringStarted)
        {
            _monitorTimer.Tick -= OnMonitorTick;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        _monitoringStarted = false;
    }

    private void OnMonitorTick(object? sender, EventArgs e) => PollPowerAndProcesses();

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || _exitInProgress || !_monitoringStarted) return;
                if (e.Mode == PowerModes.Suspend) { _monitorTimer.Stop(); _batteryHistory.Break(); return; }
                if (e.Mode == PowerModes.Resume) _batteryHistory.Break();
                PollPowerAndProcesses(forceSweep: e.Mode == PowerModes.Resume);
                _monitorTimer.Start();
            }));
        }
        catch (InvalidOperationException) { /* The window closed before the event was posted. */ }
    }

    internal void PollPowerAndProcesses(bool forceSweep = false)
    {
        if (IsDisposed || _exitInProgress || _changingEcoMode) return;
        try { _battery = _powerSource.Read(); }
        catch { _battery = BatterySnapshot.Unknown; }
        _batteryHistory.Add(_runtimeClock.Elapsed, DateTimeOffset.Now, _battery, _ecoEnabled);
        bool wasProcessModeActive = _ecoEnabled && _ecoHookActive;
        EvaluateAutomaticMode();
        bool enabled = _ecoEnabled && _ecoHookActive;
        if (_sweepSchedule.Due(_runtimeClock.Elapsed, enabled, forceSweep && wasProcessModeActive))
        {
            try { ScanBackgroundProcesses(newProcessesOnly: true); }
            catch (Exception ex) { LogWarn(L.T("Background app check failed: {0}", ex.Message)); }
        }
        UpdateBatteryDashboard();
    }

    internal virtual void StartForegroundMonitoring() => HookManager.SubscribeToWindowEvents();
    internal virtual void ScanBackgroundProcesses(bool newProcessesOnly)
    {
        if (newProcessesOnly) EnergyManager.RefreshUserBackgroundProcesses();
        else EnergyManager.ThrottleAllUserBackgroundProcesses();
    }

    private void EvaluateAutomaticMode()
    {
        _automationBlocked = _originalSettingsCaptureFailed;
        bool? desired = _automaticPolicy.DesiredState(_battery, manualProfileActive: _ecoEnabled && !_automaticProcessMode);
        if (_automaticProcessMode && !_automaticPolicy.Enabled) desired = false;
        if (desired == null) return;
        _applyingAutomatic = true;
        try
        {
            if (desired == false && _automaticProcessMode)
            {
                if (!RestoreOriginalSettings(restoreAll: false)) _automaticPolicy.Override();
                return;
            }
            if (desired != true || _ecoEnabled) return;
            _automationBlocked = _originalSettingsCaptureFailed || _originalSettings.HasPendingChanges ||
                EnergyManager.HasTrackedProcesses || _ecoHookActive;
            if (_automationBlocked) return;
            StartForegroundMonitoring();
            _ecoHookActive = true;
            ScanBackgroundProcesses(newProcessesOnly: false);
            _sweepSchedule.Mark(_runtimeClock.Elapsed);
            _automaticProcessMode = _ecoEnabled = true;
            LogInfo(L.T("Automatic Eco Mode started for background apps. Your profile settings were not changed."));
            RefreshOriginalSettingsStatus();
            RecordPowerModeChange();
        }
        catch (Exception ex)
        {
            _automaticPolicy.Override();
            _automaticProcessMode = _ecoEnabled = false;
            StopEcoQos();
            LogWarn(L.T("Automatic mode could not start: {0}", ex.Message));
            RefreshOriginalSettingsStatus();
        }
        finally { _applyingAutomatic = false; }
    }

    private void RecordPowerModeChange()
    {
        _batteryHistory.Add(_runtimeClock.Elapsed, DateTimeOffset.Now, _battery, _ecoEnabled);
        UpdateBatteryDashboard();
    }

    private void SetAutomaticMode(bool enabled)
    {
        try { _automationPreferences.Save(enabled); }
        catch (Exception ex)
        {
            _updatingAutomationSwitch = true;
            chkAutomatic.Checked = _automaticPolicy.Enabled;
            _updatingAutomationSwitch = false;
            LogWarn(L.T("Automatic mode preference could not be saved: {0}", ex.Message));
            return;
        }
        _automaticPolicy.Enabled = enabled;
        _automaticPolicy.Resume();
        EvaluateAutomaticMode();
        UpdateBatteryDashboard();
    }

    private void ResumeAutomaticMode()
    {
        if (_ecoEnabled && !_automaticProcessMode)
        {
            _applyingAutomatic = true;
            try { if (!RestoreOriginalSettings(false)) return; }
            finally { _applyingAutomatic = false; }
        }
        _automaticPolicy.Resume();
        EvaluateAutomaticMode();
        UpdateBatteryDashboard();
    }
}
