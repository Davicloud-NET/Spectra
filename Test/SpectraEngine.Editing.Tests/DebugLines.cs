using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;

namespace SpectraEngine.Editing.Tests;

// Reads a DebugDraw buffer back. Six floats a vertex: position, then colour.
internal static class DebugLines
{
    public static List<Vector3> Coloured(DebugDraw output, Vector3 colour)
    {
        ReadOnlySpan<float> data = output.Vertices;
        var positions = new List<Vector3>();
        for (int i = 0; i + 5 < data.Length; i += 6)
        {
            if (new Vector3(data[i + 3], data[i + 4], data[i + 5]) == colour)
                positions.Add(new Vector3(data[i], data[i + 1], data[i + 2]));
        }

        return positions;
    }

    public static int CountOf(DebugDraw output, Vector3 colour) => Coloured(output, colour).Count / 2;
}
