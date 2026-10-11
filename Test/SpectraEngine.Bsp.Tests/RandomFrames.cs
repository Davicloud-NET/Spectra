using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Seeded random inputs for the CFrame tests.
internal static class RandomFrames
{
    public static float Between(Random random, float low, float high) =>
        low + (float)random.NextDouble() * (high - low);

    public static float Angle(Random random) => Between(random, -MathF.PI, MathF.PI);

    public static Vector3 Vector(Random random, float reach = 20f) =>
        new(Between(random, -reach, reach), Between(random, -reach, reach), Between(random, -reach, reach));

    // An axis that is never short enough to lose its direction.
    public static Vector3 Axis(Random random)
    {
        while (true)
        {
            Vector3 axis = Vector(random, 1f);
            if (axis.Length() > 0.1f)
                return axis;
        }
    }

    // Built from a quaternion, so the frames under test owe nothing to the
    // operations being tested.
    public static CFrame Frame(Random random)
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Axis(random)), Angle(random));
        return new CFrame(Vector(random), rotation);
    }
}
