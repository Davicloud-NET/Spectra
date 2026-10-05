using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;

namespace SoundCornersSpike.Questions;

// Questions 2 and 6: path length, first leg and total bend of each flood
// against the hand solution.
internal static class Accuracy
{
    public static readonly Method[] Methods =
    [
        new(FloodAlgorithm.Bfs6, Sight.Grid, "breadth first, 6"),
        new(FloodAlgorithm.Dijkstra26, Sight.Grid, "Dijkstra, 26"),
        new(FloodAlgorithm.Marching, Sight.Grid, "fast marching"),
        new(FloodAlgorithm.Theta26, Sight.Grid, "Theta*, 26, grid sight"),
        new(FloodAlgorithm.Theta6, Sight.Grid, "Theta*, 6, grid sight"),
        new(FloodAlgorithm.Theta6, Sight.RayFromListener, "Theta*, 6, ray to listener"),
        new(FloodAlgorithm.Theta6, Sight.Ray, "Theta*, 6, rays"),
        new(FloodAlgorithm.Bfs6, Sight.Grid, "breadth first, 6, pulled with rays", Sight.Ray),
        new(FloodAlgorithm.Dijkstra26, Sight.Grid, "Dijkstra, 26, pulled with rays", Sight.Ray),
    ];

    // The one the sections that try only one use. --chosen=<index> picks
    // another from Methods.
    public static Method Chosen { get; set; } = Methods[5];

    public static readonly float[] CellSizes = [0.5f, 1f, 2f];

    public static void Run()
    {
        Report.Section("Hand cases: the true paths");

        List<HandCase> cases = HandCase.All();
        var rows = new List<string[]>();
        foreach (HandCase c in cases)
        {
            rows.Add(
            [
                c.Name,
                Report.F(c.Straight),
                Report.F(c.ExactLength),
                (c.ExactPath.Count - 2).ToString(),
                Report.F(c.ExactBend, 1),
                Report.F(c.ExactLength / c.Straight),
            ]);
        }

        Report.Table(["case", "straight", "true path", "corners", "true bend, deg", "path / straight"], rows);

        Report.Section("Hand cases: each flood against the true path");
        Report.Note("Length error is (flood - true) / true. Leg is the angle between the flood's first leg and the true one.");
        Report.Note("Leg, rays is the same after the first leg is refined with rays against the world.");
        Report.Note("Leg, tightened is after the first corner is also swung to where the way over is shortest.");

        var flood = new Flood();

        // Worst case per method and cell size, over the cases.
        var worst = new Dictionary<(string, float), (float Length, float Leg, float Refined, float Tight, float Bend, float Ends)>();

        foreach (HandCase c in cases)
        {
            rows.Clear();
            var probe = new LiveProbe(c.World);

            foreach (float cell in CellSizes)
            {
                var field = new AirField(probe, cell, CellRule.CenterAndLinks);

                foreach (Method method in Methods)
                {
                    flood.Run(field, c.Parts, c.Listener, method.Options(radius: 40f));
                    PathReader reader = method.Reader(flood);

                    PathAnswer plain = reader.Read(c.Source);
                    PathAnswer refined = reader.Read(c.Source, refine: true);
                    PathAnswer swung = reader.Read(c.Source, tighten: true);

                    if (!plain.Found)
                    {
                        rows.Add([Report.F(cell, 1), method.Name, "no path"]);
                        continue;
                    }

                    float lengthError = (plain.Length - c.ExactLength) / c.ExactLength * 100f;
                    float leg = PathReader.AngleDegrees(plain.FirstLeg, c.ExactFirstLeg);
                    float refinedLeg = PathReader.AngleDegrees(refined.FirstLeg, c.ExactFirstLeg);
                    float tightLeg = PathReader.AngleDegrees(swung.FirstLeg, c.ExactFirstLeg);
                    float bendError = plain.BendDegrees - c.ExactBend;

                    rows.Add(
                    [
                        Report.F(cell, 1),
                        method.Name,
                        Report.F(plain.Length),
                        Signed(lengthError) + "%",
                        Report.F(leg, 1),
                        Report.F(refinedLeg, 1),
                        Report.F(tightLeg, 1),
                        Report.F(plain.BendDegrees, 1),
                        Report.F(refined.EndsBendDegrees, 1),
                        Report.F(plain.Length - c.Straight),
                        (plain.Legs - 1).ToString(),
                    ]);

                    worst.TryGetValue((method.Name, cell), out var soFar);
                    worst[(method.Name, cell)] = (
                        System.MathF.Max(soFar.Length, System.MathF.Abs(lengthError)),
                        System.MathF.Max(soFar.Leg, leg),
                        System.MathF.Max(soFar.Refined, refinedLeg),
                        System.MathF.Max(soFar.Tight, tightLeg),
                        System.MathF.Max(soFar.Bend, System.MathF.Abs(bendError)),
                        System.MathF.Max(soFar.Ends, System.MathF.Abs(refined.EndsBendDegrees - c.ExactBend)));
                }
            }

            Report.Note($"### {c.Name}: true length {Report.F(c.ExactLength)}, true bend {Report.F(c.ExactBend, 1)} deg, "
                + $"true detour {Report.F(c.ExactLength - c.Straight)}");
            Report.Table(
                [
                    "cell", "flood", "length", "length error", "leg, deg", "leg, rays, deg", "leg, tightened, deg",
                    "bend, corner by corner, deg",
                    "bend, from the two ends, deg", "detour", "corners",
                ],
                rows);
        }

        Report.Note("### Worst over the five cases");
        rows.Clear();
        foreach (float cell in CellSizes)
        {
            foreach (Method method in Methods)
            {
                if (!worst.TryGetValue((method.Name, cell), out var w)) continue;

                rows.Add(
                [
                    Report.F(cell, 1), method.Name, Report.F(w.Length, 1) + "%", Report.F(w.Leg, 1),
                    Report.F(w.Refined, 1), Report.F(w.Tight, 1), Report.F(w.Bend, 1), Report.F(w.Ends, 1),
                ]);
            }
        }

        Report.Table(
            [
                "cell", "flood", "length error", "leg, deg", "leg, rays, deg", "leg, tightened, deg",
                "bend error, corner by corner, deg",
                "bend error, from the two ends, deg",
            ],
            rows);

        Baked(cases);
    }

