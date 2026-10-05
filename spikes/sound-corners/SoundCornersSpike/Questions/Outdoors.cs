using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 4: what bounds the flood where nearly every cell is air.
internal static class Outdoors
{
    private const float Radius = 60f;

    public static void Run()
    {
        CsgWorld hut = OutdoorWorlds.HutInField();
        Bounds(
            "a hut in a field, the listener 15 from it on the far side from the door",
            hut, PartSet.Empty, new Vector3(-15f, 1.6f, 2f) + HandCase.Shift, [OutdoorWorlds.HutSound], [30f]);
        Bounds(
            "the same hut, the listener 30 from it",
            hut, PartSet.Empty, new Vector3(-30f, 1.6f, 5f) + HandCase.Shift, [OutdoorWorlds.HutSound], [60f]);

        HandCase wall = HandCase.OverTheWall();
        Bounds("a wall in the open, the sound behind it", wall.World, PartSet.Empty, wall.Listener, [wall.Source], [30f]);

        Demo("the demo's course, the listener outside the start room", DemoWorld.Build());

        // The worst case for the bounds: a sound with no way out at all.
        Demo("the start room roofed and its door shut, so no sound in it has a path", DemoWorld.Build(roofed: true));

        RoundTheHut(hut);
    }

    // The demo's sounds as the engine would pick them: in earshot by the
    // straight line, and with that line blocked.
    private static void Demo(string name, DemoWorld demo)
    {
        var listener = new Vector3(134f, 1.6f, 6f);
        var live = new LiveProbe(demo.World);
        var blocked = new List<Vector3>();
        var reach = new List<float>();
        var own = new List<int>();

        foreach (DemoSound sound in demo.Sounds)
        {
            if (Vector3.Distance(listener, sound.Position) >= sound.MaxDistance) continue;

            if (!live.SegmentBlocked(listener, sound.Position)
                && !demo.Parts.SegmentBlocked(listener, sound.Position, sound.OwnPart))
            {
                continue;
            }

            blocked.Add(sound.Position);
            reach.Add(sound.MaxDistance);
            own.Add(sound.OwnPart);
        }

        Bounds(
            $"{name}, {blocked.Count} of {demo.Sounds.Count} sounds in earshot and blocked",
            demo.World, demo.Parts, listener, blocked.ToArray(), reach.ToArray(), own.ToArray());
    }

