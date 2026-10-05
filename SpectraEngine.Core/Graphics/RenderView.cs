using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Graphics;

/// <summary>One draw in a <see cref="RenderView"/>: a mesh, its material and its world matrix.</summary>
/// <param name="Material">Items without one are skipped.</param>
public readonly record struct RenderItem(Mesh Mesh, Material? Material, Matrix4x4 World);

/// <summary>
/// The frustum-culled draw list for one camera, built once per frame by
/// <c>Scene.BuildRenderView</c> and reused. Render thread only.
/// </summary>
public sealed class RenderView
{
    private readonly List<RenderItem> _items = [];
    private readonly List<RenderItem> _worldItems = [];

    /// <summary>
    /// The culled draw list. Order is stable across builds of an unchanged scene.
    /// </summary>
    public IReadOnlyList<RenderItem> Items => _items;

    /// <summary>
    /// The culled static-world draws: one item per visible chunk and material,
    /// with an identity world matrix. Empty until the first compile lands.
    /// </summary>
    public IReadOnlyList<RenderItem> WorldItems => _worldItems;

    /// <summary>
    /// Mesh-renderer items that survived culling. Not the length of
    /// <see cref="Items"/>, which also carries part-brush draws.
    /// </summary>
    public int VisibleCount { get; internal set; }

    /// <summary>Total mesh nodes in the scene that can draw, culled or not.</summary>
    public int TotalCount { get; internal set; }

    /// <summary>
    /// World chunks that survived culling. Not the length of
    /// <see cref="WorldItems"/>: a chunk emits one item per material.
    /// </summary>
    public int WorldChunksVisible { get; internal set; }

    /// <summary>Total chunks of the compiled static world with render geometry, culled or not.</summary>
    public int WorldChunksTotal { get; internal set; }

    /// <summary>Per-material world draws emitted this build: the length of <see cref="WorldItems"/>.</summary>
    public int WorldMaterialBatchesVisible { get; internal set; }

    /// <summary>Per-material world draws across every uploaded chunk, culled or not.</summary>
    public int WorldMaterialBatchesTotal { get; internal set; }

    /// <summary>
    /// Part brushes that survived culling. Their draws are in <see cref="Items"/>.
    /// </summary>
    public int PartBrushesVisible { get; internal set; }

    /// <summary>Total part brushes in the scene that can draw, culled or not.</summary>
    public int PartBrushesTotal { get; internal set; }

    /// <summary>
    /// How many lights a single pass can carry. Lights past it are dropped;
    /// see <see cref="LightsDropped"/>.
    /// </summary>
    public const int MaxLights = 8;

    private readonly RenderLight[] _lights = new RenderLight[MaxLights];
    private readonly float[] _lightKeys = new float[MaxLights];

    /// <summary>Lights for this pass, nearest first.</summary>
    public ReadOnlySpan<RenderLight> Lights => _lights.AsSpan(0, LightCount);

    /// <summary>How many of <see cref="Lights"/> are filled.</summary>
    public int LightCount { get; private set; }

    /// <summary>Lights the scene had that did not fit.</summary>
    public int LightsDropped { get; private set; }

    // Keeps the nearest MaxLights. A negative sortKey means always keep
    // (directional lights). Ties keep the earlier offer, so the result
    // follows scene order. Fixed buffer: the view build must not allocate.
    internal void OfferLight(in RenderLight light, float sortKey)
    {
        if (LightCount == MaxLights && sortKey >= _lightKeys[MaxLights - 1])
        {
            LightsDropped++;
            return;
        }

        int insertAt = LightCount;
        while (insertAt > 0 && _lightKeys[insertAt - 1] > sortKey)
            insertAt--;

        int last = LightCount == MaxLights ? MaxLights - 1 : LightCount;
        if (LightCount == MaxLights)
            LightsDropped++;
        else
            LightCount++;

        for (int i = last; i > insertAt; i--)
        {
            _lights[i] = _lights[i - 1];
            _lightKeys[i] = _lightKeys[i - 1];
        }

        _lights[insertAt] = light;
        _lightKeys[insertAt] = sortKey;
    }

    /// <summary>
    /// Fewest instances worth collapsing. Smaller groups cost more to set up
    /// as an instanced draw than they save.
    /// </summary>
    public const int MinimumBatchSize = 4;

    private readonly Dictionary<RenderBatchKey, int> _batchSlots = [];
    private readonly List<int> _batchCounts = [];
    private readonly List<int> _batchWritten = [];
    // Index into _items of the first item in each slot.
    private readonly List<int> _batchFirstItem = [];
    private readonly List<RenderBatch> _batches = [];
    private readonly List<RenderItem> _singleItems = [];
    private readonly List<Matrix4x4> _instanceTransforms = [];

