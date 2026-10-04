using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

// Bobs one brush node up and down so the async static-world recompile stays
// busy in the demo. It runs after the editor each frame, so it re-centres on
// any edit made to the node instead of overwriting it. Render thread only.
internal sealed class DemoBobAnimation
{
    private readonly SceneNode _node;
    private readonly float _amplitude;
    private readonly double _periodSeconds;

    // The setters store values exactly, so a position that differs from
    // _lastWritten means somebody else wrote the node.
    private Vector3 _rest;
    private Vector3 _lastWritten;

    public DemoBobAnimation(SceneNode node, float amplitude, double periodSeconds)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(periodSeconds);

        _node = node;
        _amplitude = amplitude;
        _periodSeconds = periodSeconds;
        _rest = node.LocalPosition;
        _lastWritten = _rest;
    }

    public Vector3 Rest => _rest;

    // Lets a host that replaces the graph rebind by id.
    public SceneNode Node => _node;

    // elapsedSeconds is the running total, so a stalled frame does not shift the phase.
    public void Advance(double elapsedSeconds)
    {
        Vector3 current = _node.LocalPosition;
        if (current != _lastWritten)
        {
            // Somebody edited the node. Subtract our own last offset so the
            // write below puts it back where they left it, with no snap.
            _rest = current - (_lastWritten - _rest);
        }

        float bob = _amplitude * MathF.Sin((float)(elapsedSeconds * (2.0 * Math.PI / _periodSeconds)));

        Vector3 next = _rest + new Vector3(0f, bob, 0f);
        _node.LocalPosition = next;
        _lastWritten = next;
    }
}
