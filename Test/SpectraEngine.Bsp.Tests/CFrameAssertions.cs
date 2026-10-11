using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

internal static class CFrameAssertions
{
    public const float Tolerance = 1e-5f;

    /// <summary>The twelve numbers of <c>GetComponents</c>.</summary>
    public static float[] Components(this CFrame frame)
    {
        var components = new float[CFrame.ComponentCount];
        frame.GetComponents(components);
        return components;
    }

    /// <summary>Position, then the rotation matrix row by row.</summary>
    public static void ShouldHaveComponents(this CFrame frame, params float[] expected)
    {
        expected.Length.ShouldBe(CFrame.ComponentCount);
        ShouldBeNear(frame.Components(), expected, Tolerance);
    }

    /// <summary>The rotation matrix row by row: R00, R01, R02, R10 and on.</summary>
    public static void ShouldHaveRotation(this CFrame frame, params float[] rows)
    {
        rows.Length.ShouldBe(9);
        ShouldBeNear(frame.Components()[3..], rows, Tolerance);
    }

    public static void ShouldBeNear(this CFrame actual, CFrame expected, float tolerance = Tolerance) =>
        ShouldBeNear(actual.Components(), expected.Components(), tolerance);

    public static void ShouldBeNear(this Vector3 actual, Vector3 expected, float tolerance = Tolerance) =>
        ShouldBeNear([actual.X, actual.Y, actual.Z], [expected.X, expected.Y, expected.Z], tolerance);

    // The slack grows with the number, so a position far out is not held to
    // more digits than a float has.
    public static void ShouldBeNear(float[] actual, float[] expected, float tolerance, string? what = null)
    {
        actual.Length.ShouldBe(expected.Length, what);
        for (int i = 0; i < expected.Length; i++)
        {
            float slack = tolerance * MathF.Max(1f, MathF.Abs(expected[i]));
            if (!(MathF.Abs(actual[i] - expected[i]) <= slack))
            {
                Assert.Fail(
                    $"{what ?? "Value"} differs at index {i}.\n" +
                    $"expected: {Format(expected)}\n" +
                    $"actual:   {Format(actual)}");
            }
        }
    }

    private static string Format(float[] values) =>
        string.Join(", ", values.Select(v => v.ToString("0.######", CultureInfo.InvariantCulture)));
}
