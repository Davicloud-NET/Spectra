using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// A brush that slides open along a straight line and closes again: a door, a
/// gate, a hatch.
/// </summary>
// The node is authored closed. movedir is in the node's own axes, so a rotated
// door slides along its rotated axis. A zero or unreadable movedir is refused
// and up is used.
// Open while open or opening does nothing and does not restart the wait. Close
// while opening turns the door round where it is, so the way back takes as
// long as the way out did. Open while closing does the same.
// A door that would squeeze the player turns round too, and fires what
// turning round fires. One that has not left its rest yet waits there.
// Travel and wait are counted in ticks.
// OnOpen and OnClose carry the activator of the input that caused them. The
// door is its own activator for both arrivals, for closing after its wait and
// for turning round on the player.
[SpectraEntity("func_door", Display = "Door", Group = "Movers", Placement = EntityPlacement.Brush)]
public sealed partial class FuncDoor : Entity
{
    /// <summary>Fired when the door starts to open.</summary>
    [EntityOutput]
    public const string OnOpen = nameof(OnOpen);

    /// <summary>Fired when the door starts to close.</summary>
    [EntityOutput]
    public const string OnClose = nameof(OnClose);

    /// <summary>Fired when the door arrives at its open pose.</summary>
    [EntityOutput]
    public const string OnFullyOpen = nameof(OnFullyOpen);

    /// <summary>Fired when the door arrives back at its closed pose.</summary>
    [EntityOutput]
    public const string OnFullyClosed = nameof(OnFullyClosed);

    private readonly LinearMover _mover;

    // Ticks until an open door closes itself. Zero while it is not waiting.
    private int _waitTicksLeft;

    /// <summary>Builds a door that is not in a level yet.</summary>
    public FuncDoor() => _mover = new LinearMover(this);

    /// <summary>The way the door slides to open, in the node's own axes.</summary>
    [Keyvalue(
        "movedir",
        Display = "Move direction",
        Tooltip = "The way the door slides to open, in its own axes.",
        Default = "0 1 0")]
    public Vector3 MoveDirection { get; set; } = Vector3.UnitY;

    /// <summary>How far the door slides, or zero to take it from the door's size.</summary>
    [Keyvalue(
        "distance",
        Display = "Distance",
        Tooltip = "How far the door slides. 0 uses its own size along the direction, less the lip.",
        Default = "0",
        Min = 0f)]
    public float Distance { get; set; }

    /// <summary>How much of the door stays in view when the distance comes from its size.</summary>
    [Keyvalue(
        "lip",
        Display = "Lip",
        Tooltip = "How much of the door is left showing when the distance comes from its size.",
        Default = "0.05")]
    public float Lip { get; set; } = 0.05f;

    /// <summary>Units a second, raised to <see cref="LinearMover.MinimumSpeed"/> when lower.</summary>
    [Keyvalue(
        "speed",
        Display = "Speed",
        Tooltip = "Units a second.",
        Default = "2",
        Min = LinearMover.MinimumSpeed)]
    public float Speed { get; set; } = 2f;

    /// <summary>Seconds the door stays open before it closes itself. Below zero it stays open.</summary>
    [Keyvalue(
        "wait",
        Display = "Wait",
        Tooltip = "Seconds the door stays open before it closes by itself. -1 keeps it open.",
        Default = "4")]
    public float Wait { get; set; } = 4f;

    /// <summary>Whether the door is open when the level starts.</summary>
    // It then stays open until something closes it: the wait runs from an arrival.
    [Keyvalue(
        "startopen",
        Display = "Start open",
        Tooltip = "The door is open when the level starts, and stays open until something closes it.",
        Default = "0")]
    public bool StartOpen { get; set; }

    /// <summary>Whether the door is at its open pose.</summary>
    public bool IsFullyOpen => _mover.IsFullyOpen;

    /// <summary>Whether the door is at its closed pose.</summary>
    public bool IsFullyClosed => _mover.IsFullyClosed;

