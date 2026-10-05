using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// The current scene as sound's obstacles: its static world, live or baked,
/// and its parts that collide. Render thread only.
/// </summary>
// It asks for the scene on every call and keeps none.
public sealed class SceneSoundObstacles : ISoundObstacles
{
    /// <summary>
    /// How far round its part a sound on one is heard from, in units. Solid
    /// this close to the part is what the part is set in.
    /// </summary>
    // A door slides into a wall a little thicker than itself. This is the
    // wall's skin over the door, and its jamb for a listener off to one side.
    public const float BodyReach = 0.25f;

    private readonly Func<Scene.Scene?> _current;

    // The sound's node and the parts above it, for the trace in hand.
    private readonly List<SceneNode> _ownBody = [];

    // What the last read saw. Weak, so a level that was closed is not kept
    // until the next one plays.
    private readonly WeakReference<Scene.Scene?> _scene = new(null);
    private readonly WeakReference<CompiledStaticWorld?> _baked = new(null);
    private int _compileCount;
    private bool _hadBaked;
    private long _revision;

    /// <summary>Builds the obstacles of whatever scene <paramref name="current"/> names.</summary>
    /// <param name="current">The scene to trace now, or null when there is none.</param>
    public SceneSoundObstacles(Func<Scene.Scene?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;
    }

    /// <inheritdoc/>
    public bool TryReadWorld(out long revision)
    {
        if (_current() is not { } scene)
        {
            revision = 0;
            return false;
        }

        // A baked world never compiles, so the counter cannot say that one
        // arrived or left.
        CompiledStaticWorld? baked = scene.CompiledStaticWorld;
        int compileCount = scene.StaticWorldCompileCount;

        if (!IsSeen(scene, baked) || compileCount != _compileCount)
        {
            _scene.SetTarget(scene);
            _baked.SetTarget(baked);
            _hadBaked = baked is not null;
            _compileCount = compileCount;
            _revision++;
        }

        revision = _revision;
        return true;
    }

    /// <inheritdoc/>
    public Vector3 HeardFrom(Vector3 from, Vector3 to, SceneNode? body)
    {
        if (SolidPartOf(body) is not { Brush: { } brush } part)
            return from;

        // The part's own frame, taken as the span query takes it.
        Matrix4x4 world = part.WorldMatrix;
        Matrix4x4 linear = world;
        linear.Translation = Vector3.Zero;
        if (!Matrix4x4.Invert(linear, out Matrix4x4 toLocal))
            return from;

        Aabb reach = brush.LocalBounds.Expanded(BodyReach);
        Vector3 start = Vector3.TransformNormal(from - world.Translation, toLocal);
        Vector3 step = Vector3.TransformNormal(to - from, toLocal);

        return from + ((to - from) * ShareInside(in reach, start, step));
    }

    /// <inheritdoc/>
    public int Trace(Vector3 from, Vector3 to, SceneNode? body, Span<SolidSpan> spans, out bool truncated)
    {
        truncated = false;
        if (_current() is not { } scene)
            return 0;

        _ownBody.Clear();
        for (SceneNode? node = body; node is not null; node = node.Parent)
        {
            if (node.Brush is not null && node.BrushKind == BrushKind.Part)
                _ownBody.Add(node);
        }

        // Collide decides what blocks. A part that rays do not hit is still solid.
        var filter = new SceneQueryFilter { IgnoreQueryFlags = true, Ignore = _ownBody };
        int count = scene.TraceSolidSpans(from, to, in filter, spans, out truncated);

        // Do not keep the nodes alive between traces.
        _ownBody.Clear();
        return count;
    }

    // The nearest part a sound sits on or under that blocks sound. A trigger
    // blocks none, so a sound under one has no body.
    private static SceneNode? SolidPartOf(SceneNode? body)
    {
        for (SceneNode? node = body; node is not null; node = node.Parent)
        {
            if (node.BrushKind == BrushKind.Part && node.CanCollide &&
                node.Brush is { Operation: BrushOperation.Additive })
            {
                return node;
            }
        }

        return null;
    }

    // How much of a step that starts inside the box stays inside it, from 0
    // to 1. Zero for one that starts outside.
    private static float ShareInside(in Aabb box, Vector3 start, Vector3 step)
    {
        float share = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float at = start[axis];

            // Written so a NaN is outside.
            if (!(at >= box.Min[axis] && at <= box.Max[axis]))
                return 0f;

            float by = step[axis];
            if (by > 0f)
                share = MathF.Min(share, (box.Max[axis] - at) / by);
            else if (by < 0f)
                share = MathF.Min(share, (box.Min[axis] - at) / by);
        }

        return share;
    }

    private bool IsSeen(Scene.Scene scene, CompiledStaticWorld? baked)
    {
        if (!_scene.TryGetTarget(out Scene.Scene? seen) || !ReferenceEquals(seen, scene))
            return false;

        if (baked is null)
            return !_hadBaked;

        return _baked.TryGetTarget(out CompiledStaticWorld? seenBaked) && ReferenceEquals(seenBaked, baked);
    }
}
