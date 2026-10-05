using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SoundCornersSpike.Questions;

// Question 8 and the part of question 1 about doors: the demo's start room
// with its door shut and open, the straight line against the flood's path.
internal static class Doors
{
    // Standing in the course, east of the room's wall.
    private static readonly (string Name, Vector3 Position)[] Listeners =
    [
        ("off to the side of the door", new Vector3(134f, 1.6f, 3f)),
        ("in line with the door", new Vector3(133f, 1.6f, 0f)),
        ("far along the wall", new Vector3(134f, 1.6f, 10f)),
    ];

    // The three ways of flooding whose choice of route is compared.
    private static readonly Method[] Compared = [Accuracy.Methods[7], Accuracy.Methods[8], Accuracy.Methods[5]];

    public static void Run()
    {
        StartRoom();
        OpeningDoor();
        SoundsInParts();
        PartCost();
    }

    private static void StartRoom()
    {
        Report.Section("The demo's start room: the wall path against the flood's path");
        Report.Note("As authored the room has no roof: its walls are 2.5 high and the course is open to the sky.");
        Report.Note("Roofed is the same room with a lid the spike adds, so the doorway is the only way out.");

        // The floor plan at ear height, east of the room, for the hand value.
        FreeRect[] plan =
        [
            new(122f, -4f, 130f, 4f),
            new(130f, -0.7f, 131f, 0.7f),
            new(131f, -19f, 135f, 19f),
        ];

        var rows = new List<string[]>();
        var mix = new List<string[]>();
        var flood = new Flood();

        foreach (bool roofed in new[] { false, true })
        {
            DemoWorld shut = DemoWorld.Build(doorOpen: 0f, roofed);
            DemoWorld open = DemoWorld.Build(doorOpen: DemoWorld.DoorTravel, roofed);
            DemoSound tone = shut.Sound(DemoPlayArea.RoomToneName);
            int door = shut.Parts.IndexOf(DemoPlayArea.StartDoorName);
            var live = new LiveProbe(shut.World);

            foreach ((string name, Vector3 listener) in Listeners)
            {
                float straight = Vector3.Distance(listener, tone.Position);
                bool wallInWay = live.SegmentBlocked(listener, tone.Position);
                bool doorInWay = shut.Parts.Hulls[door].Blocks(listener, tone.Position);

                List<Vector2> flat = ExactPath2D.Solve(
                    plan, new Vector2(listener.X, listener.Z), new Vector2(tone.Position.X, tone.Position.Z));
                float rise = listener.Y - tone.Position.Y;
                float throughDoor = MathF.Sqrt((ExactPath2D.Length(flat) * ExactPath2D.Length(flat)) + (rise * rise));

                var row = new List<string>
                {
                    roofed ? "roofed" : "as authored",
                    name,
                    Report.F(straight),
                    wallInWay ? "wall" : doorInWay ? "door" : "nothing",
                    Report.F(throughDoor),
                    roofed ? "none" : Report.F(OverWall(listener, tone.Position)),
                };

                foreach (Method method in Compared)
                {
                    foreach (float cell in new[] { 0.5f, 1f })
                    {
                        FloodOptions options = method.Options(radius: 30f);
                        options.MinY = -4f;
                        options.MaxY = 6f;
                        PathReader reader = method.Reader(flood);
                        var field = new AirField(live, cell, CellRule.CenterAndLinks);

                        flood.Run(field, shut.Parts, listener, options);
                        PathAnswer whenShut = reader.Read(tone.Position, refine: true);

                        flood.Run(field, open.Parts, listener, options);
                        PathAnswer whenOpen = reader.Read(tone.Position, refine: true);

                        row.Add($"{Describe(whenShut)} / {Describe(whenOpen)}");

                        if (cell == 1f && method == Accuracy.Chosen)
                            mix.Add(TwoPaths(roofed, name, listener, tone, shut, wallInWay, doorInWay, whenShut, whenOpen));
                    }
                }

                rows.Add(row.ToArray());
            }
        }

        var header = new List<string>
        {
            "room", "listener", "straight", "in the way", "by hand, doorway", "by hand, over the wall",
        };
        foreach (Method method in Compared)
        {
            header.Add($"{method.Name}, cell 0.5");
            header.Add($"{method.Name}, cell 1");
        }

        Report.Note("Flood cells: door shut / door open. Each is the path length and the way it goes.");
        Report.Table(header, rows);

        Report.Note("### The two paths as the engine would play them");
        Report.Note("RoomTone: full volume to 6, silent from 16. The wall is taken as the generic preset, the door as wood.");
        Report.Note("The air path's loudness here is distance alone. What a bend should cost is a decision, not a measurement.");
        Report.Table(
            [
                "room", "listener", "wall path: solid crossed", "wall path: gain, dB", "wall path: high end, dB",
                "air path, door shut: gain, dB", "air path, door open: gain, dB", "air path, door open: bend from the two ends, deg",
            ],
            mix);
    }

