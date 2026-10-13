using System;
using System.Collections.Generic;
using System.IO;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// Collects a compiled map's assets, nodes, entities, lights, scripts,
/// collision hulls, chunks, spawns and brush sources and writes them through
/// <see cref="ScmapWriter"/>.
/// </summary>
// The chunk directory is sorted at build time; nodes keep the caller's order,
// because sibling order is authored data.
public sealed class ScmapBuilder
{
    private readonly ScmapAssetTable _assets = new();
    private readonly List<ScmapNodeSource> _nodes = [];
    private readonly List<ScmapChunkSource> _chunks = [];
    private readonly HashSet<ChunkCoord> _chunkCoords = [];
    private readonly List<ScmapSpawnSource> _spawns = [];
    private readonly List<ScmapBrushSourceEntry> _brushes = [];
    private readonly List<ScmapEntitySource> _entities = [];
    private readonly List<ScmapCollisionHullSource> _hulls = [];
    private readonly List<ScmapLightSource> _lights = [];
    private readonly List<(ScmapScriptSource Script, uint AssetIndex)> _scripts = [];
    private readonly List<ScmapNodeSetsSource> _nodeSets = [];

    /// <summary>Creates a builder for a scene.</summary>
    public ScmapBuilder(string sceneName)
    {
        ArgumentNullException.ThrowIfNull(sceneName);
        SceneName = sceneName;
    }

    /// <summary>The scene's name.</summary>
    public string SceneName { get; }

    /// <summary>How many assets the map references.</summary>
    public int AssetCount => _assets.Count;

    /// <summary>How many nodes have been added.</summary>
    public int NodeCount => _nodes.Count;

    /// <summary>How many cells the directory will carry.</summary>
    public int ChunkCount => _chunks.Count;

    /// <summary>How many authored brushes <c>BRSH</c> will carry.</summary>
    public int BrushSourceCount => _brushes.Count;

    /// <summary>How many entities <c>ENTT</c> will carry.</summary>
    public int EntityCount => _entities.Count;

    /// <summary>How many hulls <c>COLL</c> will carry.</summary>
    public int CollisionHullCount => _hulls.Count;

    /// <summary>How many lights <c>LGHT</c> will carry.</summary>
    public int LightCount => _lights.Count;

    /// <summary>How many scripts <c>SCPT</c> will carry.</summary>
    public int ScriptCount => _scripts.Count;

    /// <summary>Adds a spawn point.</summary>
    public void AddSpawn(ScmapSpawnSource spawn) => _spawns.Add(spawn);

    /// <summary>
    /// Adds an asset reference, or returns the index it already has. Keyed
    /// case-insensitively on the normalised path. Only a script keeps one row
    /// for each way its path is spelled.
    /// </summary>
    /// <exception cref="ScmapAssetKindException">The path is already present under a different kind.</exception>
    public uint AddAsset(ScmapAssetSource asset) => _assets.Add(asset);

    /// <summary>
    /// Adds a material by the path it was interned from. Throws for the default
    /// material and for a reference this process never interned.
    /// </summary>
    public uint AddMaterial(MaterialRef material, ulong contentHash = 0) =>
        _assets.AddMaterial(material, contentHash);

