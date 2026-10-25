using System.Text.Json;

namespace ecomode;

internal sealed class LanguagePreferences(string? path = null)
{
    private readonly FileSettingsSnapshotStore _store = new(path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EcoMode", "language-settings.json"));

    internal string? Load()
    {
        try
        {
            if (!_store.Exists) return null;
            string? code = JsonSerializer.Deserialize<Preference>(_store.Read())?.Language;
            return L.Languages.Any(language => language.Code == code) ? code : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal void Save(string code)
    {
        if (!L.Languages.Any(language => language.Code == code)) throw new ArgumentOutOfRangeException(nameof(code));
        _store.Write(JsonSerializer.Serialize(new Preference { Language = code }));
    }

    private sealed class Preference
    {
        public string? Language { get; init; }
    }
}
