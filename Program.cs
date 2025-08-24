namespace ecomode;

using System;
using System.Security.Principal;
using System.Windows.Forms;
using System.Text;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Global exception handlers (before anything else)
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ShowAndLog("UI Thread Exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            ShowAndLog("Non-UI/Background Exception", e.ExceptionObject as Exception ?? new Exception("Unknown"));

        try
        {
            ApplicationConfiguration.Initialize();
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

            // Wrap constructor too, in case InitializeComponent throws
            MainForm form;
            try
            {
                form = new MainForm(isAdmin);
            }
            catch (Exception ex)
            {
                ShowAndLog("Error creating MainForm (InitializeComponent?)", ex);
                return;
            }

            Application.Run(form);
        }
        catch (Exception ex)
        {
            ShowAndLog("Fatal error in Program.Main", ex);
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
            MessageBox.Show($"{title}:\r\n\r\n{ex}", "EcoModeGUI — Error",
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