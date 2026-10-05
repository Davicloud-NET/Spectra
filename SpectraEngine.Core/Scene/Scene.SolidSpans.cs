using System;
using System.Numerics;

namespace SpectraEngine.Core.Scene;

public sealed partial class Scene
{
    // Made on the first trace: most scenes are never asked.
    private SolidSpanTracer? _solidSpans;

    // How many brushes the last trace had to clip.
    internal int LastSolidSpanBrushCount => _solidSpans?.BrushesClipped ?? 0;

    /// <summary>
    /// The solid a straight line from <paramref name="from"/> to
    /// <paramref name="to"/> passes through: what a sound has to get through.
    /// It counts the static world as last compiled, with its cuts open, and
    /// part brushes where they stand now. Meshes do not block. A line that
    /// touches a solid or runs along its face passes through nothing.
    /// Render thread only.
    /// </summary>
    /// <param name="spans">
    /// Filled from its start, nearest solid first. A line that starts inside a
    /// solid gets a span from 0, and one that ends inside a span up to its length.
    /// </param>
    /// <param name="truncated">True when there was more solid than <paramref name="spans"/> has room for.</param>
    /// <returns>How many spans were written.</returns>
    public int TraceSolidSpans(Vector3 from, Vector3 to, Span<SolidSpan> spans, out bool truncated) =>
        SolidSpans.Trace(from, to, default, spans, out truncated);

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
        return SolidSpans.Trace(from, to, in filter, spans, out truncated);
    }

    private SolidSpanTracer SolidSpans => _solidSpans ??= new SolidSpanTracer(this);
}
