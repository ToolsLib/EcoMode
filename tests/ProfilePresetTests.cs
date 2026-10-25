using ecomode;
using System.Reflection;
using System.Text.Json;

internal static partial class Program
{
    private static void RunProfilePresetTests()
    {
        Run("all embedded presets validate, round-trip and create independent settings", PresetDefinitions);
        Run("workload presets preserve power, browser and GPU settings", PresetWorkloads);
        Run("loading presets updates the profile and UI without changing originals or system settings", PresetLoading);
        Run("active Eco mode and pending recovery prevent preset replacement", PresetRecoveryGuard);
        Run("failed preset saves preserve the runtime configuration and controls", PresetSaveFailure);
    }

    private static void PresetDefinitions()
    {
        Check(ProfilePresets.All.Count == 6 && ProfilePresets.All.Select(p => p.Id).Distinct().Count() == 6,
            "Expected six uniquely identified presets.");
        var assembly = typeof(AppState).Assembly;
        var embedded = assembly.GetManifestResourceNames().Where(name => name.StartsWith("ecomode.profiles.")).Order().ToArray();
        Check(embedded.SequenceEqual(ProfilePresets.All.Select(p => $"ecomode.profiles.{p.Id}.json").Order()),
            "The picker and embedded profile files disagree.");
        foreach (var preset in ProfilePresets.All)
        {
            var state = preset.CreateState();
            Check(state.IncludeEcoQos && state.BypassProcesses.Length != 0, $"{preset.Name} has no background management.");
            string path = Path.Combine(_root, preset.Id + ".json");
            state.Save(path);
            Check(JsonSerializer.Serialize(state) == JsonSerializer.Serialize(AppState.Load(path)), $"{preset.Name} did not round-trip.");
            state.BypassProcesses[0] = "changed";
            Check(preset.CreateState().BypassProcesses[0] != "changed", "Editing a preset changed its template.");
        }
        Throws(() => ProfilePresets.Get("../unknown"));
    }

    private static void PresetWorkloads()
    {
        foreach (string id in new[] { "gaming", "development", "production", "video-calls" })
        {
            var state = ProfilePresets.Get(id).CreateState();
            Check(!state.IncludePowerPlan && !state.IncludeEdge && !state.IncludeChrome && !state.IncludeGpuPref && state.GpuPrefApps.Length == 0,
                $"{id} would change performance-sensitive system settings.");
        }
        Check(ProfilePresets.Get("gaming").CreateState().BypassProcesses.Contains("obs64"), "Gaming should protect recording.");
        Check(ProfilePresets.Get("development").CreateState().BypassProcesses.Contains("dotnet"), "Development should protect background builds.");
        Check(ProfilePresets.Get("production").CreateState().BypassProcesses.Contains("blender"), "Creative Work should protect background rendering.");
        Check(ProfilePresets.Get("video-calls").CreateState().BypassProcesses.Contains("ms-teams"), "Video Calls should protect meetings.");
        var battery = ProfilePresets.Get("battery-saver").CreateState();
        Check(battery.IncludePowerPlan && battery.IncludeEdge && battery.IncludeChrome && battery.IncludeGpuPref && battery.IncludeEcoQos,
            "Battery Saver should enable all saving options.");
        var balanced = ProfilePresets.Get("balanced").CreateState();
        Check(!balanced.IncludePowerPlan && !balanced.IncludeEdge && !balanced.IncludeChrome && balanced.IncludeGpuPref,
            "Balanced should preserve power and browser policies.");
    }

