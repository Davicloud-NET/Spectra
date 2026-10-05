using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

// A world of slabs that stand across the x axis and reach as far as any line
// goes. It counts the traces it is asked for.
internal sealed class FakeSoundObstacles : ISoundObstacles
{
    private readonly record struct Wall(float MinX, float MaxX, MaterialRef Material, SceneNode? Owner);

    private readonly List<Wall> _slabs = [];

    // False for a level nobody can hear.
    public bool HasWorld { get; set; } = true;

    // Raise it, as a compile that landed does.
    public long Revision { get; set; } = 1;

    // Traces since the test last set it to zero.
    public int Traces { get; set; }

    // Every trace's two ends and body, oldest first. Null keeps none, for a
    // test that counts allocations.
    public List<(Vector3 From, Vector3 To, SceneNode? Body)>? Asked { get; set; } = [];

    // Report at most this many spans a trace and say there were more.
    public int MostSpans { get; set; } = int.MaxValue;

    // A slab that belongs to nobody, or to the node a sound may sit under.
    public void Slab(float minX, float maxX, MaterialRef material = default, SceneNode? owner = null) =>
        _slabs.Add(new Wall(minX, maxX, material, owner));

    public void Clear() => _slabs.Clear();

    public bool TryReadWorld(out long revision)
    {
        revision = Revision;
        return HasWorld;
    }

    // Every sound is heard from where it is.
    public Vector3 HeardFrom(Vector3 from, Vector3 to, SceneNode? body) => from;

    public int Trace(Vector3 from, Vector3 to, SceneNode? body, Span<SolidSpan> spans, out bool truncated)
    {
        Traces++;
        Asked?.Add((from, to, body));
        truncated = false;

        float length = Vector3.Distance(from, to);
        int room = Math.Min(spans.Length, MostSpans);
        int count = 0;

        foreach (Wall slab in _slabs)
        {
            if (slab.Owner is not null && IsUnder(body, slab.Owner))
                continue;
            if (!TryCross(slab, from.X, to.X, length, out SolidSpan span))
                continue;

            if (count == room)
            {
                truncated = true;
                break;
            }

            spans[count++] = span;
        }

        spans[..count].Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return count;
    }

    // The stretch of a line from one x to another that lies in the slab.
    private static bool TryCross(Wall slab, float fromX, float toX, float length, out SolidSpan span)
    {
        float run = toX - fromX;
        float enter;
        float leave;

        if (run == 0f)
        {
            bool inside = fromX > slab.MinX && fromX < slab.MaxX;
            enter = inside ? 0f : 1f;
            leave = 1f;
        }
        else
        {
            float a = (slab.MinX - fromX) / run;
            float b = (slab.MaxX - fromX) / run;
            enter = Math.Clamp(Math.Min(a, b), 0f, 1f);
            leave = Math.Clamp(Math.Max(a, b), 0f, 1f);
        }

        span = new SolidSpan(enter * length, leave * length, slab.Material);
        return span.Thickness > SolidSpan.Tolerance;
    }

    private static bool IsUnder(SceneNode? node, SceneNode owner)
    {
        for (; node is not null; node = node.Parent)
        {
            if (ReferenceEquals(node, owner))
                return true;
        }

        return false;
    }
}
