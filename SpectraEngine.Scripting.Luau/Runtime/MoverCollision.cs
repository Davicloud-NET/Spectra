using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Luau.Runtime;

/// <summary>What the player collides with, as a mover is handed it: the world and every solid part. It sweeps the player's capsule, finds the planes that touch it, and keeps a list of planes to solve and to clip a velocity against. A place is where the capsule's feet are. A sweep or a gather reaches no further than 1000 units, from a place no further than that from where the player began the tick.</summary>
// The engine's own pieces, so a mover in Luau is a page and collides as the
// built-in one does. The list is the engine's: a plane crosses as a normal
// and a number, and a tick makes no table.
[ScriptClass("CharacterCollision")]
internal sealed partial class MoverCollision : ScriptObject
{
    // Normal only, as the built-in mover tells two planes of a slide apart.
    private const float SamePlaneDot = 0.999f;

    private static readonly CharacterQueryFilter Filter = CharacterQueryFilter.Default;

    private readonly MoverFrame _frame;
    private readonly CharacterContactPlane[] _planes = new CharacterContactPlane[CharacterMover.MaxContactPlanes];
    private readonly CharacterContactSource[] _sources = new CharacterContactSource[CharacterMover.MaxContactPlanes];
    private int _count;
    private Aabb _volume;

    public MoverCollision(MoverFrame frame) => _frame = frame;

    public override string Name => "CharacterCollision";

    /// <summary>How many planes the list holds, at most 8.</summary>
    [ScriptProperty]
    public int PlaneCount => _count;

    // Picks what the tick can touch, as the built-in mover does at its start.
    public void Begin(in CharacterState state, CharacterTuning tuning, float deltaTime)
    {
        _count = 0;
        _volume = CharacterMover.TickVolume(in state, tuning, deltaTime);
        Source.BeginTick(in _volume, in Filter);
    }

    /// <summary>Moves the capsule from a place along a translation and stops a skin off what is in the way. Returns how much of the way it went, from 0 to 1, and for what stopped it: its normal, how far off it is, the nearest point on it, and its part when it is one. With nothing in the way the first is 1.</summary>
    [ScriptMethod]
    public (float Fraction, Vector3 Normal, float Separation, Vector3 Point, SceneNode? Part) Sweep(Vector3 feet, Vector3 translation)
    {
        ICharacterCollisionSource source = Source;
        if (!MoverLimits.IsWithin(translation, MoverLimits.MaxReach))
            throw new InvalidOperationException(MoverLimits.TooLong("A sweep", translation, MoverLimits.MaxReach, "units"));

        CharacterCapsule capsule = CapsuleAt(feet);
        Cover(source, in capsule, translation, 0f);
        float fraction = source.SweepCapsule(
            in capsule, translation, in Filter, out CharacterContactPlane plane, out CharacterContactSource stopped);

        return (fraction, plane.Plane.Normal, plane.Plane.D, stopped.Point, stopped.Node);
    }

    /// <summary>Empties the list of planes.</summary>
    [ScriptMethod]
    public void ClearPlanes()
    {
        _ = Source;
        _count = 0;
    }

    /// <summary>Adds a plane a sweep stopped at to the list, by its normal, so Clip takes away what drives into it. One that faces the way a plane of the list does is left out.</summary>
    [ScriptMethod]
    public void AddPlane(Vector3 normal)
    {
        _ = Source;
        float length = normal.Length();
        if (!(length > 1e-3f) || !float.IsFinite(length))
            throw new InvalidOperationException(MoverLimits.NotADirection(normal));

        normal /= length;
        for (int i = 0; i < _count; i++)
        {
            if (Vector3.Dot(_planes[i].Plane.Normal, normal) > SamePlaneDot)
                return;
        }

        if (_count >= _planes.Length)
            return;

        // Pressed against, so Clip does not skip it.
        _planes[_count] = CharacterContactPlane.Rigid(normal, 0f);
        _planes[_count].Push = 1f;
        _sources[_count] = default;
        _count++;
    }

