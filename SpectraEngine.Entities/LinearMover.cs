using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Entities;

/// <summary>
/// Slides an entity's node in a straight line between the pose it was authored
/// at, closed, and an open pose. Owned by the entity class that uses it.
/// </summary>
// Progress is a count of ticks, never a summed position: both ends are the
// stored transforms, and a trip back takes as long as the trip out.
// Fires no output and schedules nothing, so a replay can run it alone.
public sealed class LinearMover
{
    /// <summary>The slowest a mover travels, in units a second.</summary>
    public const float MinimumSpeed = 0.01f;

    /// <summary>
    /// How far inside one of the mover's brushes the player's own tick may
    /// leave it before the mover stops pressing.
    /// </summary>
    public const float BlockDepth = 0.1f;

    private const float MinimumDirectionLength = 1e-6f;

    private const double WholeTickSlack = 1e-3;

    private readonly Entity _owner;

    // Null until the owner says which brushes travel with the node.
    private BrushPenetration? _solid;

    /// <summary>
    /// A mover for <paramref name="owner"/>'s node. It goes nowhere until
    /// <see cref="SetTravel"/>.
    /// </summary>
    public LinearMover(Entity owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>The node's transform at rest closed: the one it was authored with.</summary>
    public Transform Closed { get; private set; } = Transform.Identity;

    /// <summary>The node's transform fully open.</summary>
    public Transform Open { get; private set; } = Transform.Identity;

    /// <summary>How many ticks the whole trip takes. At least one.</summary>
    public int TravelTicks { get; private set; } = 1;

    /// <summary>
    /// How far along the trip the node is, from 0, closed, to
    /// <see cref="TravelTicks"/>, open.
    /// </summary>
    public int TicksTravelled { get; private set; }

    /// <summary>
    /// The tick of the trip the node is heading for. Equal to
    /// <see cref="TicksTravelled"/> at rest.
    /// </summary>
    public int TargetTicks { get; private set; }

    /// <summary>Whether the node is at its open pose.</summary>
    public bool IsFullyOpen => TicksTravelled == TravelTicks;

    /// <summary>Whether the node is at its closed pose.</summary>
    public bool IsFullyClosed => TicksTravelled == 0;

    /// <summary>Whether the node has somewhere left to go.</summary>
    public bool IsMoving => TargetTicks != TicksTravelled;

    /// <summary>
    /// Brings an authored direction to unit length. One with no length is
    /// refused and comes back as up.
    /// </summary>
    public static bool TryNormalize(Vector3 authored, out Vector3 direction)
    {
        float length = authored.Length();
        if (!float.IsFinite(length) || length < MinimumDirectionLength)
        {
            direction = Vector3.UnitY;
            return false;
        }

        direction = authored / length;
        return true;
    }

    /// <summary>
    /// How much room a set of brushes takes along a direction: the distance
    /// that slides them clear of where they stand. Zero with no brushes.
    /// </summary>
    /// <param name="node">The node whose axes <paramref name="direction"/> is in.</param>
    /// <param name="brushes">Brush nodes at or below <paramref name="node"/>.</param>
    /// <param name="direction">Unit length.</param>
    public static float ExtentAlong(SceneNode node, IReadOnlyList<SceneNode> brushes, Vector3 direction)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(brushes);

        float low = float.PositiveInfinity;
        float high = float.NegativeInfinity;

        for (int i = 0; i < brushes.Count; i++)
        {
            if (brushes[i].Brush is not { } brush)
                continue;

            Matrix4x4 toNode = RelativeTo(node, brushes[i]);
            IReadOnlyList<Polygon> faces = brush.LocalFaces;
            for (int f = 0; f < faces.Count; f++)
            {
                foreach (Vector3 vertex in faces[f].VertexSpan)
                {
                    float along = Vector3.Dot(Vector3.Transform(vertex, toNode), direction);
                    low = MathF.Min(low, along);
                    high = MathF.Max(high, along);
                }
            }
        }

        return high > low ? high - low : 0f;
    }

    /// <summary>
    /// Takes the node's present transform as closed and works out the open
    /// pose and the length of the trip. Call once, when the owner spawns.
    /// </summary>
    /// <param name="direction">Unit length, in the node's own axes.</param>
    /// <param name="distance">How far open is from closed. Below zero means zero.</param>
    /// <param name="speed">Units a second, raised to <see cref="MinimumSpeed"/> when lower.</param>
    public void SetTravel(Vector3 direction, float distance, float speed)
    {
        // Written so NaN takes the fallback too.
        if (!(distance > 0f))
            distance = 0f;
        if (!(speed >= MinimumSpeed))
            speed = MinimumSpeed;

        Transform closed = _owner.Node.LocalTransform;
        Transform open = closed;
        open.Position += Vector3.Transform(direction, closed.Rotation) * distance;

        Closed = closed;
        Open = open;
        TravelTicks = WholeTicks(distance / ((double)speed * _owner.World.FixedDeltaTime));
        TicksTravelled = 0;
        TargetTicks = 0;
    }

