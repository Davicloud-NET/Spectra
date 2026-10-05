using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Probes;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Worlds;

// A small world with a listener, a source and the true shortest path between
// them, worked out in the one plane the path lies in.
internal sealed class HandCase
{
    // Moves every case off the snap grid, so no wall face runs through the
    // centres of the air cells. The demo's own geometry is tried as it is.
    // Sixteenths, which a float holds with no rounding: at (0.31, 0.17, 0.43) a cut
    // flush with a wall came out one rounding step away from it, and rays
    // hit the film of solid left between the two. See Breaks.FlushCut.
    public static readonly Vector3 Shift = new(0.3125f, 0.1875f, 0.4375f);

    private HandCase(string name, CsgWorld world, Vector3 listener, Vector3 source, List<Vector3> exact)
    {
        Name = name;
        World = world;
        Listener = listener;
        Source = source;
        ExactPath = exact;

        for (int i = 1; i < exact.Count; i++) ExactLength += Vector3.Distance(exact[i - 1], exact[i]);
        ExactBend = PathReader.TotalBend(exact);
        ExactFirstLeg = Vector3.Normalize(exact[1] - exact[0]);
        Straight = Vector3.Distance(listener, source);
    }

    public string Name { get; }

    public CsgWorld World { get; }

    public PartSet Parts { get; } = PartSet.Empty;

    public Vector3 Listener { get; }

    public Vector3 Source { get; }

    public List<Vector3> ExactPath { get; }

    public float ExactLength { get; }

    public float ExactBend { get; }

    public Vector3 ExactFirstLeg { get; }

    public float Straight { get; }

    public static List<HandCase> All() =>
    [
        OneCorner(),
        UCorridor(),
        DoorwaySideways(),
        DoorwayOblique(),
        OverTheWall(),
    ];

    // An L-shaped corridor three wide. The path touches the inner corner.
    public static HandCase OneCorner()
    {
        var b = new WorldBuilder { Offset = Shift };
        b.Box(new Vector3(-4f, -2f, -4f), new Vector3(24f, 5f, 24f));
        b.Cut(new Vector3(0f, 0f, 0f), new Vector3(20f, 3f, 3f));
        b.Cut(new Vector3(17f, 0f, 0f), new Vector3(20f, 3f, 20f));

        FreeRect[] free = [new(0f, 0f, 20f, 3f), new(17f, 0f, 20f, 20f)];
        return Plan("one corner", b, free, new Vector2(4f, 1.5f), new Vector2(18.5f, 15f), height: 1.5f);
    }

    // Two corridors side by side, joined at one end: round the end of the
    // wall between them, two corners.
    public static HandCase UCorridor()
    {
        var b = new WorldBuilder { Offset = Shift };
        b.Box(new Vector3(-4f, -2f, -4f), new Vector3(24f, 5f, 12f));
        b.Cut(new Vector3(0f, 0f, 0f), new Vector3(20f, 3f, 3f));
        b.Cut(new Vector3(17f, 0f, 0f), new Vector3(20f, 3f, 8f));
        b.Cut(new Vector3(0f, 0f, 5f), new Vector3(20f, 3f, 8f));

        FreeRect[] free = [new(0f, 0f, 20f, 3f), new(17f, 0f, 20f, 8f), new(0f, 5f, 20f, 8f)];
        return Plan("U corridor", b, free, new Vector2(4f, 1.5f), new Vector2(4f, 6.5f), height: 1.5f);
    }

    // Two rooms and a wall one thick with the demo's doorway in it, 1.4 wide
    // and 2.2 high. Listener and source both stand off to one side of it.
    public static HandCase DoorwaySideways() =>
        Doorway("doorway, from the side", new Vector2(10f, 2f), new Vector2(15f, 4f));

    // The same rooms, with the straight line just missing the doorway.
    public static HandCase DoorwayOblique() =>
        Doorway("doorway, nearly in line", new Vector2(8f, 2f), new Vector2(17f, 18f));

    public static WorldBuilder DoorwayRooms(out FreeRect[] free)
    {
        var b = new WorldBuilder { Offset = Shift };
        b.Box(new Vector3(-4f, -2f, -4f), new Vector3(29f, 5f, 24f));
        b.Cut(new Vector3(0f, 0f, 0f), new Vector3(12f, 3f, 20f));
        b.Cut(new Vector3(13f, 0f, 0f), new Vector3(25f, 3f, 20f));
        b.Cut(new Vector3(12f, 0f, 9.3f), new Vector3(13f, 2.2f, 10.7f));

        free = [new(0f, 0f, 12f, 20f), new(13f, 0f, 25f, 20f), new(12f, 9.3f, 13f, 10.7f)];
        return b;
    }

    // A wall 2.5 high and one thick standing in the open. The path goes over
    // the top, in the upright plane through listener and source.
    public static HandCase OverTheWall()
    {
        WorldBuilder b = FreeStandingWall();

        FreeRect[] free =
        [
            new(-40f, 0f, -0.5f, 30f),
            new(-40f, 2.5f, 40f, 30f),
            new(0.5f, 0f, 40f, 30f),
        ];

        var listener = new Vector2(-6f, 1.6f);
        var source = new Vector2(4f, 1.25f);
        List<Vector2> flat = ExactPath2D.Solve(free, listener, source);

        var exact = new List<Vector3>();
        foreach (Vector2 p in flat) exact.Add(new Vector3(p.X, p.Y, 0f) + Shift);

        return new HandCase("over a wall in the open", b.Build(), exact[0], exact[^1], exact);
    }

    public static WorldBuilder FreeStandingWall()
    {
        var b = new WorldBuilder { Offset = Shift };
        b.Box(new Vector3(-40f, -1f, -40f), new Vector3(40f, 0f, 40f));
        b.Box(new Vector3(-0.5f, 0f, -30f), new Vector3(0.5f, 2.5f, 30f));
        return b;
    }

    private static HandCase Doorway(string name, Vector2 listener, Vector2 source)
    {
        WorldBuilder b = DoorwayRooms(out FreeRect[] free);
        return Plan(name, b, free, listener, source, height: 1.5f);
    }

    // A case whose path lies in the floor plan: x and z, at one height.
    private static HandCase Plan(
        string name, WorldBuilder b, FreeRect[] free, Vector2 listener, Vector2 source, float height)
    {
        List<Vector2> flat = ExactPath2D.Solve(free, listener, source);

        var exact = new List<Vector3>();
        foreach (Vector2 p in flat) exact.Add(new Vector3(p.X, height, p.Y) + Shift);

        return new HandCase(name, b.Build(), exact[0], exact[^1], exact);
    }
}
