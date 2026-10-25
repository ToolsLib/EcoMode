using Microsoft.Win32;

namespace ecomode;

// Encoding is kept separate from the destination whitelist so it can be exercised
// against a disposable registry key without granting access to production keys.
internal static class RegistryValueAccess
{
    public static SettingValue Read(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        if (key == null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return SettingValue.Missing;
        object data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidDataException($"Cannot read registry value {name}.");
        return SettingValue.From(data, key.GetValueKind(name));
    }

    public static void Write(RegistryKey root, string path, string name, SettingValue value)
    {
        if (!value.Exists)
        {
            using var existing = root.OpenSubKey(path, writable: true);
            existing?.DeleteValue(name, throwOnMissingValue: false);
            return;
        }
        object data = value.Kind switch
        {
            RegistryValueKind.DWord => value.Data.GetInt32(),
            RegistryValueKind.QWord => value.Data.GetInt64(),
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.Data.GetString()!,
            RegistryValueKind.MultiString => value.Data.EnumerateArray().Select(e => e.GetString()!).ToArray(),
            RegistryValueKind.Binary or RegistryValueKind.None => value.Data.GetBytesFromBase64(),
            _ => throw new InvalidDataException($"Unsupported registry value kind: {value.Kind}")
        };
        using var key = root.CreateSubKey(path, writable: true);
        key.SetValue(name, data, value.Kind);
    }
}
