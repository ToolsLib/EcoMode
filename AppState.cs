using System.Text.Json;

namespace ecomode;

public sealed class AppState
{
    public bool IncludePowerPlan { get; set; } = true;
    public bool IncludeEdge { get; set; } = true;
    public bool IncludeChrome { get; set; } = true;
    public bool IncludeGpuPref { get; set; } = true;
    public bool IncludeEcoQos { get; set; } = true;
    public string[] GpuPrefApps { get; set; } =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
    ];
    public string[] BypassProcesses { get; set; } = [];
    public string[]? EcoQosProcesses { get; set; }

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "settings.json");

    internal void Normalize()
    {
        GpuPrefApps = NormalizeNames(GpuPrefApps);
        BypassProcesses = NormalizeNames(BypassProcesses);
        if (GpuPrefApps.Any(path => !Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("GPU applications must be absolute paths to .exe files.");
        EcoQosProcesses = null;
    }

    private static string[] NormalizeNames(string[]? names) => (names ?? [])
        .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim().Trim('"').Trim())
        .Where(name => name.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static AppState Parse(string json)
    {
        var state = JsonSerializer.Deserialize<AppState>(json) ?? throw new InvalidDataException("The profile contains no settings.");
        state.Normalize();
        return state;
    }

    public void Save(string? path = null)
    {
        Normalize();
        new FileSettingsSnapshotStore(path ?? ConfigPath).Write(
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static AppState Load(string? path = null)
    {
        try
        {
            string config = path ?? ConfigPath;
            if (File.Exists(config)) return Parse(File.ReadAllText(config));
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        return new AppState();
    }
}
