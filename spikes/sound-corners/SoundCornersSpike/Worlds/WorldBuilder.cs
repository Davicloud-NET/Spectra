using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Probes;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;

namespace SoundCornersSpike.Worlds;

// Brushes placed the way DemoPlayArea places them: size in the brush, place
// in the transform. Order matters: a cut only opens what came before it.
internal sealed class WorldBuilder
{
    private readonly List<BrushPlacement> _placements = [];

    public PartSet Parts { get; } = new();

    public int BrushCount => _placements.Count;

    // Moves everything added afterwards. The hand cases use it to keep
    // surfaces off the centres of the air cells.
    public Vector3 Offset { get; set; }

    public WorldBuilder Box(Vector3 min, Vector3 max) => Add(min, max, Quaternion.Identity, cut: false);

    public WorldBuilder Cut(Vector3 min, Vector3 max) => Add(min, max, Quaternion.Identity, cut: true);

    public WorldBuilder RotatedBox(Vector3 center, Vector3 half, Quaternion rotation, bool cut = false) =>
        Add(center - half, center + half, rotation, cut);

    public WorldBuilder Part(Vector3 min, Vector3 max, string name)
    {
        Parts.Add(PartHull.FromBox(min + Offset, max + Offset, name));
        return this;
    }

    public WorldBuilder AddPlacements(IEnumerable<BrushPlacement> placements)
    {
        _placements.AddRange(placements);
        return this;
    }

    public CsgWorld Build() => CsgWorld.Build(_placements);

    private WorldBuilder Add(Vector3 min, Vector3 max, Quaternion rotation, bool cut)
    {
        Vector3 half = (max - min) * 0.5f;
        Vector3 center = ((min + max) * 0.5f) + Offset;

        Brush brush = Brush.CreateBox(-half, half);
        if (cut) brush = brush.WithOperation(BrushOperation.Subtractive);

        Matrix4x4 world = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(center);
        _placements.Add(new BrushPlacement(brush, world));
        return this;
    }

    // The same per-cell trees a bake writes, flattened in memory. No file is
    // written and nothing is mapped.
    public static CompiledStaticWorld Bake(CsgWorld live, bool nativeMemory)
    {
        IReadOnlyList<WorldChunk> cells = live.Chunks.OrderedChunks;
        var chunks = new CompiledStaticWorldChunk[cells.Count];

        for (int i = 0; i < chunks.Length; i++)
        {
            WorldChunk cell = cells[i];
            FlatBspNode[] nodes = BspFlattener.Flatten(cell.Bsp, out int root);

            System.ReadOnlyMemory<FlatBspNode> memory = nativeMemory
                ? new NativeBspNodes(nodes).Memory
                : nodes;

            chunks[i] = new CompiledStaticWorldChunk(
                cell.Coord, cell.Coord.Bounds, new FlatBspTree(memory, root), TriangleCount: 0);
        }

        return new CompiledStaticWorld("spike", chunks, file: null);
    }
}
