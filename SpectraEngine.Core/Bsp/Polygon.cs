using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Bsp;

/// <summary>
/// A convex polygon on a single plane, wound counter-clockwise around
/// <see cref="Surface"/>'s normal. Immutable.
/// </summary>
public sealed class Polygon
{
    /// <summary>Distance tolerance for treating a vertex as lying on a plane.</summary>
    public const float Epsilon = 1e-4f;

    // Up to this many distances go on the stack; larger faces rent from the
    // pool. The stackalloc is sized to the vertex count, not this maximum,
    // because every stackalloc is zeroed.
    private const int MaxStackDistances = 128;

    private readonly Vector3[] _vertices;

    /// <summary>Creates a polygon with the default material and world-aligned texture axes.</summary>
    public Polygon(Vector3[] vertices, Plane surface)
        : this(vertices, surface, FaceSurface.Default)
    {
    }

    /// <summary>Creates a polygon on <paramref name="surface"/> wearing <paramref name="face"/>.</summary>
    public Polygon(Vector3[] vertices, Plane surface, FaceSurface face)
    {
        _vertices = vertices;
        Surface = surface;
        Face = face;
        // Not lazy: carve workers read Bounds on shared polygons in parallel,
        // and a lazily written Aabb? can tear.
        Bounds = Aabb.FromPoints(vertices);
    }

    public IReadOnlyList<Vector3> Vertices => _vertices;

    /// <summary>The vertices as a span. Use this in hot loops; <see cref="Vertices"/> allocates an enumerator.</summary>
    public ReadOnlySpan<Vector3> VertexSpan => _vertices;

    public int VertexCount => _vertices.Length;

    /// <summary>The plane this polygon lies on.</summary>
    public Plane Surface { get; }

    /// <summary>The material and texture axes. Kept through splits, snapping and welding.</summary>
    public FaceSurface Face { get; }

    /// <summary>The axis-aligned bounding box, computed at construction.</summary>
    public Aabb Bounds { get; }

    /// <summary>Classifies this polygon against a splitting plane.</summary>
    public PolygonClassification Classify(Plane splitter)
    {
        int count = _vertices.Length;
        if (count <= MaxStackDistances)
        {
            Span<float> distances = stackalloc float[count];
            return ClassifyInto(splitter, distances);
        }

        float[] rented = ArrayPool<float>.Shared.Rent(count);
        try
        {
            return ClassifyInto(splitter, rented.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    // Leaves the per-vertex distances in the span so Split can reuse them.
    private PolygonClassification ClassifyInto(Plane splitter, Span<float> distances)
    {
        // SIMD setup only pays off on many-sided faces.
        if (_vertices.Length >= Vector<float>.Count * 2)
        {
            SimdPlane.SignedDistances(splitter, _vertices, distances);
        }
        else
        {
            for (int i = 0; i < _vertices.Length; i++)
                distances[i] = Plane.DotCoordinate(splitter, _vertices[i]);
        }

        int front = 0, back = 0;
        foreach (float d in distances)
        {
            if (d > Epsilon) front++;
            else if (d < -Epsilon) back++;
        }

        if (front > 0 && back > 0) return PolygonClassification.Spanning;
        if (front > 0) return PolygonClassification.Front;
        if (back > 0) return PolygonClassification.Back;
        return PolygonClassification.Coplanar;
    }

    /// <summary>
    /// Splits this polygon into the parts in front of and behind
    /// <paramref name="splitter"/>. An output is null when nothing lies on that
    /// side. A coplanar polygon is reported as front.
    /// </summary>
    public void Split(Plane splitter, out Polygon? front, out Polygon? back)
    {
        int count = _vertices.Length;
        if (count <= MaxStackDistances)
        {
            Span<float> distances = stackalloc float[count];
            SplitWithDistances(splitter, distances, out front, out back);
            return;
        }

        float[] rented = ArrayPool<float>.Shared.Rent(count);
        try
        {
            SplitWithDistances(splitter, rented.AsSpan(0, count), out front, out back);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(rented);
        }
    }

    private void SplitWithDistances(Plane splitter, Span<float> distances, out Polygon? front, out Polygon? back)
    {
        switch (ClassifyInto(splitter, distances))
        {
            case PolygonClassification.Front:
            case PolygonClassification.Coplanar:
                front = this; back = null; return;
            case PolygonClassification.Back:
                front = null; back = this; return;
        }

        int count = _vertices.Length;

        // Counting pass. Conditions must match the fill pass below.
        int frontCount = 0, backCount = 0;
        for (int i = 0; i < count; i++)
        {
            float da = distances[i];
            float db = distances[(i + 1) % count];

            if (da >= -Epsilon) frontCount++;
            if (da <= Epsilon) backCount++;
            if ((da > Epsilon && db < -Epsilon) || (da < -Epsilon && db > Epsilon))
            {
                frontCount++;
                backCount++;
            }
        }

        // Fewer than three vertices: a sliver, no polygon on that side.
        Vector3[]? frontVerts = frontCount >= 3 ? new Vector3[frontCount] : null;
        Vector3[]? backVerts = backCount >= 3 ? new Vector3[backCount] : null;
        int fi = 0, bi = 0;

        for (int i = 0; i < count; i++)
        {
            int j = (i + 1) % count;
            Vector3 a = _vertices[i];
            float da = distances[i];
            float db = distances[j];

            if (da >= -Epsilon && frontVerts is not null) frontVerts[fi++] = a;
            if (da <= Epsilon && backVerts is not null) backVerts[bi++] = a;

            if ((da > Epsilon && db < -Epsilon) || (da < -Epsilon && db > Epsilon))
            {
                float t = da / (da - db);
                Vector3 crossing = Vector3.Lerp(a, _vertices[j], t);
                if (frontVerts is not null) frontVerts[fi++] = crossing;
                if (backVerts is not null) backVerts[bi++] = crossing;
            }
        }

        front = frontVerts is not null ? new Polygon(frontVerts, Surface, Face) : null;
        back = backVerts is not null ? new Polygon(backVerts, Surface, Face) : null;
    }

    /// <summary>Fan-triangulates the polygon into vertex triples.</summary>
    public IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> Triangulate()
    {
        for (int i = 1; i + 1 < _vertices.Length; i++)
            yield return (_vertices[0], _vertices[i], _vertices[i + 1]);
    }

    /// <summary>
    /// Returns a new polygon with the vertices, the plane and the face mapped
    /// through <paramref name="transform"/>.
    /// </summary>
    public Polygon Transformed(Matrix4x4 transform)
    {
        var verts = new Vector3[_vertices.Length];
        for (int i = 0; i < _vertices.Length; i++)
            verts[i] = Vector3.Transform(_vertices[i], transform);
        return new Polygon(verts, Plane.Transform(Surface, transform), Face.Transformed(transform));
    }

    /// <summary>
    /// Returns this polygon facing the other way: vertex order reversed and
    /// surface plane negated.
    /// </summary>
    // Always both. Winding drives culling, the plane drives normals and BSP
    // solidity; flipping only one renders inside out or reads inverted.
    public Polygon Flipped()
    {
        int count = _vertices.Length;
        var verts = new Vector3[count];
        for (int i = 0; i < count; i++)
            verts[i] = _vertices[count - 1 - i];
        return new Polygon(verts, new Plane(-Surface.Normal, -Surface.D), Face);
    }
}
