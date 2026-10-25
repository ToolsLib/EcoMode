using System.Text.Json;
using System.Text.Json.Serialization;

namespace ecomode;

// Preserve the stored "Simple" value used by earlier previews; its UI name is Standard.
internal enum AppView { Simple, Advanced }

// A display preference belongs to this user, independently of importable profiles.
internal sealed class ViewPreferences(string? path = null)
{
    private readonly FileSettingsSnapshotStore _store = new(path ?? DefaultPath);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter<AppView>(allowIntegerValues: false) }
    };

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "view-settings.json");

    internal AppView? Load()
    {
        try
        {
            if (!_store.Exists) return null;
            var saved = JsonSerializer.Deserialize<Preference>(_store.Read(), JsonOptions);
            return saved?.View is AppView view && Enum.IsDefined(view) ? view : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal void Save(AppView view)
    {
        if (!Enum.IsDefined(view)) throw new ArgumentOutOfRangeException(nameof(view));
        _store.Write(JsonSerializer.Serialize(new Preference { View = view }, JsonOptions));
    }

    internal AppView GetOrChoose(Func<AppView> choose, Action<string> saveFailed)
    {
        if (Load() is AppView saved) return saved;
        var selected = choose();
        try { Save(selected); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveFailed(L.T("Your view choice could not be remembered. You may be asked again next time. ") + ex.Message);
        }
        return selected;
    }

    private sealed class Preference
    {
        public AppView? View { get; init; }
    }
}