    private static void PresetLoading()
    {
        string path = Path.Combine(_root, "preset-loading.json");
        new AppState().Save(path);
        var (system, manager, snapshotPath) = Setup(Target("gpu"));
        manager.Capture(Profile, false);
        string snapshot = File.ReadAllText(snapshotPath);
        using var form = new TestMainForm(false, manager, path);
        foreach (var preset in ProfilePresets.All)
        {
            form.LoadPreset(preset.Id);
            var expected = preset.CreateState();
            Check(JsonSerializer.Serialize(AppState.Load(path)) == JsonSerializer.Serialize(expected), "Preset selection was not saved.");
            Check(JsonSerializer.Serialize(Field<AppState>(form, "_state")) == JsonSerializer.Serialize(expected), "Runtime profile does not match the preset.");
            Check(Field<CheckBox>(form, "chkPowerPlan").Checked == expected.IncludePowerPlan &&
                Field<CheckBox>(form, "chkEdge").Checked == expected.IncludeEdge &&
                Field<CheckBox>(form, "chkChrome").Checked == expected.IncludeChrome &&
                Field<CheckBox>(form, "chkGpuPref").Checked == expected.IncludeGpuPref &&
                Field<CheckBox>(form, "chkEcoQos").Checked == expected.IncludeEcoQos, "Preset checkboxes were not refreshed.");
            Check(Field<TextBoxBase>(form, "txtEcoQosProcs").Lines.SequenceEqual(expected.BypassProcesses), "Bypass editor was not refreshed.");
            Check(Field<TextBoxBase>(form, "txtGpuApps").Text.ReplaceLineEndings(Environment.NewLine) == string.Join(Environment.NewLine, expected.GpuPrefApps), "GPU editor was not refreshed.");
            Check(expected.BypassProcesses.All(name => EnergyManager.BypassProcessList.Contains(name + ".exe", StringComparer.OrdinalIgnoreCase)),
                "Runtime bypass list was not refreshed.");
            Check(!Field<bool>(form, "_ecoEnabled") && !manager.HasPendingChanges && system.Writes == 0,
                "Loading a preset applied system changes.");
            Check(File.ReadAllText(snapshotPath) == snapshot, "Loading a preset replaced the originals.");
        }
        string saved = File.ReadAllText(path);
        Throws(() => form.LoadPreset("unknown"));
        Check(File.ReadAllText(path) == saved, "Unknown preset overwrote settings.");
    }

    private static void PresetRecoveryGuard()
    {
        string path = Path.Combine(_root, "preset-recovery.json");
        new AppState { BypassProcesses = new[] { "keep" } }.Save(path);
        var (_, manager, _) = Setup(Target("gpu"));
        using var form = new TestMainForm(false, manager, path);
        string saved = File.ReadAllText(path);
        foreach (string flag in new[] { "_ecoEnabled", "_ecoHookActive" })
        {
            var field = typeof(MainForm).GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(form, true);
            try { Throws(() => form.LoadPreset("gaming")); }
            finally { field.SetValue(form, false); }
        }
        using var child = Child();
        try
        {
            Toggle(child, true);
            Throws(() => form.LoadPreset("gaming"));
        }
        finally { EnergyManager.UnthrottleAllUserProcesses(); child.Kill(); child.WaitForExit(); }
        manager.Capture(Profile, false);
        manager.ApplyEco(Profile, false);
        Throws(() => form.LoadPreset("gaming"));
        Check(File.ReadAllText(path) == saved && Field<AppState>(form, "_state").BypassProcesses.Contains("keep"),
            "Blocked preset replacement changed the profile.");
        Check(manager.HasPendingChanges, "Blocked preset replacement discarded pending recovery.");
        Check(manager.Restore(false), "Test restore failed.");
        form.LoadPreset("gaming");
        Check(AppState.Load(path).BypassProcesses.Contains("steam"), "Preset remained blocked after recovery completed.");
    }

    private static void PresetSaveFailure()
    {
        string path = Path.Combine(_root, "preset-save-failure.json");
        new AppState { BypassProcesses = new[] { "keep" } }.Save(path);
        var (_, manager, _) = Setup();
        using var form = new TestMainForm(false, manager, path);
        File.Delete(path);
        Directory.CreateDirectory(path);
        Throws(() => form.LoadPreset("gaming"));
        Check(Field<AppState>(form, "_state").BypassProcesses.Contains("keep") &&
            Field<TextBoxBase>(form, "txtEcoQosProcs").Text == "keep" &&
            EnergyManager.BypassProcessList.Contains("keep.exe"), "A failed save changed the runtime configuration.");
    }
}
