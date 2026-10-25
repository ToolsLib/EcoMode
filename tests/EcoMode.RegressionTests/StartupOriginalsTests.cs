using ecomode;
using Microsoft.Win32;

internal static partial class Program
{
    private static void RunStartupOriginalsTests()
    {
        Run("startup preserves existing originals with and without pending recovery", StartupPreservesOriginals);
        Run("elevated startup fills a missing machine backup without replacing user originals", StartupCapturesMachineScope);
        Run("failed startup journal saves block Eco ON and can be retried", StartupJournalFailure);
        Run("startup refuses to overwrite a corrupt existing backup", StartupCorruptOriginals);
    }

    private static void StartupPreservesOriginals()
    {
        foreach (bool pending in new[] { false, true })
        {
            var (system, manager, path) = Setup(Target("setting"));
            manager.Capture(Profile, false);
            if (pending) manager.ApplyEco(Profile, false);
            else system.Values["setting"] = SettingValue.From(99, RegistryValueKind.DWord);
            string originals = File.ReadAllText(path);
            int writes = system.Writes;
            var reopened = new OriginalSettingsManager(system, _ => { }, path);
            using var form = new TestMainForm(false, reopened, SwitchConfig());
            Check(File.ReadAllText(path) == originals && system.Writes == writes && reopened.HasPendingChanges == pending,
                "Startup replaced the saved baseline, cleared recovery, or changed system settings.");
            Check(Field<EcoModeSwitch>(form, "ecoSwitch").State == (pending ? EcoSwitchState.RestorePending : EcoSwitchState.Off),
                "Startup reported an incorrect state for existing originals.");
            Check(reopened.Restore(false, restoreAll: true) && system.Values["setting"].Data.GetInt32() == 37,
                "Startup lost the original setting value.");
        }
    }

    private static void StartupCapturesMachineScope()
    {
        var system = new FakeSystem { Targets = new() { Target("user"), Target("machine", SettingKind.MachineRegistry) } };
        system.Values["user"] = SettingValue.From(81, RegistryValueKind.DWord);
        system.Values["machine"] = SettingValue.From(37, RegistryValueKind.DWord);
        var userStore = new MemoryStore();
        var machineStore = new MemoryStore();
        using (var standard = new TestMainForm(false, new SettingsCoordinator(system, _ => { }, userStore, machineStore), SwitchConfig()))
            Check(userStore.Exists && !machineStore.Exists && system.Writes == 0, "Standard startup did not capture just the user scope.");
        string userOriginals = userStore.Read();
        system.Values["user"] = SettingValue.From(99, RegistryValueKind.DWord);

        // Simulate a full/read-only shared journal on the first elevated launch.
        machineStore.FailWrites = true;
        using (var failed = new TestMainForm(true, new SettingsCoordinator(system, _ => { }, userStore, machineStore), SwitchConfig()))
            Check(Field<Label>(failed, "lblOriginalStatus").Text == "ORIGINALS  /  SAVE FAILED" && !machineStore.Exists,
                "A missing machine backup was mistaken for a complete saved baseline.");
        machineStore.FailWrites = false;
        var elevated = new SettingsCoordinator(system, _ => { }, userStore, machineStore);
        using var form = new TestMainForm(true, elevated, SwitchConfig());
        Check(userStore.Read() == userOriginals && machineStore.Exists && system.Writes == 0,
            "Elevated startup replaced user originals or failed to retry the missing machine backup.");
        string machineOriginals = machineStore.Read();
        elevated.EnsureCaptured(Profile, true);
        Check(userStore.Read() == userOriginals && machineStore.Read() == machineOriginals,
            "Repeated startup rewrote an existing baseline.");
        Check(elevated.Restore(true, restoreAll: true) && system.Values["user"].Data.GetInt32() == 81 &&
            system.Values["machine"].Data.GetInt32() == 37, "Startup did not preserve both scopes' original values.");
    }

    private static void StartupJournalFailure()
    {
        var system = new FakeSystem { Targets = new() { Target("setting") } };
        system.Values["setting"] = SettingValue.From(37, RegistryValueKind.DWord);
        var store = new MemoryStore { FailWrites = true };
        var manager = new OriginalSettingsManager(system, _ => { }, store);
        using var form = new TestMainForm(false, manager, SwitchConfig());
        ClickEcoSwitch(form);
        Check(!manager.HasSnapshot && !manager.HasPendingChanges && system.Writes == 0 &&
            Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.Off &&
            Field<Label>(form, "lblOriginalStatus").Text == "ORIGINALS  /  SAVE FAILED",
            "A failed journal save enabled Eco Mode, changed settings, or hid the backup failure.");
        store.FailWrites = false;
        ClickEcoSwitch(form);
        Check(manager.HasPendingChanges && Field<EcoModeSwitch>(form, "ecoSwitch").State == EcoSwitchState.On,
            "Eco ON could not retry the failed startup backup.");
        ClickEcoSwitch(form);
        Check(system.Values["setting"].Data.GetInt32() == 37 && !manager.HasPendingChanges,
            "The retried startup backup could not restore originals.");
    }

    private static void StartupCorruptOriginals()
    {
        var (system, manager, path) = Setup(Target("setting"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{broken backup");
        using var form = new TestMainForm(false, manager, SwitchConfig());
        Check(Field<Label>(form, "lblOriginalStatus").Text == "ORIGINALS  /  COULD NOT READ" &&
            File.ReadAllText(path) == "{broken backup" && system.Writes == 0 && !Field<bool>(form, "_ecoEnabled"),
            "Startup overwrote corrupt originals, hid the failure, or changed system settings.");
    }
}
