using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Checks the span query against the world's own trees: along random segments,
// a point is inside a span when the tree says the point is solid.
// Shared by the live suite and the cooked one, which hands in a baked world.
internal static class SolidSpanOracle
{
    // How far a sample must be from any change of solid to be compared. The
    // trees are built from welded surfaces and the spans from authored planes,
    // so the two may put a boundary a little apart.
    public const float Margin = 0.02f;

    private const int SegmentsPerLevel = 48;
    private const int SamplesPerSegment = 48;

    public readonly record struct Tally(int Compared, int Skipped, int Solid);

    // World brushes only: additive and subtractive boxes, overlapping, on a
    // quarter-unit lattice so faces often share a plane, some turned off axis.
    // They straddle the origin, which is a corner of eight cells.
    public static Scene BuildLevel(int seed)
    {
        var random = new Random(seed);
        var scene = new Scene($"Oracle{seed}");
        int brushes = 10 + random.Next(8);

        for (int i = 0; i < brushes; i++)
        {
            Vector3 half = new(Lattice(random, 0.5f, 5f), Lattice(random, 0.5f, 4f), Lattice(random, 0.5f, 5f));
            Vector3 center = new(Lattice(random, -9f, 9f), Lattice(random, -5f, 5f), Lattice(random, -9f, 9f));

            SceneNode node = scene.Root.CreateChild($"Brush{i}");
            node.LocalPosition = center;

            if (random.Next(4) == 0)
            {
                node.LocalRotation = Quaternion.CreateFromYawPitchRoll(
                    random.Next(24) * (MathF.PI / 12f), random.Next(3) * (MathF.PI / 12f), 0f);
            }

            Brush brush = Brush.CreateBox(-half, half);
            node.Brush = random.Next(3) == 0 ? brush.WithOperation(BrushOperation.Subtractive) : brush;
        }

        return scene;
    }

    // Traces random segments through the level and compares each sample with
    // isSolid. Fails on the first sample that disagrees.
    public static Tally Check(Scene scene, Func<Vector3, bool> isSolid, int seed)
    {
        var random = new Random(seed ^ 0x5EED);
        var spans = new SolidSpan[64];
        int compared = 0, skipped = 0, solid = 0;

        for (int s = 0; s < SegmentsPerLevel; s++)
        {
            Vector3 from = Point(random);
            Vector3 to = Point(random);
            float length = Vector3.Distance(from, to);
            if (length < 1f)
                continue;

            int count = scene.TraceSolidSpans(from, to, spans, out bool truncated);
            truncated.ShouldBeFalse();

            Vector3 direction = (to - from) / length;
            Sideways(direction, out Vector3 right, out Vector3 up);

            for (int i = 0; i < SamplesPerSegment; i++)
            {
                float t = (i + (float)random.NextDouble()) / SamplesPerSegment * length;
                Vector3 point = from + direction * t;

                bool expected = isSolid(point);
                if (NearAChange(point, expected, isSolid, direction, right, up) ||
                    NearASpanEnd(spans.AsSpan(0, count), t))
                {
                    skipped++;
                    continue;
                }

                bool actual = InsideASpan(spans.AsSpan(0, count), t);
                actual.ShouldBe(
                    expected,
                    $"level {scene.Name}, from {from} to {to}, {t} along at {point}: " +
                    $"the tree says {(expected ? "solid" : "air")}, spans {Describe(spans.AsSpan(0, count))}");

                compared++;
                if (expected) solid++;
            }
        }

        return new Tally(compared, skipped, solid);
    }

    private static bool NearAChange(
        Vector3 point, bool here, Func<Vector3, bool> isSolid, Vector3 direction, Vector3 right, Vector3 up) =>
        isSolid(point + direction * Margin) != here || isSolid(point - direction * Margin) != here ||
        isSolid(point + right * Margin) != here || isSolid(point - right * Margin) != here ||
        isSolid(point + up * Margin) != here || isSolid(point - up * Margin) != here;

    private static bool NearASpanEnd(ReadOnlySpan<SolidSpan> spans, float t)
    {
        foreach (SolidSpan span in spans)
        {
            if (MathF.Abs(t - span.Start) < Margin || MathF.Abs(t - span.End) < Margin)
                return true;
        }

        return false;
    }

    private static bool InsideASpan(ReadOnlySpan<SolidSpan> spans, float t)
    {
        foreach (SolidSpan span in spans)
        {
            if (t > span.Start && t < span.End)
                return true;
        }

        return false;
    }

    private static string Describe(ReadOnlySpan<SolidSpan> spans)
    {
        if (spans.Length == 0)
            return "none";

        var parts = new string[spans.Length];
        for (int i = 0; i < spans.Length; i++)
            parts[i] = $"[{spans[i].Start}, {spans[i].End}]";
        return string.Join(" ", parts);
    }

    private static void Sideways(Vector3 direction, out Vector3 right, out Vector3 up)
    {
        Vector3 reference = MathF.Abs(direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        right = Vector3.Normalize(Vector3.Cross(reference, direction));
        up = Vector3.Cross(direction, right);
    }

    // A little wider than the brushes reach, so segments start and end in air
    // as often as in solid.
    private static Vector3 Point(Random random) => new(
        (float)(random.NextDouble() * 26.0 - 13.0),
        (float)(random.NextDouble() * 14.0 - 7.0),
        (float)(random.NextDouble() * 26.0 - 13.0));

    private static float Lattice(Random random, float min, float max)
    {
        int steps = (int)MathF.Round((max - min) / 0.25f);
        return min + random.Next(steps + 1) * 0.25f;
    }
}
