using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

public sealed partial class Scene
{
    private readonly SolidSpanTracer _solidSpans = new();

    // How many brushes the last trace had to clip.
    internal int LastSolidSpanBrushCount => _solidSpans.BrushesClipped;

    /// <summary>
    /// The solid a straight line from <paramref name="from"/> to
    /// <paramref name="to"/> passes through, nearest first. This is what a
    /// sound has to get through. It counts the static world as carved, so a
    /// cut doorway is open, and part brushes where they stand now. Meshes do
    /// not block. Render thread only.
    /// </summary>
    /// <param name="spans">Filled from its start, nearest solid first.</param>
    /// <param name="truncated">True when there was more solid than <paramref name="spans"/> has room for.</param>
    /// <returns>How many spans were written.</returns>
    // A segment that starts inside a solid gets a span from 0, and one that
    // ends inside gets a span up to its length. One that only touches a solid
    // or runs along a face passes through nothing, like one with no length.
    // The world is the one last compiled. Under a baked map it is the collision
    // hulls, which name the default material when the map kept none for them.
    public int TraceSolidSpans(Vector3 from, Vector3 to, Span<SolidSpan> spans, out bool truncated) =>
        _solidSpans.Trace(this, from, to, default, spans, out truncated);

    /// <summary>
    /// As <see cref="TraceSolidSpans(Vector3, Vector3, Span{SolidSpan}, out bool)"/>,
    /// with <paramref name="filter"/> choosing among the parts. A part blocks
    /// only when it collides, so a trigger never does, and the default filter
    /// also wants <see cref="SceneNode.CanQuery"/>. A subtractive part blocks
    /// nothing. The static world always blocks, unless the filter leaves it out.
    /// </summary>
    public int TraceSolidSpans(
        Vector3 from, Vector3 to, in SceneQueryFilter filter, Span<SolidSpan> spans, out bool truncated)
    {
        ValidateQueryGroup(in filter);
        return _solidSpans.Trace(this, from, to, in filter, spans, out truncated);
    }
}
