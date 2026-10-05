using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 1: what it costs to ask whether a cell is air, how the rules
// disagree, at which size a doorway closes and a thin wall leaks.
internal static class Cells
{
    private static readonly (CellRule Rule, string Name)[] Rules =
    [
        (CellRule.Center, "centre point"),
        (CellRule.CenterAndLinks, "centre point and link rays"),
        (CellRule.NinePoints, "nine points"),
        (CellRule.Box, "box"),
    ];

    public static void Run()
    {
        DemoWorld demo = DemoWorld.Build();
        CsgWorld town = OutdoorWorlds.Town(2000, out _);

        Cost("the demo's course", demo.World, new Vector3(118f, -2f, -22f), new Vector3(172f, 6f, 22f));
        Cost("a town of 2,000 brushes", town, new Vector3(-27f, -2f, -22f), new Vector3(27f, 6f, 22f));
        Agreement(demo.World);
        Doorway();
        ThinWall();
    }

    private static void Cost(string name, CsgWorld world, Vector3 min, Vector3 max)
    {
        Report.Section($"Cell tests: cost in {name}");

        var live = new LiveProbe(world);
        var bakedArray = new BakedProbe(WorldBuilder.Bake(world, nativeMemory: false), "baked, array");
        var bakedNative = new BakedProbe(WorldBuilder.Bake(world, nativeMemory: true), "baked, native memory");

        var centers = new List<Vector3>();
        for (float z = min.Z + 0.5f; z < max.Z; z += 1f)
        {
            for (float y = min.Y + 0.5f; y < max.Y; y += 1f)
            {
                for (float x = min.X + 0.5f; x < max.X; x += 1f)
                    centers.Add(new Vector3(x, y, z));
            }
        }

        int solid = 0;
        foreach (Vector3 c in centers)
        {
            if (live.IsSolid(c)) solid++;
        }

        Report.Note($"{centers.Count} cells of size 1, {Report.Percent((double)solid / centers.Count)} with the centre in solid. "
            + $"{world.Placements.Count} brushes in {world.Chunks.Count} chunks.");

        var rows = new List<string[]>();
        var half = new Vector3(0.4995f);
        int sink = 0;

        foreach (SolidProbe probe in new SolidProbe[] { live, bakedArray, bakedNative })
        {
            (double point, double pointSpread) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 c in centers) sink += probe.IsSolid(c) ? 1 : 0;
                },
                centers.Count);

            (double box, double boxSpread) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 c in centers) sink += (int)probe.ClassifyBox(c, half);
                },
                centers.Count);

            (double segment, double segmentSpread) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 c in centers) sink += probe.SegmentBlocked(c, c + Vector3.UnitX) ? 1 : 0;
                },
                centers.Count);

            (double longRay, double longSpread) = Timing.PerCall(
                () =>
                {
                    Vector3 eye = (min + max) * 0.5f;
                    foreach (Vector3 c in centers) sink += probe.SegmentBlocked(eye, c) ? 1 : 0;
                },
                centers.Count);

            rows.Add(
            [
                probe.Name,
                $"{Report.F(point, 1)} ns, {Report.F(pointSpread, 0)}%",
                $"{Report.F(box, 1)} ns, {Report.F(boxSpread, 0)}%",
                $"{Report.F(segment, 1)} ns, {Report.F(segmentSpread, 0)}%",
                $"{Report.F(longRay, 1)} ns, {Report.F(longSpread, 0)}%",
            ]);
        }

        Report.Note("Each cell is a median, then how far three rounds lay apart.");
        Report.Table(["world", "point", "box", "ray to the next cell", "ray from the middle of the region"], rows);

        // A whole rule, as the flood pays for it: per cell classified.
        rows.Clear();
        foreach ((CellRule rule, string ruleName) in Rules)
        {
            foreach (SolidProbe probe in new SolidProbe[] { live, bakedNative })
            {
                AirField? last = null;
                (double cold, double coldSpread) = Timing.PerCall(
                    () =>
                    {
                        last = new AirField(probe, 1f, rule);
                        Sweep(last, min, max, ref sink);
                    },
                    centers.Count,
                    runs: 7);

                AirField warmField = last!;
                (double warm, double warmSpread) = Timing.PerCall(() => Sweep(warmField, min, max, ref sink), centers.Count);

                rows.Add(
                [
                    ruleName,
                    probe.Name,
                    $"{Report.F(cold, 1)} ns, {Report.F(coldSpread, 0)}%",
                    $"{Report.F(warm, 1)} ns, {Report.F(warmSpread, 0)}%",
                    Report.F((double)warmField.PointTests / centers.Count, 2),
                    Report.F((double)warmField.SegmentTests / centers.Count, 2),
                    Report.F((double)warmField.BoxTests / centers.Count, 2),
                ]);
            }
        }

        Report.Note("A rule per cell: first time (cold) and from the kept answer (warm).");
        Report.Table(
            ["rule", "world", "cold", "warm", "points / cell", "rays / cell", "boxes / cell"],
            rows);

        GC.KeepAlive(sink);
    }

    private static void Sweep(AirField field, Vector3 min, Vector3 max, ref int sink)
    {
        field.CellOf(min, out int x0, out int y0, out int z0);
        field.CellOf(max - new Vector3(0.5f), out int x1, out int y1, out int z1);

        for (int z = z0; z <= z1; z++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++) sink += field.Flags(x, y, z);
            }
        }
    }

    // How each rule calls a cell, against 64 points spread through it.
    private static void Agreement(CsgWorld world)
    {
        Report.Section("Cell tests: what each rule calls a cell, the demo's course");
        Report.Note("Truth is 64 points inside the cell: all in air, all in solid, or some of each (cut).");

        var probe = new LiveProbe(world);
        var rows = new List<string[]>();

        foreach (float cell in Accuracy.CellSizes)
        {
            var fields = new AirField[Rules.Length];
            for (int i = 0; i < fields.Length; i++) fields[i] = new AirField(probe, cell, Rules[i].Rule);

            int air = 0, solid = 0, cut = 0;
            var cutAsAir = new int[Rules.Length];
            var airAsSolid = new int[Rules.Length];
            var solidAsAir = new int[Rules.Length];

            fields[0].CellOf(new Vector3(118f, -2f, -22f), out int x0, out int y0, out int z0);
            fields[0].CellOf(new Vector3(171.9f, 5.9f, 21.9f), out int x1, out int y1, out int z1);

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int inSolid = 0;
                        for (int k = 0; k < 64; k++)
                        {
                            var inside = new Vector3(((k & 3) + 0.5f) / 4f, (((k >> 2) & 3) + 0.5f) / 4f, ((k >> 4) + 0.5f) / 4f);
                            if (probe.IsSolid((new Vector3(x, y, z) + inside) * cell)) inSolid++;
                        }

                        if (inSolid == 0) air++;
                        else if (inSolid == 64) solid++;
                        else cut++;

                        for (int i = 0; i < fields.Length; i++)
                        {
                            bool calledAir = fields[i].IsAir(x, y, z);
                            if (inSolid == 0 && !calledAir) airAsSolid[i]++;
                            else if (inSolid == 64 && calledAir) solidAsAir[i]++;
                            else if (inSolid is > 0 and < 64 && calledAir) cutAsAir[i]++;
                        }
                    }
                }
            }

            for (int i = 0; i < Rules.Length; i++)
            {
                if (Rules[i].Rule == CellRule.CenterAndLinks) continue;

                rows.Add(
                [
                    Report.F(cell, 1),
                    Rules[i].Name,
                    air.ToString(),
                    solid.ToString(),
                    cut.ToString(),
                    Report.Percent((double)cutAsAir[i] / Math.Max(cut, 1)),
                    airAsSolid[i].ToString(),
                    solidAsAir[i].ToString(),
                ]);
            }
        }

        Report.Table(
            ["cell", "rule", "air cells", "solid cells", "cut cells", "cut called air", "air called solid", "solid called air"],
            rows);
    }

    // Two sealed rooms and a wall one thick with a doorway 2.2 high. The
    // doorway is slid along the wall, since a level's door can stand anywhere
    // against the grid.
    private static void Doorway()
    {
        Report.Section("Cell tests: when a doorway closes");
        Report.Note("Share of 16 door positions, an eighth of a unit apart, at which a flood gets from one room to the other.");

        float[] widths = [0.7f, 1.0f, 1.4f, 2.0f, 2.8f];
        var rows = new List<string[]>();
        var flood = new Flood();
        var reader = new PathReader(flood);

        foreach (float cell in Accuracy.CellSizes)
        {
            foreach ((CellRule rule, string ruleName) in Rules)
            {
                if (rule == CellRule.Center) continue;

                var row = new List<string> { Report.F(cell, 1), ruleName };
                foreach (float width in widths)
                {
                    int open = 0;
                    for (int k = 0; k < 16; k++)
                    {
                        float slide = k * 0.125f;
                        var b = new WorldBuilder { Offset = HandCase.Shift + new Vector3(0f, 0f, slide) };
                        b.Box(new Vector3(-4f, -2f, -6f), new Vector3(29f, 5f, 26f));
                        b.Cut(new Vector3(0f, 0f, -2f), new Vector3(12f, 3f, 20f));
                        b.Cut(new Vector3(13f, 0f, -2f), new Vector3(25f, 3f, 20f));
                        b.Cut(new Vector3(12f, 0f, 10f - (width * 0.5f)), new Vector3(13f, 2.2f, 10f + (width * 0.5f)));

                        var field = new AirField(new LiveProbe(b.Build()), cell, rule);
                        Vector3 listener = new Vector3(6f, 1.5f, 10f) + b.Offset;
                        Vector3 target = new Vector3(19f, 1.5f, 10f) + b.Offset;

                        flood.Run(field, PartSet.Empty, listener, new FloodOptions { Algorithm = FloodAlgorithm.Bfs6, Radius = 60f });
                        if (reader.Read(target, bends: false).Found) open++;
                    }

                    row.Add(Report.Percent(open / 16.0, 0));
                }

                rows.Add(row.ToArray());
            }
        }

        Report.Table(["cell", "rule", "0.7 wide", "1.0 wide", "1.4 wide", "2.0 wide", "2.8 wide"], rows);
    }

    // Two sealed rooms with a wall between them and no opening. A flood that
    // gets across has leaked.
    private static void ThinWall()
    {
        Report.Section("Cell tests: when a thin wall leaks");
        Report.Note("Share of 8 wall positions, a quarter of a unit apart, at which a flood crosses a wall with no opening.");
        Report.Note("Each cell: 6 neighbours / 26 neighbours.");

        float[] thicknesses = [0.1f, 0.3f, 0.5f, 1.0f];
        float[] angles = [0f, 20f, 45f];
        var flood = new Flood();
        var reader = new PathReader(flood);

        foreach (float angle in angles)
        {
            var rows = new List<string[]>();
            Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle * MathF.PI / 180f);
            Vector3 normal = Vector3.Transform(Vector3.UnitX, turn);

            foreach (float cell in Accuracy.CellSizes)
            {
                foreach ((CellRule rule, string ruleName) in Rules)
                {
                    var row = new List<string> { Report.F(cell, 1), ruleName };

                    foreach (float thickness in thicknesses)
                    {
                        int leaks6 = 0, leaks26 = 0;

                        for (int k = 0; k < 8; k++)
                        {
                            // Two rooms cut side by side. The wall is what
                            // is left between them: a brush put into a cut
                            // would be cut away with it.
                            var b = new WorldBuilder { Offset = HandCase.Shift + new Vector3(k * 0.25f, 0f, 0f) };
                            b.Box(new Vector3(-12f, -2f, -12f), new Vector3(32f, 5f, 32f));

                            var middle = new Vector3(10f, 1.5f, 10f);
                            var roomHalf = new Vector3(4f, 1.5f, 6f);
                            Vector3 near = middle - (normal * ((thickness * 0.5f) + roomHalf.X));
                            Vector3 far = middle + (normal * ((thickness * 0.5f) + roomHalf.X));
                            b.RotatedBox(near, roomHalf, turn, cut: true);
                            b.RotatedBox(far, roomHalf, turn, cut: true);

                            var field = new AirField(new LiveProbe(b.Build()), cell, rule);
                            Vector3 listener = near + b.Offset;
                            Vector3 target = far + b.Offset;

                            flood.Run(field, PartSet.Empty, listener, new FloodOptions { Algorithm = FloodAlgorithm.Bfs6, Radius = 60f });
                            if (reader.Read(target, bends: false).Found) leaks6++;

                            flood.Run(field, PartSet.Empty, listener, new FloodOptions { Algorithm = FloodAlgorithm.Dijkstra26, Radius = 60f });
                            if (reader.Read(target, bends: false).Found) leaks26++;
                        }

                        row.Add($"{Report.Percent(leaks6 / 8.0, 0)} / {Report.Percent(leaks26 / 8.0, 0)}");
                    }

                    rows.Add(row.ToArray());
                }
            }

            Report.Note($"### Wall turned {angle:F0} degrees against the grid");
            Report.Table(["cell", "rule", "0.1 thick", "0.3 thick", "0.5 thick", "1.0 thick"], rows);
        }
    }
}
