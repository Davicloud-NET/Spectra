using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Icons;

/// <summary>
/// One icon as the 3D view draws it: strokes of straight segments on a unit
/// box, x to the right and y down as in its file.
/// </summary>
public sealed class ViewportGlyph
{
    private readonly Vector2[] _points;

    // Where each stroke begins in _points, then one entry for the end of the last.
    private readonly int[] _starts;

    /// <summary>Builds a glyph from polylines laid end to end.</summary>
    /// <param name="points">Every stroke's points, one stroke after the other.</param>
    /// <param name="starts">The index in <paramref name="points"/> where each stroke begins.</param>
    public ViewportGlyph(IReadOnlyList<Vector2> points, IReadOnlyList<int> starts)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(starts);

        _points = [.. points];
        _starts = new int[starts.Count + 1];
        for (int i = 0; i < starts.Count; i++)
            _starts[i] = starts[i];

        _starts[^1] = _points.Length;
        SegmentCount = _points.Length - starts.Count;
    }

    /// <summary>How many strokes the glyph has.</summary>
    public int StrokeCount => _starts.Length - 1;

    /// <summary>How many straight segments its strokes add up to.</summary>
    public int SegmentCount { get; }

    /// <summary>Every point of every stroke.</summary>
    public ReadOnlySpan<Vector2> Points => _points;

    /// <summary>One stroke's points, in the order they are joined.</summary>
    public ReadOnlySpan<Vector2> Stroke(int index) =>
        _points.AsSpan(_starts[index], _starts[index + 1] - _starts[index]);
}
