using SpectraEngine.Core.Entities;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// A brush that slides along a straight line to wherever it is sent and rests
/// there: a platform, a lift, a piston.
/// </summary>
// The node is authored at position 0. Position 1 is distance along movedir,
// which is in the node's own axes. A zero or unreadable movedir is refused and
// up is used.
// Open heads for position 1 and Close for position 0, from wherever the brush
// is. Open while opening does nothing. SetPosition heads for a fraction of the
// way, rounded to a whole tick of travel.
// OnFullyOpen and OnFullyClosed fire on arriving at position 1 and 0, with the
// mover as their activator. Arriving anywhere between fires nothing.
[SpectraEntity("func_movelinear", Display = "Linear Mover", Group = "Movers", Placement = EntityPlacement.Brush)]
public sealed partial class FuncMoveLinear : Entity
{
    /// <summary>Fired when the brush arrives at position 1.</summary>
    [EntityOutput]
    public const string OnFullyOpen = nameof(OnFullyOpen);

    /// <summary>Fired when the brush arrives at position 0.</summary>
    [EntityOutput]
    public const string OnFullyClosed = nameof(OnFullyClosed);

    private readonly LinearMover _mover;

    /// <summary>Builds a mover that is not in a level yet.</summary>
    public FuncMoveLinear() => _mover = new LinearMover(this);

    /// <summary>The way the brush slides towards position 1, in the node's own axes.</summary>
    [Keyvalue(
        "movedir",
        Display = "Move direction",
        Tooltip = "The way the brush slides towards position 1, in its own axes.",
        Default = "0 1 0")]
    public Vector3 MoveDirection { get; set; } = Vector3.UnitY;

    /// <summary>How far position 1 is from position 0.</summary>
    [Keyvalue(
        "distance",
        Display = "Distance",
        Tooltip = "How far the brush slides from position 0 to position 1.",
        Default = "4",
        Min = 0f)]
    public float Distance { get; set; } = 4f;

    /// <summary>Units a second, raised to <see cref="LinearMover.MinimumSpeed"/> when lower.</summary>
    [Keyvalue(
        "speed",
        Display = "Speed",
        Tooltip = "Units a second.",
        Default = "2",
        Min = LinearMover.MinimumSpeed)]
    public float Speed { get; set; } = 2f;

    /// <summary>Where along the way the brush is when the level starts, from 0 to 1.</summary>
    [Keyvalue(
        "startposition",
        Display = "Start position",
        Tooltip = "Where along the way the brush is when the level starts, from 0 to 1.",
        Default = "0",
        Min = 0f,
        Max = 1f)]
    public float StartPosition { get; set; }

    /// <summary>How many ticks the whole way takes.</summary>
    public int TravelTicks => _mover.TravelTicks;

    /// <summary>How far along the way the brush is, from 0 to <see cref="TravelTicks"/>.</summary>
    public int TicksTravelled => _mover.TicksTravelled;

    /// <summary>How many inputs carried an argument this mover could not use.</summary>
    public int RefusedInputCount { get; private set; }

    /// <inheritdoc/>
    protected override void OnSpawn()
    {
        if (!LinearMover.TryNormalize(MoveDirection, out Vector3 direction))
        {
            Data.TryGetValue("movedir", out string authored);
            RefuseKeyvalue("movedir", authored);
        }

        _mover.SetTravel(direction, Distance, Speed);
        _mover.PlaceAt(_mover.TicksAt(StartPosition));
    }

    /// <inheritdoc/>
    protected override void OnTick()
    {
        switch (_mover.Advance())
        {
            case LinearMoverStep.Moving:
                return;

            case LinearMoverStep.ArrivedOpen:
                FireOnFullyOpen();
                break;

            case LinearMoverStep.ArrivedClosed:
                FireOnFullyClosed();
                break;
        }

        SetTicking(false);
    }

    [EntityInput("Open")]
    private void Open(ref EntityInputContext context) => HeadFor(_mover.TravelTicks);

    [EntityInput("Close")]
    private void Close(ref EntityInputContext context) => HeadFor(0);

    // An argument that is not a number leaves the brush on its course.
    [EntityInput("SetPosition")]
    private void SetPosition(ref EntityInputContext context)
    {
        if (!KeyvalueWire.TryParseFloat(context.Parameter, out float position))
        {
            RefusedInputCount++;
            return;
        }

        HeadFor(_mover.TicksAt(position));
    }

    private void HeadFor(int ticks)
    {
        _mover.MoveTo(ticks);
        SetTicking(_mover.IsMoving);
    }
}
