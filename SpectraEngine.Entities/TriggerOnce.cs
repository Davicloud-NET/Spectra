using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities;

/// <summary>
/// A volume that fires the first time the player walks in, then switches
/// itself off.
/// </summary>
// Switching off is what Disable does. The node stays in the level, and Enable
// arms the trigger for one more touch.
// OnStartTouch and OnTrigger fire together, in that order.
// The trigger is its own activator: the player is not an entity, so
// !activator on one of its wires names the trigger.
[SpectraEntity("trigger_once", Group = "Triggers", Placement = EntityPlacement.Volume)]
public sealed partial class TriggerOnce : Entity, ITouchListener
{
    /// <summary>Fired when the player comes inside.</summary>
    [EntityOutput]
    public const string OnStartTouch = nameof(OnStartTouch);

    /// <summary>Fired with <see cref="OnStartTouch"/>, straight after it.</summary>
    [EntityOutput]
    public const string OnTrigger = nameof(OnTrigger);

    private readonly TriggerState _state = new();

    /// <summary>Whether the trigger starts switched off.</summary>
    [Keyvalue(
        "startdisabled",
        Display = "Start disabled",
        Tooltip = "The trigger senses nothing until something sends it Enable.",
        Default = "0")]
    public bool StartDisabled { get; set; }

    /// <summary>Whether the trigger is armed.</summary>
    public bool IsEnabled => _state.IsEnabled;

    /// <summary>How many times the trigger has fired since it spawned.</summary>
    public int TriggerCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => _state.Spawn(this, CollectOwnedBrushes(), StartDisabled);

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Headline("triggered", HeadlineText.YesNo(TriggerCount > 0));
        state.Add("enabled", IsEnabled);
        state.Add("triggers", TriggerCount);
    }

    void ITouchListener.OnTouchStarted(in TouchVisitor visitor)
    {
        _state.Started();
        TriggerCount++;
        FireOnStartTouch();
        FireOnTrigger();

        // Ends this touch too, which OnTouchEnded hears before this returns.
        _state.SetEnabled(this, false);
    }

    void ITouchListener.OnTouchEnded(in TouchVisitor visitor) => _state.Ended();

    [EntityInput("Enable")]
    private void Enable(ref EntityInputContext context) => _state.SetEnabled(this, true);

    [EntityInput("Disable")]
    private void Disable(ref EntityInputContext context) => _state.SetEnabled(this, false);

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context) => _state.Toggle(this);
}
