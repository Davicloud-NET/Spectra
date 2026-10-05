using System.Numerics;
using SpectraEngine.Core.Bsp;

namespace SoundCornersSpike.Probes;

// What the air grid asks of the carved world. Parts are not in here.
internal abstract class SolidProbe
{
    public abstract string Name { get; }

    public abstract bool IsSolid(Vector3 point);

    // True when the straight line from a to b meets solid.
    public abstract bool SegmentBlocked(Vector3 a, Vector3 b);

    // Conservative: a box that only touches air can still come back Mixed,
    // because the box is not clipped on the way down the tree.
    public abstract BoxContent ClassifyBox(Vector3 center, Vector3 half);

    // False when no brush reaches the chunk, so every point in it is air.
    public abstract bool HasGeometry(ChunkCoord chunk);

    protected const int SeenAir = 1;
    protected const int SeenSolid = 2;

    protected static BoxContent ToContent(int seen) => seen switch
    {
        SeenAir => BoxContent.Air,
        SeenSolid => BoxContent.Solid,
        _ => BoxContent.Mixed,
    };

    // The half extent of the box along the plane's normal.
    protected static float Reach(in Plane plane, Vector3 half) =>
        (half.X * System.MathF.Abs(plane.Normal.X))
        + (half.Y * System.MathF.Abs(plane.Normal.Y))
        + (half.Z * System.MathF.Abs(plane.Normal.Z));
}
