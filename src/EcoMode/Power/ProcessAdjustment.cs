using ecomode.Interop;

namespace ecomode;

internal interface IProcessSettings
{
    Win32Api.PriorityClass ReadPriority();
    Win32Api.PROCESS_POWER_THROTTLING_STATE ReadThrottling();
    void WritePriority(Win32Api.PriorityClass priority);
    void WriteThrottling(Win32Api.PROCESS_POWER_THROTTLING_STATE state);
}

// Tracks successful writes separately: access denied during an attempted priority
// change must not create a priority restore obligation that can never succeed.
internal sealed class ProcessAdjustment
{
    private const Win32Api.ProcessorPowerThrottlingFlags Speed = Win32Api.ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
    private readonly IProcessSettings _settings;
    private readonly Win32Api.PROCESS_POWER_THROTTLING_STATE _originalThrottling;
    internal Win32Api.PriorityClass OriginalPriority { get; }
    private bool _priorityChanged;
    private bool _throttlingChanged;
    internal bool HasChanges => _priorityChanged || _throttlingChanged;

    internal ProcessAdjustment(IProcessSettings settings)
    {
        _settings = settings;
        OriginalPriority = settings.ReadPriority();
        _originalThrottling = settings.ReadThrottling();
    }

    internal void Apply(bool enable)
    {
        var current = _settings.ReadThrottling();
        var priority = _settings.ReadPriority();
        if (SameSpeed(current, _originalThrottling)) _throttlingChanged = false;
        if (priority == OriginalPriority) _priorityChanged = false;

        var desired = current;
        desired.ControlMask |= Speed;
        desired.StateMask = enable ? desired.StateMask | Speed : desired.StateMask & ~Speed;
        if (!SameSpeed(current, desired))
        {
            _settings.WriteThrottling(desired);
            _throttlingChanged = !SameSpeed(desired, _originalThrottling);
        }
        var desiredPriority = enable ? Win32Api.PriorityClass.IDLE_PRIORITY_CLASS : OriginalPriority;
        if (priority != desiredPriority)
        {
            _settings.WritePriority(desiredPriority);
            _priorityChanged = desiredPriority != OriginalPriority;
        }
    }

    internal bool Restore(Action<string, Exception> reportFailure)
    {
        if (_throttlingChanged)
        {
            try
            {
                var current = _settings.ReadThrottling();
                var desired = current;
                desired.ControlMask = (current.ControlMask & ~Speed) | (_originalThrottling.ControlMask & Speed);
                desired.StateMask = (current.StateMask & ~Speed) | (_originalThrottling.StateMask & Speed);
                if (!SameSpeed(current, desired)) _settings.WriteThrottling(desired);
                _throttlingChanged = false;
            }
            catch (Exception ex) { reportFailure("EcoQoS", ex); }
        }
        if (_priorityChanged)
        {
            try
            {
                if (_settings.ReadPriority() != OriginalPriority) _settings.WritePriority(OriginalPriority);
                _priorityChanged = false;
            }
            catch (Exception ex) { reportFailure("Priority", ex); }
        }
        return !HasChanges;
    }

    private static bool SameSpeed(Win32Api.PROCESS_POWER_THROTTLING_STATE left, Win32Api.PROCESS_POWER_THROTTLING_STATE right) =>
        (left.ControlMask & Speed) == (right.ControlMask & Speed) && (left.StateMask & Speed) == (right.StateMask & Speed);
}
