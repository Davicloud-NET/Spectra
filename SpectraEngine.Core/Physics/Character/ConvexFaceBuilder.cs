using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Core.Physics.Character;

/// <summary>
/// Builds the face polygons of a convex solid from its outward half-space
/// planes.
/// </summary>
// Seed-and-clip like Brush, but tolerant of duplicate planes, which Brush
// rejects and a flush cut produces. Also the emptiness test for cover
// elements: planes that bound nothing leave fewer than four faces.
public static class ConvexFaceBuilder
{
    private const float MinFaceExtent = 1e-4f;

    /// <summary>
    /// The face polygons of the solid bounded by <paramref name="planes"/>, or
    /// an empty array when the planes bound nothing.
    /// </summary>
    public static Polygon[] Build(ReadOnlySpan<Plane> planes, float minThickness)
    {
        float seedExtent = SeedExtent(planes);
        if (seedExtent <= 0f)
            return [];

        var faces = new List<Polygon>(planes.Length);

        for (int i = 0; i < planes.Length; i++)
        {
            Polygon? face = SeedQuad(planes[i], seedExtent);

            for (int j = 0; j < planes.Length && face is not null; j++)
            {
                if (i == j)
                    continue;

                // Skip a duplicate of this face's own plane. Clipping by it
                // would delete the face (Split puts coplanar on the front).
                if (SameDirectedPlane(planes[i], planes[j]))
                    continue;

                face.Split(planes[j], out _, out Polygon? inside);
                face = inside;
            }

            if (face is null || face.VertexCount < 3)
                continue;

            if (Extent(face) < MathF.Max(minThickness, MinFaceExtent))
                continue;

            faces.Add(face);
        }

        return faces.Count >= 4 ? [.. faces] : [];
    }

    /// <summary>Whether two planes are the same plane facing the same way.</summary>
    public static bool SameDirectedPlane(Plane a, Plane b) =>
        Vector3.Dot(a.Normal, b.Normal) > 1f - 1e-4f && MathF.Abs(a.D - b.D) < 1e-4f;

    /// <summary>The axis-aligned bounds of a face set.</summary>
    public static Aabb Bounds(ReadOnlySpan<Polygon> faces)
    {
        if (faces.Length == 0)
            return new Aabb(Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int f = 0; f < faces.Length; f++)
        {
            ReadOnlySpan<Vector3> verts = faces[f].VertexSpan;
            for (int v = 0; v < verts.Length; v++)
            {
                min = Vector3.Min(min, verts[v]);
                max = Vector3.Max(max, verts[v]);
            }
        }

        return new Aabb(min, max);
    }

    // Generous on purpose: too small a seed clips real geometry away.
    private static float SeedExtent(ReadOnlySpan<Plane> planes)
    {
        float largest = 0f;
        for (int i = 0; i < planes.Length; i++)
        {
            float offset = MathF.Abs(planes[i].D);
            if (!float.IsFinite(offset))
                return 0f;
            largest = MathF.Max(largest, offset);
        }

        return MathF.Max(largest * 4f, 16f);
    }

    private static Polygon? SeedQuad(Plane plane, float extent)
    {
        Vector3 normal = plane.Normal;
        float lengthSquared = normal.LengthSquared();
        if (lengthSquared < 1e-12f)
            return null;

        Vector3 reference = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(reference, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);

        Vector3 origin = normal * -plane.D;
        Vector3 u = tangent * extent;
        Vector3 v = bitangent * extent;

        // Counter-clockwise about the outward normal.
        var verts = new[]
        {
            origin - u - v,
            origin + u - v,
            origin + u + v,
            origin - u + v,
        };

        return new Polygon(verts, plane);
    }

    private static float Extent(Polygon face)
    {
        ReadOnlySpan<Vector3> verts = face.VertexSpan;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (int i = 0; i < verts.Length; i++)
        {
            min = Vector3.Min(min, verts[i]);
            max = Vector3.Max(max, verts[i]);
        }

        Vector3 size = max - min;
        return MathF.Max(size.X, MathF.Max(size.Y, size.Z));
    }
}