    private static void Bounds(
        string name, CsgWorld world, PartSet parts, Vector3 listener, Vector3[] sounds, float[] reach,
        int[]? ownParts = null)
    {
        Report.Section($"Bounding the flood: {name}");

        var probe = new LiveProbe(world);
        var flood = new Flood();
        PathReader reader = Accuracy.Chosen.Reader(flood);

        float ceiling = 0f;
        (double ceilingNs, _) = Timing.PerCall(
            () => ceiling = WorldHeights.Ceiling(world, listener, sounds, Radius, 1f), 1);

        int sink = 0;
        (double gateNs, _) = Timing.PerCall(
            () =>
            {
                foreach (Vector3 sound in sounds)
                    sink += probe.SegmentBlocked(listener, sound) || parts.SegmentBlocked(listener, sound) ? 1 : 0;
            },
            sounds.Length);

        Report.Note($"Asking whether a sound's straight line is blocked: {Report.F(gateNs, 0)} ns a sound. "
            + $"Finding the floor and the ceiling from the brushes' boxes: {Report.F(ceilingNs / 1000.0, 1)} us, and the ceiling is {Report.F(ceiling, 2)} at cell 1.");
        Report.Note($"Radius {Radius:F0}. Reach is each sound's max distance: {string.Join(", ", reach)}.");

        (string Name, bool Ceiling, bool Exit, bool Prune, int Budget)[] variants =
        [
            ("radius only", false, false, false, 0),
            ("floor and ceiling", true, false, false, 0),
            ("stop when every sound is reached", false, true, false, 0),
            ("skip cells out of every sound's reach", false, false, true, 0),
            ("floor and ceiling, stop and reach", true, true, true, 0),
            ("those three and a budget of 20,000 cells", true, true, true, 20000),
            ("those three and a budget of 5,000 cells", true, true, true, 5000),
        ];

        foreach (float cell in new[] { 1f, 0.5f })
        {
            var rows = new List<string[]>();
            PathAnswer[]? reference = null;

            foreach (var variant in variants)
            {
                FloodOptions options = Accuracy.Chosen.Options(Radius);

                long side = (2 * ((long)MathF.Ceiling(options.Radius / cell) + 2)) + 1;
                if (!variant.Ceiling && side * side * side > 8_000_000)
                {
                    rows.Add([variant.Name, "not run: the window is " + Report.F(side * side * side * 14 / 1048576.0, 0) + " MB"]);
                    continue;
                }

                options.Targets = sounds;
                options.TargetReach = reach;
                options.EarlyExit = variant.Exit;
                options.Prune = variant.Prune;
                options.CellBudget = variant.Budget;
                if (variant.Ceiling) WorldHeights.Bound(options, world, listener, sounds, Radius, cell);

                var field = new AirField(probe, cell, CellRule.CenterAndLinks);
                FloodStats stats = default;

                Timed cold = Timing.Measure(
                    () =>
                    {
                        field = new AirField(probe, cell, CellRule.CenterAndLinks);
                        stats = flood.Run(field, parts, listener, options);
                    },
                    runs: 5,
                    warmup: 1);
                Timed warm = Timing.Measure(() => stats = flood.Run(field, parts, listener, options), runs: 11);

                var answers = new PathAnswer[sounds.Length];
                int found = 0;
                for (int i = 0; i < sounds.Length; i++)
                {
                    answers[i] = reader.Read(sounds[i], ownParts?[i] ?? -1);
                    if (answers[i].Found && answers[i].Length <= reach[i]) found++;
                }

                reference ??= answers;

                float lengthOff = 0f, legOff = 0f;
                for (int i = 0; i < sounds.Length; i++)
                {
                    if (!answers[i].Found || !reference[i].Found) continue;
                    lengthOff = MathF.Max(lengthOff, MathF.Abs(answers[i].Length - reference[i].Length));
                    legOff = MathF.Max(legOff, PathReader.AngleDegrees(answers[i].FirstLeg, reference[i].FirstLeg));
                }

                rows.Add(
                [
                    variant.Name,
                    stats.Settled.ToString(),
                    Report.F(flood.Window.MemoryBytes / 1048576.0, 1),
                    $"{warm.Ms}, {warm.Spread}",
                    $"{cold.Ms}, {cold.Spread}",
                    $"{found} of {sounds.Length}",
                    answers.Length > 0 && answers[0].Found ? Report.F(answers[0].Length) : "none",
                    Report.F(lengthOff),
                    Report.F(legOff, 1),
                    stats.BudgetHit ? "yes" : "no",
                ]);
            }

            Report.Note($"### Cell {cell:F1}");
            Report.Table(
                [
                    "bound", "cells settled", "window, MB", "warm, ms", "cold, ms", "sounds in reach found",
                    "first sound's path", "length off, units", "leg off, deg", "budget hit",
                ],
                rows);
        }

        GC.KeepAlive(sink);
    }

    // The listener walks a full circle round the hut at 5 units a second and
    // 60 frames a second, with a flood every frame.
    private static void RoundTheHut(CsgWorld hut)
    {
        Report.Section("Walking round the hut");
        Report.Note("A circle 15 from the hut's middle, 5 units a second, a flood each frame at 60 a second.");
        Report.Note("Bounded by floor and ceiling, the stop and the reach. Jumps are between one frame and the next.");
        Report.Note("Eased is the direction smoothed with a time constant of 0.1 s.");

        var rows = new List<string[]>();
        foreach (Method method in new[] { Accuracy.Methods[7], Accuracy.Methods[8], Accuracy.Methods[5] })
        {
            foreach (float cell in new[] { 1f, 0.5f })
            {
                rows.Add(HutWalk(hut, method, cell, refine: false));
                rows.Add(HutWalk(hut, method, cell, refine: true));
                rows.Add(HutWalk(hut, method, cell, refine: true, tighten: true));
            }
        }

        Report.Table(
            [
                "flood", "cell", "first leg", "frames", "no path", "largest jump, deg", "99th percentile, deg",
                "jumps over 3 deg", "changes of route", "largest jump at a change, deg", "largest jump, eased, deg",
                "largest length jump", "routes",
            ],
            rows);
    }

