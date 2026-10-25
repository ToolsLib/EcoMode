using System.Text.Json;

namespace ecomode;

public partial class MainForm
{
    private LiveAppsForm? _liveAppsForm;
    private readonly Button btnLiveApps = new UtilityButton();
    private readonly ToolStripMenuItem miLiveApps = new("&Live apps…");

    private void ShowLiveApps()
    {
        if (_exitInProgress || _changingEcoMode) return;
        if (_liveAppsForm == null || _liveAppsForm.IsDisposed)
            _liveAppsForm = new LiveAppsForm(ReadLiveApps, SetLiveAppExclusion) { Icon = Icon };
        if (!_liveAppsForm.Visible) _liveAppsForm.Show(this);
        _liveAppsForm.BringToFront();
        _liveAppsForm.Activate();
    }

    internal virtual IReadOnlyList<LiveProcessSnapshot> ReadLiveApps() =>
        IsDisposed || _exitInProgress ? [] : EnergyManager.GetLiveProcesses(_ecoEnabled && _ecoHookActive);

    internal void SetLiveAppExclusion(string name, bool excluded)
    {
        if (_exitInProgress || _changingEcoMode) throw new InvalidOperationException(L.T("Wait for the current operation to finish."));
        if (string.IsNullOrWhiteSpace(name) || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
            !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(L.T("Select a valid executable name."));
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        if (EnergyManager.DefaultBypass.Contains(name) || ProcessExclusions.SecurityProcesses.Contains(name) ||
            string.Equals(name, self.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.T("Built-in safety exclusions cannot be changed."));

        static string ImageName(string value)
        {
            string clean = value.Trim().Trim('"').Trim();
            return clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? clean : clean + ".exe";
        }
        var saved = _state.BypassProcesses.Where(value => !string.Equals(ImageName(value), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (excluded) saved.Add(name);
        var candidate = AppState.Parse(JsonSerializer.Serialize(_state));
        candidate.BypassProcesses = saved.ToArray();
        // Persist only this exclusion change; other unsaved controls stay as edited.
        candidate.Save(_configPath);
        _state.BypassProcesses = candidate.BypassProcesses;
        var edited = txtEcoQosProcs.Lines.Where(value => !string.Equals(ImageName(value), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (excluded) edited.Add(name);
        txtEcoQosProcs.Lines = edited.ToArray();
        EnergyManager.SetBypass(_state.BypassProcesses);
        if (!excluded && _ecoEnabled && _ecoHookActive)
        {
            try { ScanBackgroundProcesses(newProcessesOnly: true); _sweepSchedule.Mark(_runtimeClock.Elapsed); }
            catch (Exception ex) { LogWarn(L.T("Background app check failed: {0}", ex.Message)); }
        }
        RefreshOriginalSettingsStatus();
        LogInfo(excluded ? L.T("{0} excluded from throttling. Existing changes were queued for restoration.", name) :
            L.T("Exclusion removed for {0}.", name));
    }
}
