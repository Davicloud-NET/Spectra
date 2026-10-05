using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;
using SoundCornersSpike.Worlds;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Questions;

// A listener walking a straight line at 5 units a second, sampled at 60
// frames a second, with the true first leg and path length at every frame.
internal sealed class Walk
{
    public const float Speed = 5f;
    public const float FrameSeconds = 1f / 60f;

    private Walk(string name, CsgWorld world, Vector3 source, Vector3[] listeners, Vector3[] legs, float[] lengths)
    {
        Name = name;
        World = world;
        Source = source;
        Listeners = listeners;
        TrueLegs = legs;
        TrueLengths = lengths;
    }

    public string Name { get; }

    public CsgWorld World { get; }

    public Vector3 Source { get; }

    public Vector3[] Listeners { get; }

    public Vector3[] TrueLegs { get; }

    public float[] TrueLengths { get; }

    public int Frames => Listeners.Length;

    // A flood for one frame of the walk, bounded the way the report
    // recommends: under the ceiling, only towards the sound, and no further
    // than it takes to reach it.
    public FloodOptions Options(Method method, float cell)
    {
        Vector3[] sounds = [Source];
        FloodOptions options = method.Options(radius: 40f);
        options.Targets = sounds;
        options.TargetReach = [40f];
        options.EarlyExit = true;
        options.Prune = true;
        WorldHeights.Bound(options, World, Listeners[0], sounds, 60f, cell);
        return options;
    }

    // Past the doorway of the two sealed rooms, three units from the wall.
    // The sound is in the other room, off to one side.
    public static Walk PastDoorway()
    {
        CsgWorld world = HandCase.DoorwayRooms(out FreeRect[] free).Build();
        var source = new Vector2(16f, 4f);

        int frames = (int)(16f / (Speed * FrameSeconds)) + 1;
        var listeners = new Vector3[frames];
        var legs = new Vector3[frames];
        var lengths = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            var at = new Vector2(9f, 2f + (i * Speed * FrameSeconds));
            List<Vector2> path = ExactPath2D.Solve(free, at, source);

            listeners[i] = new Vector3(at.X, 1.5f, at.Y) + HandCase.Shift;
            lengths[i] = ExactPath2D.Length(path);
            Vector2 leg = Vector2.Normalize(path[1] - path[0]);
            legs[i] = new Vector3(leg.X, 0f, leg.Y);
        }

        return new Walk(
            "past a doorway", world, new Vector3(source.X, 1.5f, source.Y) + HandCase.Shift, listeners, legs, lengths);
    }

    // Along a wall standing in the open, six units from it, with the sound
    // behind the wall. The path goes over the top the whole way.
    public static Walk AlongWall()
    {
        CsgWorld world = HandCase.FreeStandingWall().Build();
        var source = new Vector3(4f, 1.25f, 0f);

        int frames = (int)(20f / (Speed * FrameSeconds)) + 1;
        var listeners = new Vector3[frames];
        var legs = new Vector3[frames];
        var lengths = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            var at = new Vector3(-6f, 1.6f, -10f + (i * Speed * FrameSeconds));

            // Unfold the wall's two faces and its top into one plane.
            float up = MathF.Sqrt((5.5f * 5.5f) + (0.9f * 0.9f));
            float down = MathF.Sqrt((3.5f * 3.5f) + (1.25f * 1.25f));
            float across = up + 1f + down;
            float along = source.Z - at.Z;
            var edge = new Vector3(-0.5f, 2.5f, at.Z + (along * up / across));

            listeners[i] = at + HandCase.Shift;
            lengths[i] = MathF.Sqrt((across * across) + (along * along));
            legs[i] = Vector3.Normalize(edge - at);
        }

        return new Walk("along a wall in the open", world, source + HandCase.Shift, listeners, legs, lengths);
    }
}