    /// <summary>
    /// Adds a node and returns its index. Call in pre-order: the parent must
    /// already have been added.
    /// </summary>
    public int AddNode(ScmapNodeSource node)
    {
        int index = _nodes.Count;
        string name = node.Name ?? string.Empty;

        if (node.ParentIndex < -1 || node.ParentIndex >= index)
        {
            throw new InvalidOperationException(
                $"Node {index} ('{name}') names parent {node.ParentIndex}. Records are pre-order, so a parent " +
                "index is -1 or a node already added; anything else is a graph a forward-pass loader walks " +
                "forever.");
        }

        if (node.PayloadKind == ScmapPayloadKind.RetiredBrushModel)
        {
            throw new InvalidOperationException(
                $"Node {index} ('{name}') declares payload kind 3, which is retired and carries no meaning. " +
                "An entity-owned brush is a part brush wearing the entity-owned flag; the value is burned " +
                "rather than reused, because an enum value in a shipped format must never mean two things.");
        }

        if (!Enum.IsDefined(node.PayloadKind))
        {
            throw new InvalidOperationException(
                $"Node {index} ('{name}') declares payload kind {(ushort)node.PayloadKind}, which this format " +
                "has no meaning for.");
        }

        if (node.DeclaredState == ScmapNodeState.Invalid)
        {
            throw new InvalidOperationException(
                $"Node {index} ('{name}') declares state 3, which is the unused encoding of a two-bit field " +
                "rather than a fourth state. A reader tolerates it as a per-node defect; a cook is the loud " +
                "gate and refuses it.");
        }

        _nodes.Add(node with { Name = name });
        return index;
    }

    /// <summary>
    /// Adds a cell to the chunk directory, in any order. Throws on a duplicate
    /// cell or on submeshes that are unsorted or not whole triangles.
    /// </summary>
    public void AddChunk(ScmapChunkSource chunk)
    {
        // A set, not a scan of _chunks: this runs once per cell of the world.
        if (_chunkCoords.Contains(chunk.Coord))
        {
            throw new InvalidOperationException(
                $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) is already in the chunk directory. " +
                "One cell owns one entry, and a duplicate makes a binary search answer whichever it lands on.");
        }

        ScmapChunkSection.Validate(in chunk);

