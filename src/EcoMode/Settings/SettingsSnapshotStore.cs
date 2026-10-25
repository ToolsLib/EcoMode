using Microsoft.Win32;
using System.Text;

namespace ecomode;

internal interface ISettingsSnapshotStore
{
    string Description { get; }
    bool Exists { get; }
    string Read();
    void Write(string json);
}

internal sealed class FileSettingsSnapshotStore(string path) : ISettingsSnapshotStore
{
    public string Description => path;
    public bool Exists => File.Exists(path);
    public string Read() => File.ReadAllText(path);

    public void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}

// HKLM inherits administrator-only write access. Unlike a per-user file, this
// journal is shared by every Windows session and survives uninstall/reinstall.
internal sealed class MachineSettingsSnapshotStore : ISettingsSnapshotStore
{
    internal const string KeyPath = @"SOFTWARE\ToolsLib\EcoMode";
    internal const string ValueName = "OriginalSettingsV1";
    public string Description => $@"HKLM\{KeyPath}\{ValueName}";

    public bool Exists
    {
        get
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) != null;
        }
    }

    public string Read()
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string ?? throw new InvalidDataException(L.T("Machine restore journal is missing or invalid."));
    }

    public void Write(string json)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, json, RegistryValueKind.String);
        key.Flush();
    }
}