    private static string[] TwoPaths(
        bool roofed, string name, Vector3 listener, DemoSound tone, DemoWorld shut,
        bool wallInWay, bool doorInWay, PathAnswer whenShut, PathAnswer whenOpen)
    {
        float straight = Vector3.Distance(listener, tone.Position);
        float falloff = SoundFalloff.Gain(straight, tone.MinDistance, tone.MaxDistance);

        // How much solid the straight line crosses: in from one end, in from
        // the other, and the stretch between the two hits.
        var loss = default(AcousticLoss);
        string crossed = "nothing";

        if (wallInWay)
        {
            CsgWorld world = shut.World;
            world.Raycast(listener, tone.Position - listener, straight, out BspRaycastHit near);
            world.Raycast(tone.Position, listener - tone.Position, straight, out BspRaycastHit far);
            float thickness = MathF.Max(0f, straight - near.Distance - far.Distance);

            loss += AcousticPresets.Generic.LossThrough(thickness);
            crossed = $"{Report.F(thickness)} of wall";
        }
        else if (doorInWay)
        {
            loss += AcousticPresets.Wood.LossThrough(0.8f);
            crossed = "0.80 of door";
        }

        AcousticGains gains = loss.ToGains();

        return
        [
            roofed ? "roofed" : "as authored",
            name,
            crossed,
            Decibels(falloff * gains.Gain),
            Decibels(gains.GainHf),
            whenShut.Found ? Decibels(SoundFalloff.Gain(whenShut.Length, tone.MinDistance, tone.MaxDistance)) : "no path",
            whenOpen.Found ? Decibels(SoundFalloff.Gain(whenOpen.Length, tone.MinDistance, tone.MaxDistance)) : "no path",
            whenOpen.Found ? Report.F(whenOpen.Direct ? 0f : whenOpen.EndsBendDegrees, 0) : "",
        ];
    }

    private static string Decibels(float gain) =>
        gain <= 0f ? "silent" : Report.F(20.0 * Math.Log10(gain), 1);

    // Over the room's east wall, which is one thick and 2.5 high: unfold the
    // two wall faces and the top into one plane and draw a straight line.
    private static float OverWall(Vector3 listener, Vector3 source)
    {
        const float Top = 2.5f;
        float up = MathF.Sqrt(((130f - source.X) * (130f - source.X)) + ((Top - source.Y) * (Top - source.Y)));
        float down = MathF.Sqrt(((listener.X - 131f) * (listener.X - 131f)) + ((Top - listener.Y) * (Top - listener.Y)));
        float across = up + 1f + down;
        float along = listener.Z - source.Z;
        return MathF.Sqrt((across * across) + (along * along));
    }

    private static string Describe(PathAnswer answer)
    {
        if (!answer.Found) return "no path";
        if (answer.Direct) return $"{Report.F(answer.Length)} straight";

        string way = answer.BendPoint.Y > 2.3f ? "over the wall" : "doorway";
        return $"{Report.F(answer.Length)} {way}";
    }

    // The door slides open in steps. What the flood makes of each step.
    private static void OpeningDoor()
    {
        Report.Section("An opening door, the roofed start room");
        Report.Note("The door slides 1.4 along z at 3 units a second, so each row is 1/30 s.");
        Report.Note("Listener off to the side of the door. Cells: path length, the way it goes, and its bend from the two ends.");

        Vector3 listener = Listeners[0].Position;
        var flood = new Flood();
        var reader = new PathReader(flood);
        var rows = new List<string[]>();

        DemoWorld world = DemoWorld.Build(roofed: true);
        var live = new LiveProbe(world.World);
        Vector3 source = world.Sound(DemoPlayArea.RoomToneName).Position;

        var fields = new Dictionary<float, AirField>
        {
            [0.5f] = new(live, 0.5f, CellRule.CenterAndLinks),
            [1f] = new(live, 1f, CellRule.CenterAndLinks),
        };

        for (int step = 0; step <= 14; step++)
        {
            float slid = step * 0.1f;
            PartSet parts = DemoWorld.Build(slid, roofed: true).Parts;
            var row = new List<string> { Report.F(slid, 1) };

            foreach (float cell in new[] { 0.5f, 1f })
            {
                foreach (bool links in new[] { false, true })
                {
                    FloodOptions options = Bounded();
                    options.PartLinks = links;

                    flood.Run(fields[cell], parts, listener, options);
                    PathAnswer answer = reader.Read(source, refine: true);
                    row.Add(answer.Found ? $"{Describe(answer)}, {Report.F(answer.Direct ? 0f : answer.EndsBendDegrees, 0)} deg" : "no path");
                }
            }

            rows.Add(row.ToArray());
        }

        Report.Table(
            ["door slid by", "cell 0.5, cells only", "cell 0.5, with links", "cell 1, cells only", "cell 1, with links"],
            rows);

        // A door thinner than a cell, shut. The demo's is 0.8 thick.
        rows.Clear();
        foreach (float thickness in new[] { 0.8f, 0.4f, 0.2f, 0.1f })
        {
            var centred = new PartSet();
            centred.Add(PartHull.FromBox(
                new Vector3(130.5f - (thickness * 0.5f), 0f, -0.7f), new Vector3(130.5f + (thickness * 0.5f), 2.2f, 0.7f), "door"));

            // On the wall's outer face, as a door hung on one side would be.
            var flush = new PartSet();
            flush.Add(PartHull.FromBox(new Vector3(131f - thickness, 0f, -0.7f), new Vector3(131f, 2.2f, 0.7f), "door"));

            var row = new List<string> { Report.F(thickness, 1) };
            foreach (PartSet set in new[] { centred, flush })
            {
                foreach (float cell in new[] { 0.5f, 1f })
                {
                    foreach (bool links in new[] { false, true })
                    {
                        FloodOptions options = Bounded();
                        options.PartLinks = links;
                        flood.Run(fields[cell], set, listener, options);
                        row.Add(reader.Read(source).Found ? "leaks" : "holds");
                    }
                }
            }

            rows.Add(row.ToArray());
        }

        Report.Note("A shut door of a given thickness in that doorway: does the flood get through it?");
        Report.Table(
            [
                "door thickness",
                "centred: 0.5, cells", "centred: 0.5, links", "centred: 1, cells", "centred: 1, links",
                "on one face: 0.5, cells", "on one face: 0.5, links", "on one face: 1, cells", "on one face: 1, links",
            ],
            rows);
    }

