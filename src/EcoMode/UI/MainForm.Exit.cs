namespace ecomode;

public partial class MainForm
{
    private bool _exitWithoutRestore;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
        if (e.Cancel) { _reallyExit = false; return; }
        _exitInProgress = true;
        _automaticPolicy.Override();

        bool restored = TryRestoreForExit();
        if (!restored && e.CloseReason == CloseReason.UserClosing) RestoreFromTray();
        // Shutdown still makes a best-effort restore without blocking on a dialog.
        while (!restored && e.CloseReason == CloseReason.UserClosing)
        {
            switch (ShowIncompleteRestoreDialog())
            {
                case RestoreExitChoice.Retry:
                    restored = TryRestoreForExit();
                    continue;
                case RestoreExitChoice.QuitAnyway:
                    _exitWithoutRestore = true;
                    LogWarn(L.T("Quit anyway selected. Process settings may remain changed; saved system recovery records are preserved."));
                    break;
                default:
                    e.Cancel = true;
                    _reallyExit = false;
                    _exitInProgress = false;
                    RestoreFromTray();
                    return;
            }
            break;
        }
        trayIcon.Visible = false;
    }

    private bool TryRestoreForExit()
    {
        try { return RestoreOriginalSettings(restoreAll: false); }
        catch (Exception ex)
        {
            LogError(L.T("Restore failed: {0}", ex.Message));
            return false;
        }
    }

    internal virtual RestoreExitChoice ShowIncompleteRestoreDialog()
    {
        using var dialog = new RestoreExitDialog();
        dialog.ShowDialog(this);
        return dialog.Choice;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopRuntimeMonitoring();
        try
        {
            // Do not repeat blocked writes after the user explicitly chose to skip.
            if (!_exitWithoutRestore) StopEcoQos();
        }
        finally
        {
            EnergyManager.ReleaseTrackedProcessHandles();
            EnergyManager.Logger = null;
            trayIcon.Dispose();
            base.OnFormClosed(e);
        }
    }
}
