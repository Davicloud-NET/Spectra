using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Vertex and index data for built-in shapes. Eight floats per vertex:
/// position (xyz), normal (xyz), uv (xy).
/// </summary>
public static class Primitives
{
    /// <summary>A unit cube centred on the origin, with flat normals and 0..1 UVs per face.</summary>
    public static (float[] Vertices, uint[] Indices) Cube()
    {
        // Four corners per face, counter-clockwise seen from outside.
        float[] vertices =
        [
            // +Z
            -0.5f, -0.5f,  0.5f,   0f,  0f,  1f,   0f, 0f,
             0.5f, -0.5f,  0.5f,   0f,  0f,  1f,   1f, 0f,
             0.5f,  0.5f,  0.5f,   0f,  0f,  1f,   1f, 1f,
            -0.5f,  0.5f,  0.5f,   0f,  0f,  1f,   0f, 1f,
            // -Z
             0.5f, -0.5f, -0.5f,   0f,  0f, -1f,   0f, 0f,
            -0.5f, -0.5f, -0.5f,   0f,  0f, -1f,   1f, 0f,
            -0.5f,  0.5f, -0.5f,   0f,  0f, -1f,   1f, 1f,
             0.5f,  0.5f, -0.5f,   0f,  0f, -1f,   0f, 1f,
            // -X
            -0.5f, -0.5f, -0.5f,  -1f,  0f,  0f,   0f, 0f,
            -0.5f, -0.5f,  0.5f,  -1f,  0f,  0f,   1f, 0f,
            -0.5f,  0.5f,  0.5f,  -1f,  0f,  0f,   1f, 1f,
            -0.5f,  0.5f, -0.5f,  -1f,  0f,  0f,   0f, 1f,
            // +X
             0.5f, -0.5f,  0.5f,   1f,  0f,  0f,   0f, 0f,
             0.5f, -0.5f, -0.5f,   1f,  0f,  0f,   1f, 0f,
             0.5f,  0.5f, -0.5f,   1f,  0f,  0f,   1f, 1f,
             0.5f,  0.5f,  0.5f,   1f,  0f,  0f,   0f, 1f,
            // +Y
            -0.5f,  0.5f,  0.5f,   0f,  1f,  0f,   0f, 0f,
             0.5f,  0.5f,  0.5f,   0f,  1f,  0f,   1f, 0f,
             0.5f,  0.5f, -0.5f,   0f,  1f,  0f,   1f, 1f,
            -0.5f,  0.5f, -0.5f,   0f,  1f,  0f,   0f, 1f,
            // -Y
            -0.5f, -0.5f, -0.5f,   0f, -1f,  0f,   0f, 0f,
             0.5f, -0.5f, -0.5f,   0f, -1f,  0f,   1f, 0f,
             0.5f, -0.5f,  0.5f,   0f, -1f,  0f,   1f, 1f,
            -0.5f, -0.5f,  0.5f,   0f, -1f,  0f,   0f, 1f,
        ];

        uint[] indices = new uint[36];
        for (uint face = 0; face < 6; face++)
        {
            uint b = face * 4;
            uint i = face * 6;
            indices[i + 0] = b + 0;
            indices[i + 1] = b + 1;
            indices[i + 2] = b + 2;
            indices[i + 3] = b + 0;
            indices[i + 4] = b + 2;
            indices[i + 5] = b + 3;
        }

        return (vertices, indices);
    }

    /// <summary>
    /// A UV sphere centred on the origin, with analytic normals and a
    /// longitude/latitude UV map.
    /// </summary>
    /// <param name="segments">Divisions around the equator; at least 3.</param>
    /// <param name="rings">Divisions from pole to pole; at least 2.</param>
    // The seam column and the poles are duplicated vertices: one vertex can't
    // carry two UVs.
    public static (float[] Vertices, uint[] Indices) Sphere(int segments = 32, int rings = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(rings, 2);

        int columns = segments + 1;
        int rows = rings + 1;
        float[] vertices = new float[columns * rows * 8];

        int v = 0;
        for (int y = 0; y < rows; y++)
        {
            // South pole first, so v = 0 is the bottom of the image.
            float vCoord = y / (float)rings;
            float phi = vCoord * MathF.PI;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            for (int x = 0; x < columns; x++)
            {
                float uCoord = x / (float)segments;
                float theta = uCoord * MathF.Tau;

                float nx = MathF.Cos(theta) * sinPhi;
                float ny = -cosPhi;
                float nz = MathF.Sin(theta) * sinPhi;

                vertices[v++] = nx * 0.5f;
                vertices[v++] = ny * 0.5f;
                vertices[v++] = nz * 0.5f;
                vertices[v++] = nx;
                vertices[v++] = ny;
                vertices[v++] = nz;
                vertices[v++] = uCoord;
                vertices[v++] = vCoord;
            }
        }

        var indices = new List<uint>(segments * rings * 6);
        for (int y = 0; y < rings; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                uint a = (uint)(y * columns + x);
                uint b = (uint)(a + columns);

                // At a pole one triangle of the quad is degenerate; skip it.
                if (y != 0)
                {
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(a + 1);
                }
                if (y != rings - 1)
                {
                    indices.Add(a + 1);
                    indices.Add(b);
                    indices.Add(b + 1);
                }
            }
        }

        return (vertices, indices.ToArray());
    }

    /// <summary>A two-colour checkerboard in RGB8, for use as a debug texture.</summary>
    public static byte[] CheckerboardRgb8(int size = 16, byte light = 230, byte dark = 60)
    {
        var pixels = new byte[size * size * 3];
        int half = size / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool dark2 = ((x < half) ^ (y < half));
                byte v = dark2 ? dark : light;
                int o = (y * size + x) * 3;
                pixels[o + 0] = v;
                pixels[o + 1] = v;
                pixels[o + 2] = v;
            }
        }
        return pixels;
    }
}
