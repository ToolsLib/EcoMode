namespace ecomode;

internal sealed record ProfilePreset(string Id, string NameSource, string DescriptionSource)
{
    internal string Name => L.T(NameSource);
    internal string Description => L.T(DescriptionSource);
    public override string ToString() => Name;

    internal AppState CreateState()
    {
        using var stream = typeof(ProfilePreset).Assembly.GetManifestResourceStream($"ecomode.profiles.{Id}.json")
            ?? throw new InvalidOperationException(L.T("The {0} preset is missing.", Name));
        using var reader = new StreamReader(stream);
        return AppState.Parse(reader.ReadToEnd());
    }
}

internal static class ProfilePresets
{
    internal static IReadOnlyList<ProfilePreset> All { get; } = Array.AsReadOnly(new[]
    {
        new ProfilePreset("balanced", "Balanced",
            "Background EcoQoS and power-saving browser GPU preferences. Keeps your power plan and browser policies."),
        new ProfilePreset("battery-saver", "Battery Saver",
            "Enables every saving option, including reduced CPU boost and browser acceleration. May slow demanding tasks."),
        new ProfilePreset("gaming", "Gaming",
            "Background EcoQoS; keeps power, browser and GPU settings. Add your game names to the bypass list to protect background games."),
        new ProfilePreset("development", "Development",
            "Background EcoQoS with common build tools and runtimes bypassed. Add any other build or container processes you use."),
        new ProfilePreset("production", "Creative Work",
            "Background EcoQoS with common editors, renderers and encoders bypassed. Keeps power, browser and GPU settings."),
        new ProfilePreset("video-calls", "Video Calls",
            "Background EcoQoS with common meeting apps and browsers bypassed. Keeps power, browser and GPU settings.")
    });

    internal static ProfilePreset Get(string id) => All.FirstOrDefault(preset => preset.Id == id)
        ?? throw new ArgumentException(L.T("Unknown profile preset."), nameof(id));
}
