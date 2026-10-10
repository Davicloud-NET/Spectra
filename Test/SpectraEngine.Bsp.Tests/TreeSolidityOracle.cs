using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

// Checks a compiled world's trees against the level's own brushes: a point is
// solid where SolidSpanOracle.CarveRule says so, and a ray stops where that
// first holds. For levels of whole-number boxes: every sample is set off the
// whole numbers, so none lies in a face. Shared by the live suite and the
// cooked one, which hands in a baked world.
internal static class TreeSolidityOracle
{
    public delegate bool RayQuery(Vector3 origin, Vector3 direction, float maxDistance, out BspRaycastHit hit);

    public readonly record struct Tally(int Compared, int Solid)
    {
        public Tally Plus(Tally other) => new(Compared + other.Compared, Solid + other.Solid);

        public override string ToString() => $"compared {Compared}, solid {Solid}";
    }

    private static readonly (Vector3 Min, Vector3 Max)[] FiveBoxCorners =
    [
        (new(-10f, -5f, 2f), new(-1f, -1f, 11f)),
        (new(-4f, -6f, -3f), new(2f, -4f, 2f)),
        (new(0f, -6f, -5f), new(4f, -3f, -2f)),
        (new(-4f, -2f, -9f), new(0f, 2f, 1f)),
        (new(-9f, -6f, -6f), new(0f, 2f, 1f)),
    ];

    // Five additive boxes round a corner of eight cells. The second reaches
    // into the cell toward +x and +z, and part of its face toward -z is buried
    // in the third, which is not resident in that cell.
    public static Scene FiveBoxes(Vector3 corner = default)
    {
        var scene = new Scene("FiveBoxes");

        for (int i = 0; i < FiveBoxCorners.Length; i++)
        {
            (Vector3 min, Vector3 max) = FiveBoxCorners[i];
            Vector3 half = (max - min) * 0.5f;

            SceneNode node = scene.Root.CreateChild($"Box{i}");
            node.LocalPosition = corner + min + half;
            node.Brush = Brush.CreateBox(-half, half);
        }

        return scene;
    }

    // Every point of a half-unit lattice over the level's brushes.
    public static Tally ComparePoints(Scene level, Func<Vector3, bool> containsPoint)
    {
        Func<Vector3, bool> rule = SolidSpanOracle.CarveRule(level);
        (Vector3 min, Vector3 max) = Extent(level);

        // Counted by hand: an assertion for each point would take most of the run.
        int compared = 0, solid = 0, wrong = 0;
        Vector3 first = default;

        for (float x = min.X + 0.2f; x < max.X; x += 0.5f)
        {
            for (float y = min.Y + 0.2f; y < max.Y; y += 0.5f)
            {
                for (float z = min.Z + 0.2f; z < max.Z; z += 0.5f)
                {
                    var point = new Vector3(x, y, z);
                    bool expected = rule(point);
                    if (containsPoint(point) != expected && wrong++ == 0)
                        first = point;

                    compared++;
                    if (expected) solid++;
                }
            }
        }

        wrong.ShouldBe(0, $"level {level.Name}: {wrong} of {compared} points differ from the brushes, first at {first}");
        return new Tally(compared, solid);
    }

    // Rays along each axis, both ways, from outside the level through it, a
    // unit apart. Solid counts the rays the brushes stop.
    public static Tally CompareRays(Scene level, RayQuery raycast)
    {
        Func<Vector3, bool> rule = SolidSpanOracle.CarveRule(level);
        List<Plane> planes = WorldPlanes(level);
        (Vector3 min, Vector3 max) = Extent(level);

        int compared = 0, stopped = 0, wrong = 0;
        string first = "";

        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 along = Axis(axis), right = Axis((axis + 1) % 3), up = Axis((axis + 2) % 3);
            float reach = Vector3.Dot(max - min, along);

            for (float a = Vector3.Dot(min, right) + 0.3f; a < Vector3.Dot(max, right); a += 1f)
            {
                for (float b = Vector3.Dot(min, up) + 0.3f; b < Vector3.Dot(max, up); b += 1f)
                {
                    Vector3 across = right * a + up * b;
                    Cast(across + along * (Vector3.Dot(min, along) + 0.3f), along, reach);
                    Cast(across + along * (Vector3.Dot(max, along) - 0.3f), -along, reach);
                }
            }
        }

