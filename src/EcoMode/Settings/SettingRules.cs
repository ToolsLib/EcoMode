using Microsoft.Win32;
using System.Text.Json;

namespace ecomode;

internal static class SettingRules
{
    internal const string GpuPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    internal static readonly Guid Epp = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    internal static readonly Guid Boost = new("be337238-0d82-4146-a960-4f3749d470c7");

    public static void ValidateTarget(SettingTarget target)
    {
        bool allowed = target.Kind switch
        {
            SettingKind.UserRegistry => Same(target.Path, GpuPath) &&
                !string.IsNullOrWhiteSpace(target.Name) && Path.IsPathFullyQualified(target.Name) &&
                Same(Path.GetExtension(target.Name), ".exe"),
            SettingKind.MachineRegistry =>
                Same(target.Path, @"SOFTWARE\Policies\Microsoft\Edge") &&
                (Same(target.Name, "HardwareAccelerationModeEnabled") || Same(target.Name, "EfficiencyModeEnabled")) ||
                Same(target.Path, @"SOFTWARE\Policies\Google\Chrome") &&
                (Same(target.Name, "HardwareAccelerationModeEnabled") || Same(target.Name, "BatterySaverModeAvailability")),
            SettingKind.Power => Guid.TryParseExact(target.Path, "D", out var scheme) && scheme != Guid.Empty &&
                Guid.TryParseExact(target.Name, "D", out var setting) && (setting == Epp || setting == Boost),
            _ => false
        };
        if (!allowed) throw new InvalidDataException(L.T("The restore snapshot contains an unsupported setting destination."));
    }

    public static void Validate(SettingTarget target, SettingValue value)
    {
        ValidateTarget(target);
        if (!value.Exists)
        {
            if (target.Kind == SettingKind.Power || value.Kind != RegistryValueKind.None || value.Data.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException(L.T("Invalid missing-value entry in the restore snapshot."));
            return;
        }
        bool valid;
        if (target.Kind == SettingKind.Power)
        {
            uint maximum = Guid.Parse(target.Name) == Epp ? 100u : 6u;
            valid = value.Kind == RegistryValueKind.None && value.Data.ValueKind == JsonValueKind.Array &&
                value.Data.GetArrayLength() == 2 && value.Data.EnumerateArray().All(e =>
                    e.ValueKind == JsonValueKind.Number && e.TryGetUInt32(out uint n) && n <= maximum);
        }
        else if (target.Kind == SettingKind.MachineRegistry)
        {
            int maximum = Same(target.Name, "BatterySaverModeAvailability") ? 2 : 1;
            valid = value.Kind == RegistryValueKind.DWord && value.Data.ValueKind == JsonValueKind.Number &&
                value.Data.TryGetInt32(out int n) && n >= 0 && n <= maximum;
        }
        else
        {
            valid = value.Kind == RegistryValueKind.String && value.Data.ValueKind == JsonValueKind.String &&
                value.Data.GetString()!.Length <= 32767;
        }
        if (!valid) throw new InvalidDataException(L.T("The restore snapshot contains an invalid setting value or type."));
    }

    private static bool Same(string? left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

internal static class GpuPreferences
{
    public static string WithPreference(string? original, int preference)
    {
        var fields = (original ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(field => !string.Equals(field.Split('=', 2)[0].Trim(), "GpuPreference", StringComparison.OrdinalIgnoreCase));
        return string.Join(';', fields.Append($"GpuPreference={preference}")) + ";";
    }
}
