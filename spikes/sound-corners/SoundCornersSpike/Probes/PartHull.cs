using System;
using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Probes;

// One part as a worker thread could hold it: world planes and a box, copied
// from the node at the moment of the snapshot.
internal readonly struct PartHull(Plane[] planes, Aabb bounds, string name)
{
    public Plane[] Planes { get; } = planes;

    public Aabb Bounds { get; } = bounds;

    public string Name { get; } = name;

    public static PartHull FromBox(Vector3 min, Vector3 max, string name = "part")
    {
        Plane[] planes =
        [
            new(new Vector3(1f, 0f, 0f), -max.X),
            new(new Vector3(-1f, 0f, 0f), min.X),
            new(new Vector3(0f, 1f, 0f), -max.Y),
            new(new Vector3(0f, -1f, 0f), min.Y),
            new(new Vector3(0f, 0f, 1f), -max.Z),
            new(new Vector3(0f, 0f, -1f), min.Z),
        ];

        return new PartHull(planes, new Aabb(min, max), name);
    }

    public static PartHull FromBrush(Brush brush, Matrix4x4 world, string name)
    {
        ReadOnlySpan<Plane> local = brush.LocalPlaneSpan;
        var planes = new Plane[local.Length];
        for (int i = 0; i < planes.Length; i++)
            planes[i] = Plane.Normalize(Plane.Transform(local[i], world));

        return new PartHull(planes, brush.LocalBounds.Transform(world), name);
    }

    public PartHull MovedBy(Vector3 offset)
    {
        var planes = new Plane[Planes.Length];
        for (int i = 0; i < planes.Length; i++)
            planes[i] = new Plane(Planes[i].Normal, Planes[i].D - Vector3.Dot(Planes[i].Normal, offset));

        return new PartHull(planes, new Aabb(Bounds.Min + offset, Bounds.Max + offset), Name);
    }

    public bool Contains(Vector3 point)
    {
        for (int i = 0; i < Planes.Length; i++)
        {
            if (Plane.DotCoordinate(Planes[i], point) > 0f) return false;
        }

        return true;
    }

    // Slab clip of the segment against the hull.
    public bool Blocks(Vector3 a, Vector3 b)
    {
        float enter = 0f;
        float exit = 1f;

        for (int i = 0; i < Planes.Length; i++)
        {
            float da = Plane.DotCoordinate(Planes[i], a);
            float db = Plane.DotCoordinate(Planes[i], b);

            if (da > 0f && db > 0f) return false;
            if (da <= 0f && db <= 0f) continue;

            float t = da / (da - db);
            if (da > 0f) enter = MathF.Max(enter, t);
            else exit = MathF.Min(exit, t);

            if (enter > exit) return false;
        }

        return true;
    }
}
