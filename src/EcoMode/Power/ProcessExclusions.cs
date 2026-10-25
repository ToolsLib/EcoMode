using System.Collections.Frozen;
using ecomode.Interop;

namespace ecomode;

internal static class ProcessExclusions
{
    // Exact image names only; profile imports and user bypass edits cannot remove these.
    internal static IReadOnlySet<string> SecurityProcesses { get; } = new[]
    {
        // Malwarebytes: https://help.malwarebytes.com/hc/en-us/articles/31589222734875
        "Malwarebytes.exe", "MBAMService.exe", "MBVpnTunnelService.exe",
        // Microsoft Defender and Defender for Endpoint.
        "MsMpEng.exe", "MpDefenderCoreService.exe", "NisSrv.exe", "MpCmdRun.exe", "MpDlpService.exe",
        "MsSense.exe", "MsSenseS.exe", "SenseIR.exe", "SenseNdr.exe", "SenseTVM.exe",
        "SenseAadAuthenticator.exe", "SenseGPParser.exe", "SenseImdsCollector.exe", "SenseSampleUploader.exe",
        "SecurityHealthService.exe", "SecurityHealthSystray.exe",
        // Windows authentication and critical service hosts.
        "lsass.exe", "LsaIso.exe", "wininit.exe", "winlogon.exe", "services.exe"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    internal static bool CanManage(string name, uint protectionLevel) =>
        !SecurityProcesses.Contains(name) && protectionLevel == Win32Api.PROTECTION_LEVEL_NONE;
}
