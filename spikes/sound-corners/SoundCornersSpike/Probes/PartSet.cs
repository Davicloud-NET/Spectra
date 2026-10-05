using System.Collections.Generic;
using System.Numerics;

namespace SoundCornersSpike.Probes;

// The parts that stand in the way of sound, as a flat list. A real level
// needs a broadphase over them; the demo has four that collide.
internal sealed class PartSet
{
    private readonly List<PartHull> _hulls = [];

    public static PartSet Empty { get; } = new();

    public IReadOnlyList<PartHull> Hulls => _hulls;

    public int Count => _hulls.Count;

    public PartSet Add(PartHull hull)
    {
        _hulls.Add(hull);
        return this;
    }

    // ignore: the part a sound sits in must not block that sound.
    public bool SegmentBlocked(Vector3 a, Vector3 b, int ignore = -1)
    {
        for (int i = 0; i < _hulls.Count; i++)
        {
            if (i != ignore && _hulls[i].Blocks(a, b)) return true;
        }

        return false;
    }

    public int IndexOf(string name)
    {
        for (int i = 0; i < _hulls.Count; i++)
        {
            if (_hulls[i].Name == name) return i;
        }

        return -1;
    }
}
