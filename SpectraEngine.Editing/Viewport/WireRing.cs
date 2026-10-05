using System;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Viewport;

// The hairline circle a selected node's shape is drawn with: a light's range,
// a cone's mouth, a distance round an entity.
internal static class WireRing
{
    private const int Segments = 32;

    // Line by line: DebugDraw.Polyline takes a list, which would allocate per
    // ring per frame.
    public static void Draw(DebugDraw output, Vector3 centre, Vector3 u, Vector3 v, float radius, Vector3 colour)
    {
        if (radius <= 0f)
            return;

        Vector3 previous = centre + (u * radius);

        for (int i = 1; i <= Segments; i++)
        {
            float angle = i * (MathF.Tau / Segments);
            Vector3 current = centre + (u * radius * MathF.Cos(angle)) + (v * radius * MathF.Sin(angle));
            output.Line(previous, current, colour);
            previous = current;
        }
    }

    // Three great circles on the world axes. They read as a sphere from any
    // side without a line for every degree.
    public static void Sphere(DebugDraw output, Vector3 centre, float radius, Vector3 colour)
    {
        Draw(output, centre, Vector3.UnitX, Vector3.UnitY, radius, colour);
        Draw(output, centre, Vector3.UnitY, Vector3.UnitZ, radius, colour);
        Draw(output, centre, Vector3.UnitZ, Vector3.UnitX, radius, colour);
    }
}
