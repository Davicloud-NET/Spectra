using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Checks the span query against the world's own trees: along a segment, a
// point is inside a span when the tree says there is solid all round it.
// Shared by the live suite and the cooked one, which hands in a baked world.
internal static class SolidSpanOracle
{
    // A sample is compared when the tree says the same at every corner of a
    // box this far round it. The query joins and drops what is thinner than
    // its tolerance, and the trees are built from surfaces snapped to a grid a
    // tenth of that, so nearer a boundary than this the two may differ.
    public const float Margin = 2f * SolidSpan.Tolerance;

    private const int SegmentsPerLevel = 48;
    private const int SamplesPerSegment = 48;

    public readonly record struct Tally(int Compared, int Skipped, int Solid)
    {
        public Tally Plus(Tally other) =>
            new(Compared + other.Compared, Skipped + other.Skipped, Solid + other.Solid);

        public override string ToString() => $"compared {Compared}, skipped {Skipped}, solid {Solid}";
    }

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

    // As BuildLevel, with every corner on a whole number and nothing turned,
    // so many boxes stand flush against each other.
    public static Scene BuildFlushLevel(int seed)
    {
        var random = new Random(seed);
        var scene = new Scene($"Flush{seed}");
        int brushes = 12 + random.Next(8);

        for (int i = 0; i < brushes; i++)
        {
            Vector3 min = new(random.Next(-10, 3), random.Next(-6, 1), random.Next(-10, 3));
            Vector3 half = new Vector3(1 + random.Next(10), 1 + random.Next(8), 1 + random.Next(10)) * 0.5f;

            SceneNode node = scene.Root.CreateChild($"Brush{i}");
            node.LocalPosition = min + half;

            Brush brush = Brush.CreateBox(-half, half);
            node.Brush = random.Next(3) == 0 ? brush.WithOperation(BrushOperation.Subtractive) : brush;
        }

        return scene;
    }

    // The carve's rule read off the level's brushes: a point is solid inside
    // an additive brush and inside no cut. A flush level is checked against
    // this and not its trees, which can read solid as air where boxes stand
    // flush.
    public static Func<Vector3, bool> CarveRule(Scene scene)
    {
        var brushes = new List<(Plane[] Planes, Matrix4x4 ToLocal, bool Cuts)>();
        foreach (SceneNode node in scene.Root.Children)
        {
            if (node.Brush is { } brush && Matrix4x4.Invert(node.WorldMatrix, out Matrix4x4 toLocal))
                brushes.Add(([.. brush.LocalPlanes], toLocal, brush.Operation == BrushOperation.Subtractive));
        }

        return point =>
        {
            bool solid = false;
            foreach ((Plane[] planes, Matrix4x4 toLocal, bool cuts) in brushes)
            {
                if (!Inside(planes, Vector3.Transform(point, toLocal)))
                    continue;
                if (cuts)
                    return false;
                solid = true;
            }

            return solid;
        };
    }

    // Traces random segments through the level and compares each sample with
    // isSolid. Fails on the first sample that disagrees.
    public static Tally Check(Scene scene, Func<Vector3, bool> isSolid, int seed)
    {
        var random = new Random(seed ^ 0x5EED);
        var tally = new Tally();

        for (int s = 0; s < SegmentsPerLevel; s++)
        {
            Vector3 from = Point(random);
            Vector3 to = Point(random);
            if (Vector3.Distance(from, to) >= 1f)
                tally = tally.Plus(CompareAlong(scene, isSolid, from, to, random));
        }

        return tally;
    }

    // As Check, along segments that run with an axis between whole-number
    // points. In a flush level many lie in a brush face, along an edge, or in
    // the seam of two boxes.
    public static Tally CheckWholeNumberLines(Scene scene, Func<Vector3, bool> isSolid, int seed)
    {
        var random = new Random(seed ^ 0x1A77);
        var tally = new Tally();

        for (int s = 0; s < SegmentsPerLevel * 2; s++)
        {
            Vector3 from = new(random.Next(-11, 12), random.Next(-7, 8), random.Next(-11, 12));
            Vector3 axis = random.Next(3) switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };

            // Toward the middle, where the boxes are.
            if (Vector3.Dot(from, axis) > 0f)
                axis = -axis;

            tally = tally.Plus(CompareAlong(scene, isSolid, from, from + axis * random.Next(6, 25), random));
        }

        return tally;
    }

    private static Tally CompareAlong(
        Scene scene, Func<Vector3, bool> isSolid, Vector3 from, Vector3 to, Random random)
    {
        var spans = new SolidSpan[64];
        int count = scene.TraceSolidSpans(from, to, spans, out bool truncated);
        truncated.ShouldBeFalse();

        float length = Vector3.Distance(from, to);
        Vector3 direction = (to - from) / length;
        Frame frame = Frame.Of(direction);

        int compared = 0, skipped = 0, solid = 0;
        for (int i = 0; i < SamplesPerSegment; i++)
        {
            float t = (i + (float)random.NextDouble()) / SamplesPerSegment * length;
            Vector3 point = from + direction * t;

            if (!TryReadSurroundings(point, isSolid, frame, out bool expected))
            {
                skipped++;
                continue;
            }

            bool actual = InsideASpan(spans.AsSpan(0, count), t);
            actual.ShouldBe(
                expected,
                $"level {scene.Name}, from {from} to {to}, {t} along at {point}: " +
                $"expected {(expected ? "solid" : "air")}, spans {Describe(spans.AsSpan(0, count))}");

            compared++;
            if (expected) solid++;
        }

        return new Tally(compared, skipped, solid);
    }

    // False near a change of solid. The corners are asked and not the point:
    // a point of a segment that lies in a brush face is on a plane of the
    // tree, and the tree may answer for either side of it.
    private static bool TryReadSurroundings(Vector3 point, Func<Vector3, bool> isSolid, Frame frame, out bool solid)
    {
        solid = isSolid(point + (frame.Along + frame.Right + frame.Up) * Margin);

        for (int corner = 1; corner < 8; corner++)
        {
            Vector3 offset =
                ((corner & 1) == 0 ? frame.Along : -frame.Along) +
                ((corner & 2) == 0 ? frame.Right : -frame.Right) +
                ((corner & 4) == 0 ? frame.Up : -frame.Up);

            if (isSolid(point + offset * Margin) != solid)
                return false;
        }

        return true;
    }

    private static bool Inside(Plane[] planes, Vector3 local)
    {
        foreach (Plane plane in planes)
        {
            if (Plane.DotCoordinate(plane, local) >= 0f)
                return false;
        }

        return true;
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

    // A segment's direction and two more square to it.
    private readonly record struct Frame(Vector3 Along, Vector3 Right, Vector3 Up)
    {
        public static Frame Of(Vector3 direction)
        {
            Vector3 reference = MathF.Abs(direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            Vector3 right = Vector3.Normalize(Vector3.Cross(reference, direction));
            return new Frame(direction, right, Vector3.Cross(direction, right));
        }
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