    /// <summary>
    /// Groups of <see cref="Items"/> to draw as one instanced call each. A
    /// pipeline that draws these and <see cref="SingleItems"/> must not also
    /// draw <see cref="Items"/>.
    /// </summary>
    public IReadOnlyList<RenderBatch> Batches => _batches;

    /// <summary>The draws no batch claimed, in their original order.</summary>
    public IReadOnlyList<RenderItem> SingleItems => _singleItems;

    /// <summary>
    /// Every batched instance's world matrix, batch by batch. Each
    /// <see cref="RenderBatch"/> names its range.
    /// </summary>
    public ReadOnlySpan<Matrix4x4> InstanceTransforms =>
        CollectionsMarshal.AsSpan(_instanceTransforms);

    /// <summary>Draws saved by batching this build: batched instances minus batches.</summary>
    public int DrawsSaved { get; private set; }

    // Groups across the whole list, not just adjacent items: copies of one prop
    // arrive interleaved in spatial-index order. That reorders draws, which is
    // only safe while every item is opaque. Blended geometry needs its own
    // sorted pass.
    internal void BuildBatches(int minimumBatchSize = MinimumBatchSize)
    {
        _batchSlots.Clear();
        _batchCounts.Clear();
        _batchWritten.Clear();
        _batchFirstItem.Clear();
        _batches.Clear();
        _singleItems.Clear();
        _instanceTransforms.Clear();
        DrawsSaved = 0;

        if (_items.Count == 0)
            return;

        // Pass 1: count each (mesh, material), in first-seen order.
        for (int i = 0; i < _items.Count; i++)
        {
            var key = new RenderBatchKey(_items[i].Mesh, _items[i].Material);
            if (_batchSlots.TryGetValue(key, out int slot))
            {
                _batchCounts[slot]++;
            }
            else
            {
                _batchSlots[key] = _batchCounts.Count;
                _batchCounts.Add(1);
                _batchFirstItem.Add(i);
            }
        }

        // Pass 2: give each qualifying key a contiguous range. Walks the slot
        // list, never the dictionary, so batch order is deterministic.
        int offset = 0;
        for (int slot = 0; slot < _batchCounts.Count; slot++)
        {
            int count = _batchCounts[slot];
            if (count < minimumBatchSize)
            {
                // -1: not a batch. Slot numbers must stay aligned.
                _batchWritten.Add(-1);
                continue;
            }

            _batchWritten.Add(offset);
            offset += count;
        }

        if (offset == 0)
        {
            _singleItems.AddRange(_items);
            return;
        }

        EnsureInstanceCapacity(offset);

        // Pass 3: place each item into its batch's range or the singles list.
        for (int i = 0; i < _items.Count; i++)
        {
            RenderItem item = _items[i];
            int slot = _batchSlots[new RenderBatchKey(item.Mesh, item.Material)];
            int at = _batchWritten[slot];
            if (at < 0)
            {
                _singleItems.Add(item);
                continue;
            }

            _instanceTransforms[at] = item.World;
            _batchWritten[slot] = at + 1;
        }

        // Pass 4: emit the batches. Offsets come from the counts; pass 3 left
        // the write cursors at each range's end.
        offset = 0;
        for (int slot = 0; slot < _batchCounts.Count; slot++)
        {
            int count = _batchCounts[slot];
            if (count < minimumBatchSize)
                continue;

            RenderItem first = _items[_batchFirstItem[slot]];
            _batches.Add(new RenderBatch(first.Mesh, first.Material, offset, count));
            offset += count;
            DrawsSaved += count - 1;
        }
    }

    private void EnsureInstanceCapacity(int count)
    {
        if (_instanceTransforms.Capacity < count)
            _instanceTransforms.Capacity = count;

        CollectionsMarshal.SetCount(_instanceTransforms, count);
    }

    internal void Add(in RenderItem item) => _items.Add(item);

    internal void AddWorldChunk(in RenderItem item) => _worldItems.Add(item);

    internal void Clear()
    {
        _items.Clear();
        _worldItems.Clear();
        VisibleCount = 0;
        TotalCount = 0;
        WorldChunksVisible = 0;
        WorldChunksTotal = 0;
        WorldMaterialBatchesVisible = 0;
        WorldMaterialBatchesTotal = 0;
        PartBrushesVisible = 0;
        PartBrushesTotal = 0;
        LightCount = 0;
        LightsDropped = 0;

        // Stale batches could name meshes that are already destroyed.
        _batchSlots.Clear();
        _batchCounts.Clear();
        _batchWritten.Clear();
        _batchFirstItem.Clear();
        _batches.Clear();
        _singleItems.Clear();
        _instanceTransforms.Clear();
        DrawsSaved = 0;
    }
}
