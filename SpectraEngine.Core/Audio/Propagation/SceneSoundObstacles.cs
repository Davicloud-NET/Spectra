using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// The scene that is current as sound's obstacles: its static world, live or
/// baked, and its parts that collide. A sound on or under a part is not
/// behind that part, the parts above it in the tree, or a solid the sound
/// stands in. It asks for the scene on every call and keeps none. Render
/// thread only.
/// </summary>
public sealed class SceneSoundObstacles : ISoundObstacles
{
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

        bool isOnAPart = _ownBody.Count > 0;

        // Collide decides what blocks. A part that rays do not hit is still solid.
        var filter = new SceneQueryFilter { IgnoreQueryFlags = true, Ignore = _ownBody };
        int count = scene.TraceSolidSpans(from, to, in filter, spans, out truncated);

        // Do not keep the nodes alive between traces.
        _ownBody.Clear();

        // A door that slides into its wall takes its sounds in with it. The
        // wall is then no more in their way than the door is.
        if (isOnAPart && count > 0 && spans[0].Start <= SolidSpan.Tolerance)
        {
            spans[1..count].CopyTo(spans);
            count--;
        }

        return count;
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
