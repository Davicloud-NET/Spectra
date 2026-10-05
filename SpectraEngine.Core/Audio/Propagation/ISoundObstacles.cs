using System;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

/// <summary>
/// The solid world as sound meets it: what a straight line passes through,
/// and whether the world is still the one an earlier answer was traced in.
/// </summary>
public interface ISoundObstacles
{
    /// <summary>
    /// Reads which world there is to trace. False when there is none, and
    /// then nothing is in any sound's way.
    /// </summary>
    /// <param name="revision">
    /// Another number than last time when the solid world is another: a
    /// compile landed, a baked map was loaded or another level is current. A
    /// part that moves does not change it.
    /// </param>
    bool TryReadWorld(out long revision);

    /// <summary>
    /// The solids on the line from <paramref name="from"/> to
    /// <paramref name="to"/>, nearest first.
    /// </summary>
    /// <param name="body">
    /// The node the sound sits on, or null. That node and the parts above it
    /// in the tree are not in the way.
    /// </param>
    /// <param name="truncated">True when there was more solid than <paramref name="spans"/> has room for.</param>
    /// <returns>How many spans were written.</returns>
    int Trace(Vector3 from, Vector3 to, SceneNode? body, Span<SolidSpan> spans, out bool truncated);
}