        // Only after every check has passed, or a rejected cell would later
        // read as a duplicate.
        _chunks.Add(chunk);
        _chunkCoords.Add(chunk.Coord);
    }

    /// <summary>
    /// Adds one authored brush's planes and faces to <c>BRSH</c>. Call in node
    /// pre-order. The node index is checked in <see cref="Write"/>.
    /// </summary>
    public void AddBrushSource(ScmapBrushSourceEntry brush)
    {
        ArgumentNullException.ThrowIfNull(brush.Planes);
        ArgumentNullException.ThrowIfNull(brush.Faces);

        if (brush.Planes.Length != brush.Faces.Length)
        {
            throw new InvalidOperationException(
                $"Brush on node {brush.NodeIndex} has {brush.Planes.Length} planes and {brush.Faces.Length} " +
                "faces. One face per plane is the invariant the whole per-face material path rests on, so a " +
                "mismatch is an indexing bug rather than a surface that renders wrongly.");
        }

        _brushes.Add(brush);
    }

    /// <summary>
    /// Adds one node's entity to <c>ENTT</c> and its wires to <c>ECON</c>. Call
    /// in node pre-order, once per node. The node index is checked in
    /// <see cref="Write"/>.
    /// </summary>
    public void AddEntity(ScmapEntitySource entity)
    {
        ArgumentNullException.ThrowIfNull(entity.ClassName);
        ArgumentNullException.ThrowIfNull(entity.Keyvalues);
        ArgumentNullException.ThrowIfNull(entity.Connections);

        if (_entities.Count > 0 && entity.NodeIndex <= _entities[^1].NodeIndex)
        {
            throw new InvalidOperationException(
                $"The entity on node {entity.NodeIndex} was added after the one on node " +
                $"{_entities[^1].NodeIndex}. Entity records are in node order, one per node, and a loader " +
                "walks them in step with the nodes.");
        }

        _entities.Add(entity);
    }

    /// <summary>
    /// Adds one baked world brush's planes to <c>COLL</c> and, when the hull
    /// names them, its face materials to <c>COLM</c>. Call in node pre-order,
    /// once per baked brush node. <see cref="Write"/> checks that every such
    /// node got one.
    /// </summary>
    public void AddCollisionHull(ScmapCollisionHullSource hull)
    {
        ArgumentNullException.ThrowIfNull(hull.Planes);

        if (hull.Planes.Length < ScmapFormat.MinimumHullPlanes)
        {
            throw new InvalidOperationException(
                $"The collision hull on node {hull.NodeIndex} has {hull.Planes.Length} planes, and fewer " +
                $"than {ScmapFormat.MinimumHullPlanes} half-spaces bound no volume.");
        }

        if (_hulls.Count > 0 && hull.NodeIndex <= _hulls[^1].NodeIndex)
        {
            throw new InvalidOperationException(
                $"The collision hull on node {hull.NodeIndex} was added after the one on node " +
                $"{_hulls[^1].NodeIndex}. Hull records are in node order, one per baked brush.");
        }

        if (hull.FaceAssets is { } faces && faces.Length != hull.Planes.Length)
        {
            throw new InvalidOperationException(
                $"The collision hull on node {hull.NodeIndex} has {hull.Planes.Length} planes and names " +
                $"materials for {faces.Length} faces. There is one face per plane.");
        }

        _hulls.Add(hull);
    }

    /// <summary>
    /// Adds one node's light to <c>LGHT</c>. Call in node pre-order, once per
    /// node. The node index is checked in <see cref="Write"/>.
    /// </summary>
    public void AddLight(ScmapLightSource light)
    {
        ArgumentNullException.ThrowIfNull(light.Light);

        if (_lights.Count > 0 && light.NodeIndex <= _lights[^1].NodeIndex)
        {
            throw new InvalidOperationException(
                $"The light on node {light.NodeIndex} was added after the one on node " +
                $"{_lights[^1].NodeIndex}. Light records are in node order, one per node, and a loader " +
                "walks them in step with the nodes.");
        }

        // A copy: Light is mutable, and the file is built later.
        _lights.Add(light with { Light = light.Light.Clone() });
    }

    /// <summary>
    /// Adds one node's script to <c>SCPT</c> and its file to the asset table.
    /// Call in node pre-order, once per node. The node index is checked in
    /// <see cref="Write"/>.
    /// </summary>
    public void AddScript(ScmapScriptSource script)
    {
        ArgumentNullException.ThrowIfNull(script.Path);

        if (_scripts.Count > 0 && script.NodeIndex <= _scripts[^1].Script.NodeIndex)
        {
            throw new InvalidOperationException(
                $"The script on node {script.NodeIndex} was added after the one on node " +
                $"{_scripts[^1].Script.NodeIndex}. Script records are in node order, one per node, and a " +
                "loader walks them in step with the nodes.");
        }

        _scripts.Add((script, AddAsset(new ScmapAssetSource(PackEntryKind.Script, script.Path))));
    }

    /// <summary>
    /// Adds the tags and attributes one node was built with, to <c>NTAG</c>
    /// and <c>NATR</c>. Call in node pre-order, once per node that has any.
    /// The node index is checked in <see cref="Write"/>.
    /// </summary>
    public void AddNodeSets(ScmapNodeSetsSource sets)
    {
        ArgumentNullException.ThrowIfNull(sets.Tags);
        ArgumentNullException.ThrowIfNull(sets.Attributes);

        if (_nodeSets.Count > 0 && sets.NodeIndex <= _nodeSets[^1].NodeIndex)
        {
            throw new InvalidOperationException(
                $"The tags and attributes of node {sets.NodeIndex} were added after those of node " +
                $"{_nodeSets[^1].NodeIndex}. Their records are in node order, and a loader walks them in " +
                "step with the nodes.");
        }

        _nodeSets.Add(sets);
    }

    /// <summary>Builds the whole file.</summary>
    /// <param name="sourceMapDigest">See <see cref="MapBundleDigest"/>.</param>
    /// <param name="mapFormatVersion">The authored map format version the bake read.</param>
    public byte[] Build(UInt128 sourceMapDigest, uint mapFormatVersion, ScmapFlags flags = ScmapFlags.None)
    {
        using var buffer = new MemoryStream();
        Write(buffer, sourceMapDigest, mapFormatVersion, flags);
        return buffer.ToArray();
    }

    /// <summary>Builds the whole file into <paramref name="stream"/>.</summary>
    public void Write(Stream stream, UInt128 sourceMapDigest, uint mapFormatVersion, ScmapFlags flags = ScmapFlags.None)
    {
        var strings = new ScmapStringTableBuilder();

        // Strings are interned here in a fixed order (scene name, asset paths,
        // node names, entity strings, tags and attributes), not as callers
        // add things, so the blob does not depend on call order.
        uint sceneNameString = strings.Intern(SceneName);

        var assetPathStrings = new uint[_assets.Count];
        for (int i = 0; i < _assets.Count; i++) assetPathStrings[i] = strings.Intern(_assets.Rows[i].ContentPath);

        var nodeNameStrings = new uint[_nodes.Count];
        for (int i = 0; i < _nodes.Count; i++) nodeNameStrings[i] = strings.Intern(_nodes[i].Name);

        // Before the string blob is built: this interns as it goes.
        byte[] entityBody = ScmapEntitySection.Build(_entities, _nodes.Count, strings, out byte[] connectionBody);
        ScmapNodeSetsSection.Build(
            _nodeSets, _nodes.Count, strings, out byte[]? tagBody, out byte[]? attributeBody);

        byte[] stringBody = strings.Build();
        byte[] assetBody = _assets.Build(assetPathStrings);
        byte[] metaBody = ScmapMetaSection.Build(sceneNameString, _spawns);
        byte[] nodeBody = ScmapNodeSection.Build(_nodes, nodeNameStrings);
        byte[] chunkBody = ScmapChunkSection.Build(_chunks, out byte[] meshBody, out byte[] bspBody);
        byte[] collisionBody = ScmapCollisionSection.Build(_nodes, _hulls);
        byte[]? hullMaterialBody = ScmapHullMaterialSection.Build(_hulls, _assets.Rows);
        byte[] lightBody = ScmapLightSection.Build(_lights, _nodes.Count);
        byte[]? brushBody = ScmapBrushSourceSection.Build(_brushes, _nodes.Count);

        // The reader cross-checks this flag against the section table, so it
        // is derived, not taken from the caller.
        ScmapFlags fileFlags = brushBody is null
            ? flags & ~ScmapFlags.HasBrushSource
            : flags | ScmapFlags.HasBrushSource;

        var writer = new ScmapWriter(fileFlags, sourceMapDigest, mapFormatVersion);

        writer.AddSection(ScmapFormat.StringSection, stringBody);
        writer.AddSection(ScmapFormat.AssetSection, assetBody);
        writer.AddSection(ScmapFormat.MetaSection, metaBody);
        writer.AddSection(ScmapFormat.NodeSection, nodeBody);
        writer.AddSection(ScmapFormat.ChunkDirectorySection, chunkBody);
        writer.AddSection(ScmapFormat.ChunkMeshSection, meshBody);
        writer.AddSection(ScmapFormat.ChunkBspSection, bspBody);

        writer.AddSection(ScmapFormat.EntitySection, entityBody);
        writer.AddSection(ScmapFormat.EntityConnectionSection, connectionBody);
        writer.AddSection(ScmapFormat.CollisionSection, collisionBody);

        // Beside the planes it describes. Left out when no hull names its
        // faces: a reader takes a missing section as "not said".
        if (hullMaterialBody is not null) writer.AddSection(ScmapFormat.HullMaterialSection, hullMaterialBody);

        writer.AddSection(ScmapFormat.LightSection, lightBody);
        writer.AddSection(ScmapFormat.ScriptSection, ScmapScriptSection.Build(_scripts, _nodes.Count));

        // Left out when no node has any: a reader takes a missing section
        // as no tag and no attribute.
        if (tagBody is not null) writer.AddSection(ScmapFormat.NodeTagSection, tagBody);
        if (attributeBody is not null) writer.AddSection(ScmapFormat.NodeAttributeSection, attributeBody);

        // Last, so what a load always reads sits at the front. Omitted when
        // there is none: an empty BRSH would still count as present.
        if (brushBody is not null) writer.AddSection(ScmapFormat.BrushSourceSection, brushBody);

        writer.Write(stream);
    }
}
