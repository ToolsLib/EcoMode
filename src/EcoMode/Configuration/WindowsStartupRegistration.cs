using Microsoft.Win32;

namespace ecomode;

internal sealed record StartupRegistrationStatus(bool Enabled, bool UsesCurrentCopy);

internal interface IStartupRegistration
{
    StartupRegistrationStatus Read();
    void SetEnabled(bool enabled);
}

internal sealed class WindowsStartupRegistration(RegistryKey? root = null, string? keyPath = null, Func<string>? command = null) : IStartupRegistration
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "EcoMode";
    private readonly RegistryKey _root = root ?? Registry.CurrentUser;
    private readonly string _keyPath = keyPath ?? RunKey;
    private readonly Func<string> _command = command ?? StartupLaunch.CurrentCommand;

    public StartupRegistrationStatus Read()
    {
        using var key = _root.OpenSubKey(_keyPath, writable: false);
        var saved = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        bool current = false;
        if (saved is string text)
        {
            try { current = string.Equals(text, _command(), StringComparison.OrdinalIgnoreCase); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or PathTooLongException)
            { /* An invalid current location must not prevent disabling an existing entry. */ }
        }
        return new(saved != null, current);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            // Resolve and validate before opening a writable key.
            string command = _command();
            using var key = _root.CreateSubKey(_keyPath, writable: true);
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using var key = _root.OpenSubKey(_keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
