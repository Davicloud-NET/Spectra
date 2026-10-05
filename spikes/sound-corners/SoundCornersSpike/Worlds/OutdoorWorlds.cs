using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Worlds;

internal static class OutdoorWorlds
{
    // Where the sound in the hut is.
    public static readonly Vector3 HutSound = new Vector3(0f, 1.5f, 0f) + HandCase.Shift;

    public const float HutTop = 2.9f;

    // A flat field 200 across with one hut on it: 6 by 6 inside, walls and
    // roof 0.3 thick, the demo's doorway in the wall that faces +x.
    public static CsgWorld HutInField()
    {
        var b = new WorldBuilder { Offset = HandCase.Shift };
        b.Box(new Vector3(-100f, -1f, -100f), new Vector3(100f, 0f, 100f));
        b.Box(new Vector3(-3.3f, 0f, -3.3f), new Vector3(3.3f, HutTop, 3.3f));
        b.Cut(new Vector3(-3f, 0f, -3f), new Vector3(3f, 2.6f, 3f));
        b.Cut(new Vector3(3f, 0f, -0.7f), new Vector3(3.3f, 2.2f, 0.7f));
        return b.Build();
    }

    // A flat field with many buildings and loose walls on it, at one building
    // to every 64 square units. Fixed seed.
    public static CsgWorld Town(int brushes, out float side)
    {
        var random = new Random(20261005);
        side = MathF.Sqrt(brushes * 64f);
        float half = side * 0.5f;

        var b = new WorldBuilder { Offset = HandCase.Shift };
        b.Box(new Vector3(-half - 8f, -1f, -half - 8f), new Vector3(half + 8f, 0f, half + 8f));

        for (int i = 1; i < brushes; i++)
        {
            float x = Range(random, -half, half);
            float z = Range(random, -half, half);

            // Keep the middle clear: the listener stands there.
            if (MathF.Abs(x) < 3f && MathF.Abs(z) < 3f) x += 8f;

            if (random.NextDouble() < 0.25)
            {
                var wallHalf = new Vector3(Range(random, 2f, 5f), 1.25f, 0.15f);
                Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Range(random, 0f, MathF.PI));
                b.RotatedBox(new Vector3(x, 1.25f, z), wallHalf, turn);
            }
            else
            {
                float height = Range(random, 2f, 8f);
                var blockHalf = new Vector3(Range(random, 1f, 3f), height * 0.5f, Range(random, 1f, 3f));
                b.Box(new Vector3(x, 0f, z) - blockHalf with { Y = 0f }, new Vector3(x, height, z) + blockHalf with { Y = 0f });
            }
        }

        return b.Build();
    }

    private static float Range(Random random, float low, float high) =>
        low + ((float)random.NextDouble() * (high - low));
}
