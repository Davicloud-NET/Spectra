using SpectraEngine.Core.Entities;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// A brush that goes in when it is pressed and comes back out by itself: a
/// wall button, a floor plate, a switch.
/// </summary>
// The node is authored out. movedir is the way in, in the node's own axes, so
// a rotated button goes in along its rotated axis. A zero or unreadable
// movedir is refused and the default is used.
// Use is what the player's use key sends. Press is the same thing for a wire.
// The button is pressed from the press until it is back out and at rest. A
// press while pressed does nothing: it neither turns the button round nor
// restarts the wait.
// Travel and wait are counted in ticks.
// OnPressed carries the activator of the input that pressed the button. The
// player is not an entity, so a use from the player has the button itself. The
// button is its own activator for OnIn and OnOut.
[SpectraEntity("func_button", Display = "Button", Group = "Movers", Placement = EntityPlacement.Brush)]
public sealed partial class FuncButton : Entity
{
    /// <summary>Fired when a press is taken and the button starts to go in.</summary>
    [EntityOutput]
    public const string OnPressed = nameof(OnPressed);

    /// <summary>Fired when the button arrives all the way in.</summary>
    [EntityOutput]
    public const string OnIn = nameof(OnIn);

    /// <summary>Fired when the button arrives back out.</summary>
    [EntityOutput]
    public const string OnOut = nameof(OnOut);

    private readonly LinearMover _mover;

    // Ticks until a button that is in comes back out. Zero while it is not waiting.
    private int _waitTicksLeft;

    /// <summary>Builds a button that is not in a level yet.</summary>
    public FuncButton() => _mover = new LinearMover(this);

    /// <summary>The way the button goes in, in the node's own axes.</summary>
    [Keyvalue(
        "movedir",
        Display = "Move direction",
        Tooltip = "The way the button goes in, in its own axes.",
        Default = "0 0 -1")]
    public Vector3 MoveDirection { get; set; } = DefaultDirection;

    /// <summary>How far the button goes in, or zero to take it from the button's size.</summary>
    [Keyvalue(
        "distance",
        Display = "Distance",
        Tooltip = "How far the button goes in. 0 uses its own size along the direction, less the lip.",
        Default = "0",
        Min = 0f)]
    public float Distance { get; set; }

    /// <summary>How much of the button stays out when the distance comes from its size.</summary>
    [Keyvalue(
        "lip",
        Display = "Lip",
        Tooltip = "How much of the button is left standing out when the distance comes from its size.",
        Default = "0.02")]
    public float Lip { get; set; } = 0.02f;

    /// <summary>Units a second, raised to <see cref="LinearMover.MinimumSpeed"/> when lower.</summary>
    [Keyvalue(
        "speed",
        Display = "Speed",
        Tooltip = "Units a second.",
        Default = "1",
        Min = LinearMover.MinimumSpeed)]
    public float Speed { get; set; } = 1f;

    /// <summary>Seconds the button stays in before it comes back out. Below zero it stays in.</summary>
    [Keyvalue(
        "wait",
        Display = "Wait",
        Tooltip = "Seconds the button stays in before it comes back out. -1 keeps it in.",
        Default = "1")]
    public float Wait { get; set; } = 1f;

    /// <summary>Whether the button is all the way in.</summary>
    public bool IsIn => _mover.IsFullyOpen;

    /// <summary>Whether the button is all the way out.</summary>
    public bool IsOut => _mover.IsFullyClosed;

    /// <summary>Whether the button has been pressed and is not back out and at rest.</summary>
    public bool IsPressed => !_mover.IsFullyClosed || _mover.IsMoving;

    /// <summary>How many ticks the button takes to go in or to come back out.</summary>
    public int TravelTicks => _mover.TravelTicks;

    /// <summary>How far in the button is, from 0 to <see cref="TravelTicks"/>.</summary>
    public int TicksTravelled => _mover.TicksTravelled;

    private static Vector3 DefaultDirection => new(0f, 0f, -1f);

    /// <inheritdoc/>
    protected override void OnSpawn()
    {
        if (!LinearMover.TryNormalize(MoveDirection, out Vector3 direction))
        {
            Data.TryGetValue("movedir", out string authored);
            RefuseKeyvalue("movedir", authored);
            direction = DefaultDirection;
        }

        float distance = Distance > 0f
            ? Distance
            : LinearMover.ExtentAlong(Node, CollectOwnedBrushes(), direction) - Lip;

        _mover.SetTravel(direction, distance, Speed);
    }

    /// <inheritdoc/>
    public override void DescribeState(EntityStateWriter state)
    {
        state.Add("pressed", IsPressed);
        state.Add("ticks travelled", TicksTravelled);
        state.Add("travel ticks", TravelTicks);
        state.Add("ticks until it comes out", _waitTicksLeft);
    }

    /// <inheritdoc/>
    protected override void OnTick()
    {
        if (_waitTicksLeft > 0)
        {
            if (--_waitTicksLeft > 0)
                return;

            _mover.MoveTo(0);
        }

        switch (_mover.Advance())
        {
            case LinearMoverStep.ArrivedOpen:
                FireOnIn();
                _waitTicksLeft = WaitTicks();
                SetTicking(_waitTicksLeft > 0);
                break;

            case LinearMoverStep.ArrivedClosed:
                FireOnOut();
                SetTicking(false);
                break;

            case LinearMoverStep.Stopped:
                SetTicking(false);
                break;
        }
    }

    [EntityInput("Use")]
    private void Use(ref EntityInputContext context) => PressIn(context.Activator);

    [EntityInput("Press")]
    private void Press(ref EntityInputContext context) => PressIn(context.Activator);

    private void PressIn(Entity? activator)
    {
        if (IsPressed)
            return;

        _mover.MoveTo(_mover.TravelTicks);
        SetTicking(true);
        FireOnPressed(activator);
    }

    // Zero means stay in. A wait of no seconds still takes one tick: the
    // button arrived on this one.
    private int WaitTicks() =>
        Wait >= 0f ? LinearMover.WholeTicks(Wait / (double)World.FixedDeltaTime) : 0;
}