    // The demo's sounds mostly sit inside the part that makes them.
    private static void SoundsInParts()
    {
        Report.Section("The demo's sounds and the parts they sit in");

        DemoWorld demo = DemoWorld.Build();
        Vector3 listener = Listeners[0].Position;
        var flood = new Flood();
        var reader = new PathReader(flood);
        var field = new AirField(new LiveProbe(demo.World), 1f, CellRule.CenterAndLinks);

        flood.Run(field, demo.Parts, listener, Bounded());

        var rows = new List<string[]>();
        foreach (DemoSound sound in demo.Sounds)
        {
            bool inside = sound.OwnPart >= 0 && demo.Parts.Hulls[sound.OwnPart].Contains(sound.Position);
            field.CellOf(sound.Position, out int x, out int y, out int z);

            bool cellBlocked = !field.IsAir(x, y, z);
            foreach (PartHull hull in demo.Parts.Hulls) cellBlocked |= hull.Contains(field.Center(x, y, z));

            PathAnswer ignoring = reader.Read(sound.Position, sound.OwnPart);
            PathAnswer blocking = reader.Read(sound.Position, ownPart: -1);

            rows.Add(
            [
                sound.Name,
                sound.OwnPart >= 0 ? demo.Parts.Hulls[sound.OwnPart].Name : "none",
                inside ? "yes" : "no",
                cellBlocked ? "no" : "yes",
                blocking.Found ? Report.F(blocking.Length) : "no path",
                ignoring.Found ? Report.F(ignoring.Length) : "no path",
            ]);
        }

        Report.Note("Cell 1. The listener is outside the room, off to the side of the door. The door is shut.");
        Report.Table(
            ["sound", "its part", "inside the part's hull", "its cell is open", "path, part blocks it", "path, own part ignored"],
            rows);
    }

    private static void PartCost()
    {
        Report.Section("What parts cost a flood");

        DemoWorld demo = DemoWorld.Build();
        Vector3 listener = Listeners[0].Position;
        var flood = new Flood();
        var field = new AirField(new LiveProbe(demo.World), 1f, CellRule.CenterAndLinks);

        // Small boxes scattered over the course, door sized.
        var random = new Random(7);
        var many = new PartSet();
        for (int i = 0; i < 200; i++)
        {
            var at = new Vector3(131f + ((float)random.NextDouble() * 38f), 0f, -19f + ((float)random.NextDouble() * 38f));
            many.Add(PartHull.FromBox(at, at + new Vector3(0.8f, 2.2f, 1.4f), "p" + i));
        }

        var rows = new List<string[]>();
        foreach ((string name, PartSet parts) in new[] { ("none", PartSet.Empty), ("the demo's own", demo.Parts), ("200 door-sized", many) })
        {
            foreach (Method method in new[] { Accuracy.Methods[7], Accuracy.Methods[8], Accuracy.Methods[5] })
            {
                FloodOptions options = method.Options(radius: 40f);
                options.MinY = -4f;
                options.MaxY = 8f;

                FloodStats stats = default;
                Timed timed = Timing.Measure(() => stats = flood.Run(field, parts, listener, options));
                rows.Add([name, parts.Count.ToString(), method.Name, stats.Settled.ToString(), timed.Ms, timed.Spread]);
            }
        }

        Report.Note("Cell 1, radius 40, cells up to height 8, the kept world answers. The parts are a flat list here,");
        Report.Note("so every step near a part and every ray is tried against every part.");
        Report.Table(["parts", "count", "flood", "cells settled", "ms", "spread"], rows);
    }

    // Every flood here stays under height 6: the room's walls are 2.5 high.
    private static FloodOptions Bounded()
    {
        FloodOptions options = Accuracy.Chosen.Options(radius: 30f);
        options.MinY = -4f;
        options.MaxY = 6f;
        return options;
    }
}
