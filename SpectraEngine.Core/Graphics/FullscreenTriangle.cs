using System;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// One oversized triangle covering the whole target, for full-screen passes.
/// Texture coordinates are already correct for the backend sampling through it.
/// </summary>
// Full standard layout (position, normal, uv): SpectraShade has no vertex-ID
// input, and D3D11 builds input layouts from the lit shader's bytecode.
// V is flipped on D3D: render targets there have a top-left origin, GL's are
// bottom-left. Uploaded textures need no such flip.
public static class FullscreenTriangle
{
    /// <summary>Clip-space vertices with backend-correct texture coordinates.</summary>
    public static float[] BuildVertices(GraphicsBackend backend)
    {
        bool topLeftOrigin = backend is not GraphicsBackend.OpenGL;

        // Runs to 3 so the edges fall outside the view. Counter-clockwise,
        // front-facing on all three backends.
        (float X, float Y)[] corners = [(-1f, -1f), (3f, -1f), (-1f, 3f)];

        var vertices = new float[corners.Length * 8];
        for (int i = 0; i < corners.Length; i++)
        {
            (float x, float y) = corners[i];
            float u = (x + 1f) * 0.5f;
            float v = (y + 1f) * 0.5f;
            if (topLeftOrigin) v = 1f - v;

            int o = i * 8;
            vertices[o + 0] = x;
            vertices[o + 1] = y;
            vertices[o + 2] = 0f;
            // Normal, unused.
            vertices[o + 3] = 0f;
            vertices[o + 4] = 0f;
            vertices[o + 5] = 1f;
            vertices[o + 6] = u;
            vertices[o + 7] = v;
        }
        return vertices;
    }

    /// <summary>Indices. D3D11 rejects a zero-length index buffer, so these are real.</summary>
    public static uint[] Indices => [0, 1, 2];
}
