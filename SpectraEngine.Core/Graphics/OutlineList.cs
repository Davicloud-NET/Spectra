using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>Which outline a mesh belongs to. Each group has its own colour.</summary>
public enum OutlineGroup
{
    /// <summary>Selected: the strong outline.</summary>
    Selected,

    /// <summary>Under the pointer: the faint one.</summary>
    Hovered,
}

/// <summary>One mesh to outline, and where it stands.</summary>
public readonly record struct OutlineItem(Mesh Mesh, Matrix4x4 World, OutlineGroup Group);

/// <summary>
/// The meshes to outline this frame. The renderer draws their silhouettes into
/// a mask and the resolve pass draws a line of even width round each one.
/// </summary>
// Render thread. The engine clears it every frame, so whoever wants an outline
// adds to it every frame.
public sealed class OutlineList
{
    /// <summary>
    /// How many meshes one frame may outline. The rest are counted in
    /// <see cref="Dropped"/>.
    /// </summary>
    public const int MaxItems = 512;

    private readonly List<OutlineItem> _items = [];

    /// <summary>What to outline, in the order it was added.</summary>
    public IReadOnlyList<OutlineItem> Items => _items;

    /// <summary>How many meshes are queued.</summary>
    public int Count => _items.Count;

    /// <summary>Meshes refused since the last clear because the list was full.</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// The selected outline's colour. Linear, like a <see cref="DebugDraw"/>
    /// colour, so an outline and an overlay line given the same value match.
    /// </summary>
    public Vector3 SelectedColor { get; set; } = new(1f, 0.34f, 0.03f);

    /// <summary>The hovered outline's colour. Linear.</summary>
    public Vector3 HoveredColor { get; set; } = new(1f, 0.34f, 0.03f);

    /// <summary>How strongly the hovered outline is mixed in, 0 to 1.</summary>
    public float HoveredStrength { get; set; } = 0.55f;

    /// <summary>The outline's width in pixels.</summary>
    public float Width { get; set; } = 2.5f;

    /// <summary>Queues a mesh for this frame.</summary>
    public void Add(Mesh mesh, in Matrix4x4 world, OutlineGroup group)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (_items.Count >= MaxItems)
        {
            Dropped++;
            return;
        }

        _items.Add(new OutlineItem(mesh, world, group));
    }

    /// <summary>Empties the list.</summary>
    public void Clear()
    {
        _items.Clear();
        Dropped = 0;
    }
}