    /// <summary>
    /// Names the brushes that travel with the node, so the mover can tell
    /// when one of them squeezes the player. A mover given none is never
    /// blocked.
    /// </summary>
    /// <param name="brushes">Brush nodes at or below the owner's node.</param>
    public void SetBrushes(IReadOnlyList<SceneNode> brushes)
    {
        ArgumentNullException.ThrowIfNull(brushes);
        _solid = new BrushPenetration(brushes);
    }

    /// <summary>The tick of the trip nearest to a fraction of it, from 0, closed, to 1, open.</summary>
    public int TicksAt(float fraction)
    {
        // Written so NaN means closed.
        if (!(fraction > 0f))
            return 0;
        if (fraction >= 1f)
            return TravelTicks;

        return (int)Math.Round((double)fraction * TravelTicks, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Heads for a tick of the trip from wherever the node is. Nothing moves
    /// until <see cref="Advance"/>.
    /// </summary>
    public void MoveTo(int ticks) => TargetTicks = Math.Clamp(ticks, 0, TravelTicks);

    /// <summary>
    /// Puts the node at a tick of the trip at once and rests it there. For
    /// the pose a level starts in.
    /// </summary>
    /// <returns>False when the world refused the move.</returns>
    public bool PlaceAt(int ticks)
    {
        ticks = Math.Clamp(ticks, 0, TravelTicks);
        if (ticks != TicksTravelled && !Write(ticks))
            return false;

        TicksTravelled = ticks;
        TargetTicks = ticks;
        return true;
    }

    /// <summary>
    /// Moves the node one tick towards <see cref="TargetTicks"/>. Call once a
    /// tick. A move that squeezes the player is taken back.
    /// </summary>
    public LinearMoverStep Advance()
    {
        if (TargetTicks == TicksTravelled)
            return LinearMoverStep.Stopped;

        int next = TicksTravelled + (TargetTicks > TicksTravelled ? 1 : -1);

        // Before the move: how deep the player's own tick left it.
        float depth = PlayerDepth();
        if (!Write(next))
        {
            // The count stays with the node, which has not moved.
            TargetTicks = TicksTravelled;
            return LinearMoverStep.Stopped;
        }

        // A player the brushes only reach is pushed or carried, however fast
        // the node goes. One left this deep had nowhere to go, and a move
        // further in is taken back.
        if (depth > BlockDepth && PlayerDepth() > depth)
        {
            Write(TicksTravelled);
            return LinearMoverStep.Blocked;
        }

        TicksTravelled = next;
        if (next != TargetTicks)
            return LinearMoverStep.Moving;
        if (next == TravelTicks)
            return LinearMoverStep.ArrivedOpen;

        return next == 0 ? LinearMoverStep.ArrivedClosed : LinearMoverStep.Stopped;
    }

    // A float step puts 2 / (2 * dt) a hair off a whole number. Without the
    // slack, a trip that divides evenly takes a tick more at some tick rates.
    // Written so NaN means one tick.
    internal static int WholeTicks(double ticks) =>
        ticks > 1d ? (int)Math.Min(Math.Ceiling(ticks - WholeTickSlack), int.MaxValue) : 1;

    private float PlayerDepth() =>
        _solid is { } solid && _owner.World.Player is { IsPresent: true } player
            ? solid.Deepest(player.Capsule)
            : float.NegativeInfinity;

    private bool Write(int ticks)
    {
        Transform pose = PoseAt(ticks);
        return _owner.World.SetLocalTransform(_owner.Node, in pose);
    }

    // Both ends are the stored transforms as they are, not a lerp that comes
    // out near them.
    private Transform PoseAt(int ticks)
    {
        if (ticks == 0)
            return Closed;
        if (ticks == TravelTicks)
            return Open;

        Transform pose = Closed;
        pose.Position = Vector3.Lerp(Closed.Position, Open.Position, (float)((double)ticks / TravelTicks));
        return pose;
    }

    // Through the local transforms on the way up, so a brush on the node
    // itself is measured in its own frame with no rounding.
    private static Matrix4x4 RelativeTo(SceneNode node, SceneNode descendant)
    {
        Matrix4x4 toNode = Matrix4x4.Identity;
        for (SceneNode? walk = descendant; walk is not null && !ReferenceEquals(walk, node); walk = walk.Parent)
            toNode *= walk.LocalTransform.Model;

        return toNode;
    }
}
