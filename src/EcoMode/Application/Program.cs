using System.Security.Principal;
using System.Text;

namespace ecomode;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Global exception handlers (before anything else)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ShowAndLog(L.T("UI Thread Exception"), e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            ShowAndLog(L.T("Non-UI/Background Exception"), e.ExceptionObject as Exception ?? new Exception("Unknown"));

        try
        {
            ApplicationConfiguration.Initialize();
            L.SetLanguage(new LanguagePreferences().Load() ?? L.DetectLanguage(System.Globalization.CultureInfo.CurrentUICulture));
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            using var instance = StartupElevation.AcquireInstance(isAdmin,
                () => MessageBox.Show(StartupElevation.Confirmation, L.T("Restart as Administrator?"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                (message, icon) => MessageBox.Show(message, "EcoMode", MessageBoxButtons.OK, icon));
            if (instance == null) return;

            MainForm form;
            try
            {
                string? viewSaveFailure = null;
                var view = new ViewPreferences().GetOrChoose(() =>
                {
                    using var choice = new ViewChoiceDialog();
                    choice.ShowDialog();
                    return choice.SelectedView;
                }, message => viewSaveFailure = message);
                form = new MainForm(isAdmin, null, initialView: view);
                if (viewSaveFailure != null) form.ReportViewSaveFailure(viewSaveFailure);
            }
            catch (Exception ex)
            {
                ShowAndLog(L.T("Error initializing EcoMode"), ex);
                return;
            }

            Application.Run(form);
        }
        catch (Exception ex)
        {
            ShowAndLog(L.T("Fatal error in Program.Main"), ex);
        }
    }

    private static void ShowAndLog(string title, Exception ex)
    {
        try
        {
            var path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\r\n{ex}\r\n\r\n",
                Encoding.UTF8);
        }
        catch { /* ignore logging issues */ }

        try
        {
            MessageBox.Show($"{title}:\r\n\r\n{ex}", L.T("EcoModeGUI — Error"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* last resort: swallow */ }
    }

    private static string GetLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EcoModeGUI");
        return Path.Combine(dir, "error.log");
    }
}
