namespace SpectraEngine.Core.Graphics;

/// <summary>
/// Clip-space quad for the texture-orientation measurement. Its UVs come
/// straight from the vertex position, the same on every backend.
/// </summary>
// Not FullscreenTriangle: that one flips V on D3D, and a quad that compensates
// can't measure whether compensation is needed.
// Full standard vertex layout because D3D11 builds input layouts from the lit shader.
public static class OrientationQuad
{
    /// <summary>Which part of the target the quad covers.</summary>
    public enum Coverage
    {
        /// <summary>The whole target.</summary>
        Full,

        /// <summary>
        /// The upper half in clip space (y from 0 to 1). Used to check which
        /// way up the readback is.
        /// </summary>
        TopHalf,
    }

    /// <summary>
    /// Four clip-space vertices with <c>u = (x + 1) / 2</c> and <c>v = (y + 1) / 2</c>.
    /// </summary>
    public static float[] BuildVertices(Coverage coverage)
    {
        float yMin = coverage == Coverage.TopHalf ? 0f : -1f;

        // Counter-clockwise: front-facing on all three backends.
        (float X, float Y)[] corners = [(-1f, yMin), (1f, yMin), (1f, 1f), (-1f, 1f)];

        var vertices = new float[corners.Length * 8];
        for (int i = 0; i < corners.Length; i++)
        {
            (float x, float y) = corners[i];
            int o = i * 8;
            vertices[o + 0] = x;
            vertices[o + 1] = y;
            vertices[o + 2] = 0f;
            vertices[o + 3] = 0f;
            vertices[o + 4] = 0f;
            vertices[o + 5] = 1f;
            vertices[o + 6] = (x + 1f) * 0.5f;
            vertices[o + 7] = (y + 1f) * 0.5f;
        }
        return vertices;
    }

    /// <summary>Two triangles over the four corners.</summary>
    public static uint[] Indices => [0, 1, 2, 0, 2, 3];
}
