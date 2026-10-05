using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;

namespace SoundCornersSpike.Questions;

// Not a question. Prints a path corner by corner, for looking at a number
// that seems wrong. Runs only when asked for by name.
internal static class PathDump
{
    public static void Run()
    {
        foreach (HandCase c in HandCase.All())
        {
            Show(c, 1f, Accuracy.Chosen);
            Show(c, 0.5f, Accuracy.Chosen);
        }
    }

    private static void Show(HandCase c, float cell, Method method)
    {
        var probe = new LiveProbe(c.World);
        var field = new AirField(probe, cell, CellRule.CenterAndLinks);
        var flood = new Flood();
        PathReader reader = method.Reader(flood);

        flood.Run(field, c.Parts, c.Listener, method.Options(radius: 40f));

        Console.WriteLine($"{c.Name}, cell {cell}, {method.Name}");
        Console.WriteLine("  true:  " + string.Join(" -> ", c.ExactPath.ConvertAll(Show)));

        PathAnswer answer = reader.Read(c.Source);
        var points = new List<Vector3>(reader.Points);
        Console.WriteLine("  flood: " + string.Join(" -> ", points.ConvertAll(Show)));

        for (int i = 1; i < points.Count; i++)
            Console.WriteLine($"    the listener sees corner {i}: {!probe.SegmentBlocked(c.Listener, points[i])}");

        PathAnswer refined = reader.Read(c.Source, refine: true);
        PathAnswer tightened = reader.Read(c.Source, tighten: true);
        Console.WriteLine(
            $"  first corner {Show(answer.BendPoint)}, refined {Show(refined.BendPoint)}, tightened {Show(tightened.BendPoint)}");
    }

    private static string Show(Vector3 p) => $"({p.X:F2}, {p.Y:F2}, {p.Z:F2})";
}
