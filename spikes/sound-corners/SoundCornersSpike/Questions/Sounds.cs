using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// Question 7: one flood from the listener answers every sound.
internal static class Sounds
{
    private const int Count = 32;

    public static void Run()
    {
        Report.Section("32 sounds from one flood");

        CsgWorld town = OutdoorWorlds.Town(2000, out _);
        var live = new LiveProbe(town);
        var baked = new BakedProbe(WorldBuilder.Bake(town, nativeMemory: true), "baked, native memory");
        Vector3 listener = new Vector3(0f, 1.6f, 0f) + HandCase.Shift;

        // Fixed seed. In air, within 30 of the listener, at heights a sound
        // would be placed at.
        var random = new Random(32);
        var sounds = new List<Vector3>();
        while (sounds.Count < Count)
        {
            double angle = random.NextDouble() * Math.Tau;
            double distance = 4.0 + (random.NextDouble() * 26.0);
            var at = new Vector3(
                (float)(Math.Cos(angle) * distance), 0.5f + ((float)random.NextDouble() * 1.5f), (float)(Math.Sin(angle) * distance));
            if (!live.IsSolid(at)) sounds.Add(at);
        }

        var blocked = new List<Vector3>();
        foreach (Vector3 sound in sounds)
        {
            if (live.SegmentBlocked(listener, sound)) blocked.Add(sound);
        }

        Report.Note($"A town of {town.Placements.Count} brushes. {blocked.Count} of the {Count} sounds have their straight line blocked.");

        int sink = 0;
        var rows = new List<string[]>();

        foreach (SolidProbe probe in new SolidProbe[] { live, baked })
        {
            (double ns, double spread) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 sound in sounds) sink += probe.SegmentBlocked(listener, sound) ? 1 : 0;
                },
                Count,
                runs: 31);
            rows.Add([$"is the straight line blocked, {probe.Name}", Report.F(ns, 0) + " ns", Report.F(ns * Count / 1000.0, 1) + " us", Report.F(spread, 0) + "%"]);
        }

        Vector3[] targets = blocked.ToArray();
        var reach = new float[targets.Length];
        Array.Fill(reach, 30f);

        var flood = new Flood();
        var field = new AirField(live, 1f, CellRule.CenterAndLinks);

        foreach (Method method in new[] { Accuracy.Methods[7], Accuracy.Methods[8], Accuracy.Methods[5] })
        {
            PathReader reader = method.Reader(flood);
            FloodOptions options = method.Options(40f);
            WorldHeights.Bound(options, town, listener, targets, 40f, 1f);
            options.Targets = targets;
            options.TargetReach = reach;
            options.Prune = true;
            options.EarlyExit = true;

            FloodStats stats = default;
            Timed one = Timing.Measure(() => stats = flood.Run(field, PartSet.Empty, listener, options));
            rows.Add([$"one flood for all of them, {method.Name}, {stats.Settled} cells", "", one.Ms + " ms", one.Spread]);

            // The same flood asking the world for every cell first, live and baked.
            foreach (SolidProbe probe in new SolidProbe[] { live, baked })
            {
                Timed first = Timing.Measure(
                    () => flood.Run(new AirField(probe, 1f, CellRule.CenterAndLinks), PartSet.Empty, listener, options),
                    runs: 7,
                    warmup: 1);
                rows.Add([$"the same flood the first time, {method.Name}, {probe.Name}", "", first.Ms + " ms", first.Spread]);
            }

            // Back on the kept answers, for the reads below.
            flood.Run(field, PartSet.Empty, listener, options);

            int found = 0;
            foreach (Vector3 sound in targets)
            {
                if (reader.Read(sound, bends: false).Found) found++;
            }

            (string Name, bool Bends, bool Refine, bool Tighten)[] reads =
            [
                ("length and first leg", false, false, false),
                ("and the total bend", true, false, false),
                ("and the first leg refined with rays", true, true, false),
                ("and the first corner tightened", true, true, true),
            ];

            foreach (var read in reads)
            {
                (double ns, double spread) = Timing.PerCall(
                    () =>
                    {
                        foreach (Vector3 sound in targets)
                            sink += reader.Read(sound, bends: read.Bends, refine: read.Refine, tighten: read.Tighten).Legs;
                    },
                    targets.Length,
                    runs: 31);

                rows.Add(
                [
                    $"read a sound, {method.Name}: {read.Name} ({found} of {targets.Length} found)",
                    Report.F(ns, 0) + " ns",
                    Report.F(ns * Count / 1000.0, 1) + " us",
                    Report.F(spread, 0) + "%",
                ]);
            }

            // Between floods: a kept path aimed again from a listener that
            // has moved a step. Timed as the tightened read with and
            // without it, since the reader keeps one path at a time.
            Vector3 moved = listener + new Vector3(0.08f, 0f, 0.03f);
            (double with, double withSpread) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 sound in targets)
                    {
                        sink += reader.Read(sound, bends: true, tighten: true).Legs;
                        sink += reader.Reaim(moved).Legs;
                    }
                },
                targets.Length,
                runs: 31);
            (double without, _) = Timing.PerCall(
                () =>
                {
                    foreach (Vector3 sound in targets) sink += reader.Read(sound, bends: true, tighten: true).Legs;
                },
                targets.Length,
                runs: 31);

            rows.Add(
            [
                $"aim a kept path again from a moved listener, {method.Name}",
                Report.F(with - without, 0) + " ns",
                Report.F((with - without) * Count / 1000.0, 1) + " us",
                Report.F(withSpread, 0) + "%",
            ]);
        }

        // The alternative: a flood per sound, each stopping at its sound.
        PathReader chosenReader = Accuracy.Chosen.Reader(flood);
        Timed each = Timing.Measure(() =>
        {
            foreach (Vector3 sound in targets)
            {
                FloodOptions options = Accuracy.Chosen.Options(40f);
                WorldHeights.Bound(options, town, listener, targets, 40f, 1f);
                options.Targets = [sound];
                options.TargetReach = [30f];
                options.Prune = true;
                options.EarlyExit = true;
                flood.Run(field, PartSet.Empty, listener, options);
                sink += chosenReader.Read(sound, bends: false).Legs;
            }
        });
        rows.Add(
        [
            $"a flood for each of the {targets.Length} blocked sounds, {Accuracy.Chosen.Name}", "", each.Ms + " ms", each.Spread,
        ]);

        Report.Note("Cell 1, radius 40, floor and ceiling, reach 30, stop when every sound is reached. Kept world answers.");
        Report.Table(["step", "each", "for 32", "spread"], rows);
        GC.KeepAlive(sink);
    }
}
