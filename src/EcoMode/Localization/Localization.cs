using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace ecomode;

internal sealed record AppLanguage(string Code, string NativeName)
{
    public override string ToString() => NativeName;
}

internal static class L
{
    internal static readonly ResourceManager Resources = new("ecomode.Localization.Strings", typeof(L).Assembly);
    internal static IReadOnlyList<AppLanguage> Languages { get; } = Array.AsReadOnly(new[]
    {
        new AppLanguage("en", "English"), new AppLanguage("fr", "Français"),
        new AppLanguage("es", "Español"), new AppLanguage("zh-Hans", "简体中文")
    });
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("en");
    private static readonly ConditionalWeakTable<object, Captions> Sources = new();
    internal static string Language => _culture.Name;
    internal static event Action? LanguageChanged;

    internal static string DetectLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "fr" => "fr",
        "es" => "es",
        "zh" => "zh-Hans",
        _ => "en"
    };

    internal static void SetLanguage(string code)
    {
        if (!Languages.Any(language => language.Code == code)) throw new ArgumentOutOfRangeException(nameof(code));
        _culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = _culture;
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = _culture;
        LanguageChanged?.Invoke();
    }

    internal static string T(string source, params object?[] arguments)
    {
        string translated = Resources.GetString(Key(source), _culture) ?? source;
        return arguments.Length == 0 ? translated : string.Format(_culture, translated, arguments);
    }

    // Keep the source captions so switching language never translates translated
    // text or replaces editable profile data. Weak keys do not keep forms alive.
    internal static void TranslateTree(Control root)
    {
        var source = Sources.GetValue(root, _ => new Captions(Known(root.Text), Known(root.AccessibleName), Known(root.AccessibleDescription)));
        if (source.Text != null && root is not TextBoxBase and not ComboBox) root.Text = T(source.Text);
        if (source.Name != null) root.AccessibleName = T(source.Name);
        if (source.Description != null) root.AccessibleDescription = T(source.Description);
        if (root is ToolStrip strip)
            foreach (ToolStripItem item in strip.Items) TranslateItem(item);
        foreach (Control child in root.Controls) TranslateTree(child);
    }

    internal static void TranslateItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items) TranslateItem(item);
    }

    private static void TranslateItem(ToolStripItem item)
    {
        var source = Sources.GetValue(item, _ => new Captions(Known(item.Text), null, null));
        if (source.Text != null) item.Text = T(source.Text);
        if (item is ToolStripDropDownItem dropDown) TranslateItems(dropDown.DropDownItems);
    }

    private static string? Known(string? value) => !string.IsNullOrEmpty(value) &&
        Resources.GetString(Key(value), CultureInfo.GetCultureInfo("en")) != null ? value : null;

    // Resource compilation treats names case-insensitively; keep this heading and
    // its sentence-case accessible name distinct without changing their captions.
    private static string Key(string source) => source == "Preset profile" ? "PresetProfileAccessibility" : source;

    private sealed record Captions(string? Text, string? Name, string? Description);
}
