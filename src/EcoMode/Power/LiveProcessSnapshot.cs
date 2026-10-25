using ecomode.Interop;

namespace ecomode;

internal enum LiveProcessState { Managed, RestorePending, Foreground, Excluded, Protected, NotManaged, Unavailable }
internal enum LiveEcoQos { On, Off, SystemManaged, Unavailable }

internal sealed record LiveProcessSnapshot(uint Id, string Name, LiveProcessState State,
    Win32Api.PriorityClass? Priority, LiveEcoQos EcoQos, bool UserExcluded, bool Locked);