    // The same floods on the same worlds baked. The flat trees are the live
    // ones flattened, so the paths have to come out the same.
    private static void Baked(List<HandCase> cases)
    {
        Report.Section("The same floods on a baked world");

        var tally = new BakedTally();
        foreach (HandCase c in cases) tally.Compare(c.World, c.Parts, c.Listener, c.Source, ownPart: -1);

        DemoWorld demo = DemoWorld.Build();
        foreach (DemoSound sound in demo.Sounds)
            tally.Compare(demo.World, demo.Parts, new Vector3(134f, 1.6f, 3f), sound.Position, sound.OwnPart);

        Report.Note("The five hand cases and the demo's seven sounds, three cell sizes, every flood, read tightened.");
        Report.Table(
            [
                "answers compared", "found on one and not the other", "largest difference in length",
                "first leg not the same to the last digit", "largest difference in first leg, deg",
            ],
            [[
                tally.Asked.ToString(), tally.FoundOnOne.ToString(), tally.WorstLength.ToString("G3"),
                tally.LegDiffers.ToString(), tally.WorstLeg.ToString("G3"),
            ]]);
    }

    public static string Signed(double value) => (value >= 0 ? "+" : "") + value.ToString("F1");

    private sealed class BakedTally
    {
        private readonly Flood _liveFlood = new();
        private readonly Flood _bakedFlood = new();

        public int Asked { get; private set; }

        public int FoundOnOne { get; private set; }

        public int LegDiffers { get; private set; }

        public float WorstLength { get; private set; }

        public float WorstLeg { get; private set; }

        public void Compare(
            SpectraEngine.Core.Bsp.CsgWorld world, PartSet parts, Vector3 listener, Vector3 source, int ownPart)
        {
            var live = new LiveProbe(world);
            var baked = new BakedProbe(WorldBuilder.Bake(world, nativeMemory: true), "baked");
            Vector3[] sounds = [source];

            foreach (float cell in CellSizes)
            {
                var liveField = new AirField(live, cell, CellRule.CenterAndLinks);
                var bakedField = new AirField(baked, cell, CellRule.CenterAndLinks);

                foreach (Method method in Methods)
                {
                    FloodOptions options = method.Options(radius: 30f);
                    WorldHeights.Bound(options, world, listener, sounds, 30f, cell);

                    _liveFlood.Run(liveField, parts, listener, options);
                    _bakedFlood.Run(bakedField, parts, listener, options);

                    PathAnswer a = method.Reader(_liveFlood).Read(source, ownPart, tighten: true);
                    PathAnswer b = method.Reader(_bakedFlood).Read(source, ownPart, tighten: true);
                    Asked++;

                    if (a.Found != b.Found) FoundOnOne++;
                    if (!a.Found || !b.Found) continue;

                    float leg = PathReader.AngleDegrees(a.FirstLeg, b.FirstLeg);
                    if (a.FirstLeg != b.FirstLeg) LegDiffers++;

                    WorstLength = System.MathF.Max(WorstLength, System.MathF.Abs(a.Length - b.Length));
                    WorstLeg = System.MathF.Max(WorstLeg, leg);
                }
            }
        }
    }
}