    /// <summary>How many ticks the door takes to open or to close.</summary>
    public int TravelTicks => _mover.TravelTicks;

    /// <summary>How far open the door is, from 0 to <see cref="TravelTicks"/>.</summary>
    public int TicksTravelled => _mover.TicksTravelled;

    /// <inheritdoc/>
    protected override void OnSpawn()
    {
        if (!LinearMover.TryNormalize(MoveDirection, out Vector3 direction))
        {
            Data.TryGetValue("movedir", out string authored);
            RefuseKeyvalue("movedir", authored);
        }

        IReadOnlyList<SceneNode> brushes = CollectOwnedBrushes();
        float distance = Distance > 0f
            ? Distance
            : LinearMover.ExtentAlong(Node, brushes, direction) - Lip;

        _mover.SetTravel(direction, distance, Speed);
        _mover.SetBrushes(brushes);

        if (StartOpen)
            _mover.PlaceAt(_mover.TravelTicks);
    }

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        if (IsFullyOpen)
            state.Headline("state", "open");
        else if (IsFullyClosed)
            state.Headline("state", "closed");
        else
            state.Headline(
                _mover.TargetTicks > TicksTravelled ? "opening" : "closing",
                HeadlineText.TicksOf(TicksTravelled, TravelTicks));

        state.Add("open", IsFullyOpen);
        state.Add("closed", IsFullyClosed);
        state.Add("ticks travelled", TicksTravelled);
        state.Add("travel ticks", TravelTicks);
        state.Add("ticks until it closes", _waitTicksLeft);
    }

    /// <inheritdoc/>
    protected override void OnTick()
    {
        if (_waitTicksLeft > 0)
        {
            if (--_waitTicksLeft > 0)
                return;

            _mover.MoveTo(0);
            FireOnClose();
        }

        switch (_mover.Advance())
        {
            case LinearMoverStep.ArrivedOpen:
                FireOnFullyOpen();
                _waitTicksLeft = WaitTicks();
                SetTicking(_waitTicksLeft > 0);
                break;

            case LinearMoverStep.ArrivedClosed:
                FireOnFullyClosed();
                SetTicking(false);
                break;

            case LinearMoverStep.Stopped:
                SetTicking(false);
                break;

            case LinearMoverStep.Blocked:
                // Blocked before it left its rest, it has no way back to
                // take. It tries again next tick.
                if (_mover.IsFullyOpen || _mover.IsFullyClosed)
                    break;

                if (IsHeadedOpen)
                    StartClosing(null);
                else
                    StartOpening(null);
                break;
        }
    }

    [EntityInput("Open")]
    private void Open(ref EntityInputContext context) => StartOpening(context.Activator);

    [EntityInput("Close")]
    private void Close(ref EntityInputContext context) => StartClosing(context.Activator);

    // Closes a door that is open or opening, opens any other.
    [EntityInput("Toggle")]
    private void Toggle(ref EntityInputContext context)
    {
        if (IsHeadedOpen)
            StartClosing(context.Activator);
        else
            StartOpening(context.Activator);
    }

    // Open, or on its way there.
    private bool IsHeadedOpen => _mover.TargetTicks == _mover.TravelTicks;

    private void StartOpening(Entity? activator)
    {
        if (IsHeadedOpen)
            return;

        _mover.MoveTo(_mover.TravelTicks);
        SetTicking(true);
        FireOnOpen(activator);
    }

    private void StartClosing(Entity? activator)
    {
        if (_mover.TargetTicks == 0)
            return;

        _waitTicksLeft = 0;
        _mover.MoveTo(0);
        SetTicking(true);
        FireOnClose(activator);
    }

    // Zero means stay open. A wait of no seconds still takes one tick: the
    // door arrived on this one.
    private int WaitTicks() =>
        Wait >= 0f ? LinearMover.WholeTicks(Wait / (double)World.FixedDeltaTime) : 0;
}
