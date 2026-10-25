using System.ComponentModel;

namespace ecomode;

internal enum EcoSwitchState { Off, On, RestorePending }

internal sealed class EcoModeSwitch : UtilitySwitch
{
    private EcoSwitchState _state;
    protected override bool IsCompact => false;
    protected override bool IsSwitchOn => State == EcoSwitchState.On;
    protected override bool IsRecoveryPending => State == EcoSwitchState.RestorePending;

    internal EcoModeSwitch()
    {
        // The form commits the visible state after apply/restore completes.
        AutoCheck = false;
        ThreeState = true;
        AccessibleName = L.T("Eco Mode");
        State = EcoSwitchState.Off;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal EcoSwitchState State
    {
        get => _state;
        set
        {
            _state = value;
            CheckState = value switch
            {
                EcoSwitchState.On => CheckState.Checked,
                EcoSwitchState.RestorePending => CheckState.Indeterminate,
                _ => CheckState.Unchecked
            };
            Text = Caption;
            AccessibleDescription = value switch
            {
                EcoSwitchState.On => L.T("On. Activate to turn Eco Mode off and restore original settings."),
                EcoSwitchState.RestorePending => L.T("Restoration pending. Activate to retry restoring original settings."),
                _ => L.T("Off. Activate to enable Eco Mode.")
            };
            Invalidate();
        }
    }
}
