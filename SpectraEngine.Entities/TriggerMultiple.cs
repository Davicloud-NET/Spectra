using SpectraEngine.Core.Entities;
using System;

namespace SpectraEngine.Entities;

/// <summary>
/// A volume that fires when the player walks in, keeps firing while the player
/// stays, and fires when the player leaves.
/// </summary>
// OnTrigger fires on entry and then every wait seconds while touched. The
// wait is counted in ticks. A wait of zero fires it on entry only.
// Disable while touched ends the touch, so OnEndTouch fires then. Enable with
// the player still inside starts a new touch on the next tick.
// The trigger is its own activator: the player is not an entity, so
// !activator on one of its wires names the trigger.
[SpectraEntity("trigger_multiple", Group = "Triggers", Placement = EntityPlacement.Volume)]
public sealed partial class TriggerMultiple : Entity, ITouchListener
{
    /// <summary>Fired when the player comes inside.</summary>
    [EntityOutput]
    public const string OnStartTouch = nameof(OnStartTouch);

    /// <summary>Fired when the player leaves, or the trigger is disabled while touched.</summary>
    [EntityOutput]
    public const string OnEndTouch = nameof(OnEndTouch);

    /// <summary>Fired on entry, then every <see cref="Wait"/> seconds while touched.</summary>
    [EntityOutput]
    public const string OnTrigger = nameof(OnTrigger);

    private readonly TriggerState _state = new();
    private float _wait = 1f;
    private int _ticksToRefire;

    /// <summary>Whether the trigger starts switched off.</summary>
    [Keyvalue(
        "startdisabled",
        Display = "Start disabled",
        Tooltip = "The trigger senses nothing until something sends it Enable.",
        Default = "0")]
    public bool StartDisabled { get; set; }

    /// <summary>Seconds between fires while touched. Zero fires on entry only.</summary>
    [Keyvalue(
        "wait",
        Display = "Refire time",
        Tooltip = "Seconds between OnTrigger fires while the player stays inside. 0 fires on entry only.",
        Default = "1",
        Min = 0f)]
    public float Wait
    {
        get => _wait;
        // NaN fails the comparison and lands on zero too.
        set => _wait = value > 0f ? value : 0f;
    }

    /// <summary>Whether the trigger is sensing.</summary>
    public bool IsEnabled => _state.IsEnabled;

    /// <summary>Whether the player is inside.</summary>
    public bool IsTouched => _state.IsTouched;

    /// <summary>How many times <see cref="OnTrigger"/> has fired since the trigger spawned.</summary>
    public int TriggerCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn() => _state.Spawn(this, CollectOwnedBrushes(), StartDisabled);

    /// <inheritdoc/>
    // Ticks only while touched with a wait above zero.
    protected override void OnTick()
    {
        if (--_ticksToRefire == 0)
            Trigger();
    }

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        if (IsEnabled)
            state.Headline("touched", HeadlineText.YesNo(IsTouched));
        else
            state.Headline("state", "disabled");

        state.Add("enabled", IsEnabled);
        state.Add("touched", IsTouched);
        state.Add("triggers", TriggerCount);
    }

    void ITouchListener.OnTouchStarted(in TouchVisitor visitor)
    {
        _state.Started();
        FireOnStartTouch();
        Trigger();
    }

    void ITouchListener.OnTouchEnded(in TouchVisitor visitor)
    {
        _state.Ended();
        if (!_state.IsTouched)
            SetTicking(false);

        FireOnEndTouch();
    }

    [EntityInput("Enable")]
    private void Enable(ref EntityInputContext context) => _state.SetEnabled(this, true);

    [EntityInput("Disable")]
    private void Disable(ref EntityInputContext context) => _state.SetEnabled(this, false);

    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context) => _state.Toggle(this);

    private void Trigger()
    {
        TriggerCount++;
        FireOnTrigger();

        _ticksToRefire = RefireTicks();
        SetTicking(_ticksToRefire > 0);
    }

    // At least one tick, so a wait shorter than a tick still refires.
    private int RefireTicks() =>
        Wait > 0f ? Math.Max(1, (int)MathF.Round(Wait / World.FixedDeltaTime)) : 0;
}