        wrong.ShouldBe(0, $"level {level.Name}: {wrong} of {compared} rays differ from the brushes, first {first}");
        return new Tally(compared, stopped);

        void Cast(Vector3 origin, Vector3 direction, float maxDistance)
        {
            float? expected = FirstSolid(rule, planes, origin, direction, maxDistance);
            bool hit = raycast(origin, direction, maxDistance, out BspRaycastHit actual);

            compared++;
            if (expected.HasValue) stopped++;

            if (Agree(expected, hit, actual, direction) || wrong++ > 0)
                return;

            first = $"from {origin} along {direction}: the brushes " +
                (expected is { } at ? $"start {at} along" : "are not in its way") +
                (hit ? $", the world stops it {actual.Distance} along facing {actual.Normal}" : ", the world lets it through");
        }
    }

    // The boxes stand square to the axes, so a ray along one meets a face head on.
    private static bool Agree(float? expected, bool hit, BspRaycastHit actual, Vector3 direction)
    {
        if (expected is not { } distance)
            return !hit;

        return hit
            && MathF.Abs(actual.Distance - distance) <= 1e-3f
            && Vector3.Dot(actual.Normal, direction) <= -0.999f;
    }

    // How far along the ray the carve rule first says solid. The rule can
    // change only where the ray crosses a brush plane, so one sample between
    // each two crossings reads the whole ray.
    private static float? FirstSolid(
        Func<Vector3, bool> rule, List<Plane> planes, Vector3 origin, Vector3 direction, float maxDistance)
    {
        var crossings = new List<float> { 0f, maxDistance };
        foreach (Plane plane in planes)
        {
            float speed = Vector3.Dot(plane.Normal, direction);
            if (MathF.Abs(speed) < 1e-6f)
                continue;

            float distance = -Plane.DotCoordinate(plane, origin) / speed;
            if (distance > 0f && distance < maxDistance)
                crossings.Add(distance);
        }

        crossings.Sort();

        for (int i = 0; i + 1 < crossings.Count; i++)
        {
            float from = crossings[i], to = crossings[i + 1];

            // Two brushes sharing a plane cross at the same distance, give or take rounding.
            if (to - from < 1e-3f)
                continue;

            if (rule(origin + direction * ((from + to) * 0.5f)))
                return from;
        }

        return null;
    }

    private static List<Plane> WorldPlanes(Scene level)
    {
        var planes = new List<Plane>();
        foreach (SceneNode node in level.Root.Children)
        {
            if (node.Brush is not { } brush)
                continue;

            foreach (Plane plane in brush.LocalPlanes)
                planes.Add(Plane.Transform(plane, node.WorldMatrix));
        }

        return planes;
    }

    // The brushes' box, on whole numbers and a unit wider all round, so the
    // lattice and the rays start in air.
    private static (Vector3 Min, Vector3 Max) Extent(Scene level)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (SceneNode node in level.Root.Children)
        {
            if (node.Brush is not { } brush)
                continue;

            Aabb bounds = brush.LocalBounds.Transform(node.WorldMatrix);
            min = Vector3.Min(min, bounds.Min);
            max = Vector3.Max(max, bounds.Max);
        }

        return (Round(min) - Vector3.One, Round(max) + Vector3.One);
    }

    private static Vector3 Round(Vector3 value) =>
        new(MathF.Round(value.X), MathF.Round(value.Y), MathF.Round(value.Z));

    private static Vector3 Axis(int axis) => axis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };
}
