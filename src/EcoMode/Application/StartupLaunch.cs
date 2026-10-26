namespace ecomode;

internal static class StartupLaunch
{
    internal const string Argument = "--startup";
    internal static bool IsAutomatic(IEnumerable<string> arguments) => arguments.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    internal static AppView SelectView(bool automatic, ViewPreferences preferences, Func<AppView> choose, Action<string> saveFailed) =>
        automatic ? preferences.Load() ?? AppView.Simple : preferences.GetOrChoose(choose, saveFailed);

    internal static string CurrentCommand() => CreateCommand(
        Environment.ProcessPath ?? throw new InvalidOperationException(L.T("The app path is unavailable.")), Environment.GetCommandLineArgs());

    internal static string CreateCommand(string executable, string[] commandLine)
    {
        static string QuotePath(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.Contains('"') || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Any(char.IsControl))
                throw new InvalidOperationException(L.T("The startup executable path is invalid."));
            return '"' + path + '"';
        }
        string command = QuotePath(executable);
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            if (commandLine.Length == 0 || string.IsNullOrWhiteSpace(commandLine[0]))
                throw new InvalidOperationException(L.T("The app assembly path is unavailable."));
            command += " " + QuotePath(Path.GetFullPath(commandLine[0]));
        }
        command += " " + Argument;
        if (command.Length > 260)
            throw new InvalidOperationException(L.T("The startup command is too long. Move EcoMode to a shorter path and try again."));
        return command;
    }
}