    /// <summary>Takes out of a velocity what drives into a plane of the list. A plane counts when AddPlane added it, or when Solve pushed against it.</summary>
    [ScriptMethod]
    public Vector3 Clip(Vector3 velocity)
    {
        ICharacterCollisionSource source = Source;
        if (!MoverLimits.IsWithin(velocity, MoverLimits.MaxSpeed))
            throw new InvalidOperationException(MoverLimits.TooLong("A vector to clip", velocity, MoverLimits.MaxSpeed, "in length"));

        return source.ClipVelocity(velocity, _planes.AsSpan(0, _count));
    }

    /// <summary>Fills the list with every plane no further from the capsule at a place than a distance, the deepest first, and returns how many there are. With a distance of 0 these are the planes the capsule is in.</summary>
    [ScriptMethod]
    public int Gather(Vector3 feet, float maxSeparation)
    {
        ICharacterCollisionSource source = Source;
        if (!MoverLimits.IsFrom0To(MoverLimits.MaxReach, maxSeparation))
            throw new InvalidOperationException(MoverLimits.OutOfRange("The distance of a gather", maxSeparation, MoverLimits.MaxReach));

        CharacterCapsule capsule = CapsuleAt(feet);
        Cover(source, in capsule, Vector3.Zero, maxSeparation);
        _count = source.GatherPlanes(in capsule, maxSeparation, in Filter, _planes, _sources);
        return _count;
    }

    /// <summary>One plane of the list, counted from 1: its normal, how far the capsule is from it, below zero when the capsule is in it, and its part when it is one.</summary>
    [ScriptMethod]
    public (Vector3 Normal, float Separation, SceneNode? Part) Plane(int index)
    {
        _ = Source;
        if (index < 1 || index > _count)
            throw new InvalidOperationException($"The list has no plane {index}. It holds {_count}.");

        return (_planes[index - 1].Plane.Normal, _planes[index - 1].Plane.D, _sources[index - 1].Node);
    }

    /// <summary>The move nearest to a wanted one that leaves the capsule in no plane of the list. With a wanted move of zero it is the push out of what the capsule is in.</summary>
    [ScriptMethod]
    public Vector3 Solve(Vector3 target)
    {
        ICharacterCollisionSource source = Source;
        if (!MoverLimits.IsWithin(target, MoverLimits.MaxReach))
            throw new InvalidOperationException(MoverLimits.TooLong("A move to solve", target, MoverLimits.MaxReach, "units"));

        return source.SolvePlanes(target, _planes.AsSpan(0, _count)).Delta;
    }

    private ICharacterCollisionSource Source =>
        _frame.IsOpen && _frame.Source is { } source ? source : throw new InvalidOperationException(MoverFrame.OnlyWhileMoving);

    private CharacterCapsule CapsuleAt(Vector3 feet)
    {
        if (!MoverLimits.IsPlace(feet))
            throw new InvalidOperationException(MoverLimits.NotAPlace("A place", feet));
        if (!MoverLimits.IsInReach(feet, _frame.StartedAt))
            throw new InvalidOperationException(MoverLimits.OutOfReach("A place", feet));

        CharacterTuning tuning = _frame.TuningNow();
        return CharacterCapsule.FromFeet(feet, tuning.StandHeight, tuning.Radius);
    }

    // A mover can go where the tick's volume does not reach, as a dash does.
    // The volume then grows, or the sweep would find nothing in its way.
    // MoverLimits.MaxReach bounds how far.
    private void Cover(ICharacterCollisionSource source, in CharacterCapsule capsule, Vector3 travel, float reach)
    {
        var pad = new Vector3(capsule.Radius + reach + _frame.TuningNow().SkinWidth);
        Vector3 min = Vector3.Min(capsule.Center1, capsule.Center2);
        Vector3 max = Vector3.Max(capsule.Center1, capsule.Center2);
        min = Vector3.Min(min, min + travel) - pad;
        max = Vector3.Max(max, max + travel) + pad;

        if (Vector3.Min(min, _volume.Min) == _volume.Min && Vector3.Max(max, _volume.Max) == _volume.Max)
            return;

        _volume = new Aabb(Vector3.Min(min, _volume.Min), Vector3.Max(max, _volume.Max)).Expanded(CharacterMover.BroadphaseMargin);
        source.BeginTick(in _volume, in Filter);
    }
}
