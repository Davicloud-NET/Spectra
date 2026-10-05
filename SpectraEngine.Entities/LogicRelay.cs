using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// Passes a trigger on: one input in, one output out, with an enabled bit so a
/// level can switch a whole branch of its wiring off.
/// </summary>
// Refires while pending: a second Trigger before the first one's wire delay
// has elapsed queues a second OnTrigger. The delay lives on the wire, not the
// relay, so the relay has no "pending" state and no CancelPending input.
[SpectraEntity("logic_relay", Group = "Logic", Placement = EntityPlacement.Abstract)]
public sealed partial class LogicRelay : Entity
{
    /// <summary>Fired every time an accepted <c>Trigger</c> passes through.</summary>
    [EntityOutput]
    public const string OnTrigger = nameof(OnTrigger);

    /// <summary>Whether the relay starts switched off.</summary>
    [Keyvalue(
        "startdisabled",
        Display = "Start disabled",
        Tooltip = "The relay ignores Trigger until something sends it Enable.",
        Default = "0")]
    public bool StartDisabled { get; set; }

    /// <summary>Whether the relay is currently passing triggers on.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>How many triggers this relay has passed on since it spawned.</summary>
    public int TriggerCount { get; private set; }

    /// <inheritdoc/>
    // Not in the keyvalue setter: keyvalues arrive in authored order.
    protected override void OnSpawn() => IsEnabled = !StartDisabled;

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        if (IsEnabled)
            state.Headline("fired", HeadlineText.Times(TriggerCount));
        else
            state.Headline("state", "disabled");

        state.Add("enabled", IsEnabled);
        state.Add("triggers", TriggerCount);
    }

    [EntityInput("Trigger")]
    private void Trigger(ref EntityInputContext context)
    {
        if (!IsEnabled)
            return;

        TriggerCount++;
        // The activator passes through unchanged.
        FireOnTrigger(context.Activator);
    }

    [EntityInput("Enable")]
    private void Enable(ref EntityInputContext context) => IsEnabled = true;

    [EntityInput("Disable")]
    private void Disable(ref EntityInputContext context) => IsEnabled = false;

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context) => IsEnabled = !IsEnabled;
}