    private static string[] HutWalk(CsgWorld hut, Method method, float cell, bool refine, bool tighten = false)
    {
        var flood = new Flood();
        PathReader reader = method.Reader(flood);
        var field = new AirField(new LiveProbe(hut), cell, CellRule.CenterAndLinks);
        Vector3 source = OutdoorWorlds.HutSound;

        float turn = (Walk.Speed * Walk.FrameSeconds) / 15f;
        int frames = (int)(MathF.Tau / turn);

        var legs = new List<Vector3>();
        var jumps = new List<float>();
        var routes = new Dictionary<string, int>();
        string lastRoute = "";
        float lastLength = 0f, largestLengthJump = 0f, largestAtChange = 0f;
        int changes = 0, missing = 0;

        for (int frame = 0; frame < frames; frame++)
        {
            float angle = frame * turn;
            Vector3 listener = new Vector3(15f * MathF.Cos(angle), 1.6f, 15f * MathF.Sin(angle)) + HandCase.Shift;

            Vector3[] sounds = [source];
            FloodOptions options = method.Options(40f);
            options.Targets = sounds;
            options.TargetReach = [30f];
            options.EarlyExit = true;
            options.Prune = true;
            WorldHeights.Bound(options, hut, listener, sounds, 40f, cell);

            flood.Run(field, PartSet.Empty, listener, options);
            PathAnswer answer = reader.Read(source, refine: refine, tighten: tighten);
            if (!answer.Found)
            {
                missing++;
                continue;
            }

            string route = answer.Direct ? "straight through the door"
                : answer.BendPoint.Y > OutdoorWorlds.HutTop ? "over the roof"
                : "round a corner of the hut";
            routes[route] = routes.GetValueOrDefault(route) + 1;

            if (legs.Count > 0)
            {
                float jump = PathReader.AngleDegrees(legs[^1], answer.FirstLeg);
                jumps.Add(jump);
                largestLengthJump = MathF.Max(largestLengthJump, MathF.Abs(answer.Length - lastLength));

                if (route != lastRoute)
                {
                    changes++;
                    largestAtChange = MathF.Max(largestAtChange, jump);
                }
            }

            legs.Add(answer.FirstLeg);
            lastLength = answer.Length;
            lastRoute = route;
        }

        Vector3[] eased = Direction.Ease(legs.ToArray(), 0.1f);
        float largestEased = 0f;
        for (int i = 1; i < eased.Length; i++)
            largestEased = MathF.Max(largestEased, PathReader.AngleDegrees(eased[i - 1], eased[i]));

        jumps.Sort();
        var shares = new List<string>();
        foreach ((string route, int count) in routes) shares.Add($"{route} {Report.Percent((double)count / frames, 0)}");

        return
        [
            method.Name,
            Report.F(cell, 1),
            tighten ? "refined and tightened" : refine ? "refined with rays" : "cell centre",
            frames.ToString(),
            missing.ToString(),
            Report.F(jumps[^1], 1),
            Report.F(jumps[(int)(jumps.Count * 0.99)], 1),
            jumps.FindAll(j => j > 3f).Count.ToString(),
            changes.ToString(),
            Report.F(largestAtChange, 1),
            Report.F(largestEased, 1),
            Report.F(largestLengthJump),
            string.Join(", ", shares),
        ];
    }
}
