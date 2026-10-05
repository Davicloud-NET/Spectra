using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Maps.Compiled;
using SpectraEngine.Core.Scene;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// Collects a compiled map's assets, nodes, entities, lights, collision hulls,
/// chunks, spawns and brush sources and writes them through
/// <see cref="ScmapWriter"/>.
/// </summary>
// The chunk directory is sorted at build time; nodes keep the caller's order,
// because sibling order is authored data.
public sealed class ScmapBuilder
{
    private readonly List<ScmapAssetSource> _assets = [];
    private readonly Dictionary<string, uint> _assetLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScmapNodeSource> _nodes = [];
    private readonly List<ScmapChunkSource> _chunks = [];
    private readonly HashSet<ChunkCoord> _chunkCoords = [];
    private readonly List<ScmapSpawnSource> _spawns = [];
    private readonly List<ScmapBrushSourceEntry> _brushes = [];
    private readonly List<ScmapEntitySource> _entities = [];
    private readonly List<ScmapCollisionHullSource> _hulls = [];
    private readonly List<ScmapLightSource> _lights = [];

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

    /// <summary>Adds a spawn point.</summary>
    public void AddSpawn(ScmapSpawnSource spawn) => _spawns.Add(spawn);

    /// <summary>
    /// Adds an asset reference, or returns the index it already has. Keyed
    /// case-insensitively on the normalised path. Throws if the path is already
    /// present under a different kind.
    /// </summary>
    public uint AddAsset(ScmapAssetSource asset)
    {
        string normalized = ContentRoot.NormalizeRelativePath(asset.ContentPath);

        if (_assetLookup.TryGetValue(normalized, out uint existing))
        {
            ScmapAssetSource had = _assets[(int)existing];
            if (had.Kind != asset.Kind)
            {
                throw new InvalidOperationException(
                    $"Asset '{normalized}' was added as {had.Kind} and again as {asset.Kind}. One path is one " +
                    "asset, so a reader would have to choose which kind it is, and choosing silently is how " +
                    "a material gets loaded as a model.");
            }

            return existing;
        }

        var index = (uint)_assets.Count;
        _assets.Add(asset with { ContentPath = normalized });
        _assetLookup[normalized] = index;
        return index;
    }

    /// <summary>
    /// Adds a material by the path it was interned from. Throws for the default
    /// material and for a reference this process never interned.
    /// </summary>
    // The only way a material gets a row. MaterialRef.Id is per-process and
    // must not be written.
    public uint AddMaterial(MaterialRef material, ulong contentHash = 0)
    {
        if (material.IsDefault)
        {
            throw new InvalidOperationException(
                "The default material names no path, so it has no asset-table row. A surface that wears it " +
                "names no asset at all, which is what the engine's fallback to the default material already " +
                "means at load.");
        }

        if (!MaterialRegistry.TryGetPath(material, out string path))
        {
            throw new InvalidOperationException(
                $"Material id {material.Id} was never interned in this process, so there is no path to write. " +
                "An id is per-process interning order and means nothing in a file.");
        }

        return AddAsset(new ScmapAssetSource(PackEntryKind.Material, path, contentHash));
    }

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

        ValidateSubmeshes(chunk);

        if (chunk.BspNodes is { Length: > 0 } &&
            (chunk.BspRootIndex < FlatBspNode.SolidLeaf || chunk.BspRootIndex >= chunk.BspNodes.Length))
        {
            throw new InvalidOperationException(
                $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) names BSP root " +
                $"{chunk.BspRootIndex} over {chunk.BspNodes.Length} nodes, which is neither an index into them " +
                "nor a leaf code. A root out of range is a query that walks off the end of the block.");
        }

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

    private static void ValidateSubmeshes(ScmapChunkSource chunk)
    {
        if (chunk.Submeshes is not { Length: > 0 }) return;

        for (int i = 0; i < chunk.Submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = chunk.Submeshes[i];
            ArgumentNullException.ThrowIfNull(submesh.Vertices);
            ArgumentNullException.ThrowIfNull(submesh.Indices);

            if (i > 0 && chunk.Submeshes[i - 1].AssetIndex >= submesh.AssetIndex)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) has submeshes out of ascending " +
                    $"asset order at {i}: asset {chunk.Submeshes[i - 1].AssetIndex} is followed by asset " +
                    $"{submesh.AssetIndex}.");
            }

            if (submesh.Vertices.Length % ScmapFormat.StandardVertexStrideFloats != 0)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) submesh {i} carries " +
                    $"{submesh.Vertices.Length} floats, which is not a whole number of " +
                    $"{ScmapFormat.StandardVertexStrideFloats}-float vertices.");
            }

            if (submesh.Indices.Length % 3 != 0)
            {
                throw new InvalidOperationException(
                    $"Cell ({chunk.Coord.X}, {chunk.Coord.Y}, {chunk.Coord.Z}) submesh {i} carries " +
                    $"{submesh.Indices.Length} indices, which is not a whole number of triangles.");
            }
        }
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
        // node names, entity strings), not as callers add things, so the blob
        // does not depend on call order.
        uint sceneNameString = strings.Intern(SceneName);

        var assetPathStrings = new uint[_assets.Count];
        for (int i = 0; i < _assets.Count; i++) assetPathStrings[i] = strings.Intern(_assets[i].ContentPath);

        var nodeNameStrings = new uint[_nodes.Count];
        for (int i = 0; i < _nodes.Count; i++) nodeNameStrings[i] = strings.Intern(_nodes[i].Name);

        // Before the string blob is built: this interns as it goes.
        byte[] entityBody = BuildEntities(strings, out byte[] connectionBody);

        byte[] stringBody = strings.Build();
        byte[] assetBody = BuildAssets(assetPathStrings);
        byte[] metaBody = BuildMeta(sceneNameString);
        byte[] nodeBody = BuildNodes(nodeNameStrings);
        byte[] chunkBody = BuildChunks(out byte[] meshBody, out byte[] bspBody);
        byte[] collisionBody = BuildCollision();
        byte[]? hullMaterialBody = ScmapHullMaterialSection.Build(_hulls, _assets);
        byte[] lightBody = BuildLights();
        byte[]? brushBody = BuildBrushSource();

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

        // Empty for now. Written so nothing else takes these codes.
        writer.AddSection(ScmapFormat.ScriptSection, ReadOnlySpan<byte>.Empty);
        writer.AddSection(ScmapFormat.ScriptBytecodeSection, ReadOnlySpan<byte>.Empty);
        writer.AddSection(ScmapFormat.ScriptSourceSection, ReadOnlySpan<byte>.Empty);

        // Last, so what a load always reads sits at the front. Omitted when
        // there is none: an empty BRSH would still count as present.
        if (brushBody is not null) writer.AddSection(ScmapFormat.BrushSourceSection, brushBody);

        writer.Write(stream);
    }

    private byte[] BuildAssets(uint[] pathStrings)
    {
        var body = new byte[ScmapFormat.AssetCountSize + (_assets.Count * ScmapFormat.AssetEntrySize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_assets.Count);

        for (int i = 0; i < _assets.Count; i++)
        {
            var entry = new ScmapAssetEntry(_assets[i].Kind, pathStrings[i], _assets[i].ContentHash);
            MemoryMarshal.Write(span[(ScmapFormat.AssetCountSize + (i * ScmapFormat.AssetEntrySize))..], in entry);
        }

        return body;
    }

    private byte[] BuildMeta(uint sceneNameString)
    {
        var body = new byte[ScmapFormat.MetaPreambleSize + (_spawns.Count * ScmapFormat.SpawnRecordSize)];
        Span<byte> span = body;

        var meta = new ScmapMeta(sceneNameString, (uint)_spawns.Count);
        MemoryMarshal.Write(span, in meta);

        for (int i = 0; i < _spawns.Count; i++)
        {
            var record = new ScmapSpawn(_spawns[i].Position, _spawns[i].Rotation);
            MemoryMarshal.Write(span[(ScmapFormat.MetaPreambleSize + (i * ScmapFormat.SpawnRecordSize))..], in record);
        }

        return body;
    }

    private byte[] BuildNodes(uint[] nameStrings)
    {
        var body = new byte[ScmapFormat.NodePreambleSize + (_nodes.Count * ScmapFormat.NodeRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_nodes.Count);

        for (int i = 0; i < _nodes.Count; i++)
        {
            ScmapNodeSource source = _nodes[i];
            var record = new ScmapNodeRecord(
                source.Id,
                nameStrings[i],
                source.ParentIndex,
                source.LocalTransform.Position,
                source.LocalTransform.Rotation,
                source.LocalTransform.Scale,
                source.PayloadKind,
                source.PayloadFlags,
                source.DeclaredRealm,
                source.DeclaredState,
                source.PayloadIndex);

            MemoryMarshal.Write(span[(ScmapFormat.NodePreambleSize + (i * ScmapFormat.NodeRecordSize))..], in record);
        }

        return body;
    }

    // ENTT, and ECON beside it. Interns per entity: its class, its keys and
    // values, then each wire's output, target, input and parameter. That order
    // is part of the file's bytes.
    private byte[] BuildEntities(ScmapStringTableBuilder strings, out byte[] connectionBody)
    {
        CountEntityRecords(out int keyvalueCount, out int connectionCount);

        long keyvalueStart = ScmapLayout.PaddedSectionSize(
            ScmapFormat.EntityPreambleSize + ((long)_entities.Count * ScmapFormat.EntityRecordSize));

        var body = new byte[keyvalueStart + ((long)keyvalueCount * ScmapFormat.KeyvalueRecordSize)];
        connectionBody = new byte[
            ScmapFormat.ConnectionPreambleSize + ((long)connectionCount * ScmapFormat.ConnectionRecordSize)];

        BinaryPrimitives.WriteUInt32LittleEndian(body, (uint)_entities.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), (uint)keyvalueCount);
        BinaryPrimitives.WriteUInt32LittleEndian(connectionBody, (uint)connectionCount);

        int keyvalue = 0;
        int connection = 0;
        for (int i = 0; i < _entities.Count; i++)
        {
            ScmapEntitySource entity = _entities[i];

            var record = new ScmapEntityRecord(
                (uint)entity.NodeIndex,
                strings.Intern(entity.ClassName),
                (uint)keyvalue,
                (uint)entity.Keyvalues.Length,
                (uint)connection,
                (uint)entity.Connections.Length);

            MemoryMarshal.Write(
                body.AsSpan(ScmapFormat.EntityPreambleSize + (i * ScmapFormat.EntityRecordSize)), in record);

            foreach (KeyValuePair<string, string> pair in entity.Keyvalues)
            {
                var pairRecord = new ScmapKeyvalueRecord(strings.Intern(pair.Key), strings.Intern(pair.Value));
                MemoryMarshal.Write(
                    body.AsSpan((int)keyvalueStart + (keyvalue++ * ScmapFormat.KeyvalueRecordSize)), in pairRecord);
            }

            foreach (EntityConnection wire in entity.Connections)
            {
                var wireRecord = new ScmapConnectionRecord(
                    strings.Intern(wire.Output),
                    strings.Intern(wire.TargetName),
                    strings.Intern(wire.Input),
                    strings.Intern(wire.Parameter),
                    wire.Delay,
                    wire.TimesToFire);

                MemoryMarshal.Write(
                    connectionBody.AsSpan(
                        ScmapFormat.ConnectionPreambleSize + (connection++ * ScmapFormat.ConnectionRecordSize)),
                    in wireRecord);
            }
        }

        return body;
    }

    private void CountEntityRecords(out int keyvalues, out int connections)
    {
        keyvalues = 0;
        connections = 0;

        foreach (ScmapEntitySource entity in _entities)
        {
            if (entity.NodeIndex < 0 || entity.NodeIndex >= _nodes.Count)
            {
                throw new InvalidOperationException(
                    $"An entity names node {entity.NodeIndex} of a {_nodes.Count}-node map. An entity that " +
                    "cannot name its node has no target name and no place in the level.");
            }

            keyvalues += entity.Keyvalues.Length;
            connections += entity.Connections.Length;
        }
    }

    // COLL. A baked brush with no hull is refused here: the reader would
    // refuse the file.
    private byte[] BuildCollision()
    {
        int planes = 0;
        int next = 0;

        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].PayloadKind != ScmapPayloadKind.StaticWorldBrush) continue;

            if (next >= _hulls.Count || _hulls[next].NodeIndex != i)
            {
                throw new InvalidOperationException(
                    $"Node {i} ('{_nodes[i].Name}') is a baked world brush with no collision hull. Every " +
                    "baked brush needs one, or a character walks through a wall that is drawn.");
            }

            planes += _hulls[next++].Planes.Length;
        }

        if (next < _hulls.Count)
        {
            throw new InvalidOperationException(
                $"A collision hull names node {_hulls[next].NodeIndex}, which is not a baked world brush of " +
                $"this {_nodes.Count}-node map. Only a brush baked into the chunks carries a hull.");
        }

        long planeStart = ScmapFormat.CollisionPreambleSize + ((long)_hulls.Count * ScmapFormat.HullRecordSize);
        var body = new byte[planeStart + ((long)planes * ScmapFormat.PlaneSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_hulls.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)planes);

        int plane = 0;
        for (int i = 0; i < _hulls.Count; i++)
        {
            ScmapCollisionHullSource hull = _hulls[i];
            var record = new ScmapHullRecord((uint)hull.NodeIndex, (uint)hull.Planes.Length, (uint)plane);
            MemoryMarshal.Write(
                span[(ScmapFormat.CollisionPreambleSize + (i * ScmapFormat.HullRecordSize))..], in record);

            for (int p = 0; p < hull.Planes.Length; p++, plane++)
            {
                MemoryMarshal.Write(
                    span[(int)(planeStart + ((long)plane * ScmapFormat.PlaneSize))..], in hull.Planes[p]);
            }
        }

        return body;
    }

    private byte[] BuildLights()
    {
        var body = new byte[ScmapFormat.LightPreambleSize + (_lights.Count * ScmapFormat.LightRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_lights.Count);

        for (int i = 0; i < _lights.Count; i++)
        {
            ScmapLightSource light = _lights[i];

            if (light.NodeIndex < 0 || light.NodeIndex >= _nodes.Count)
            {
                throw new InvalidOperationException(
                    $"A light names node {light.NodeIndex} of a {_nodes.Count}-node map. A light that cannot " +
                    "name its node has no position and no direction.");
            }

            var record = new ScmapLightRecord((uint)light.NodeIndex, light.Light);
            MemoryMarshal.Write(
                span[(ScmapFormat.LightPreambleSize + (i * ScmapFormat.LightRecordSize))..], in record);
        }

        return body;
    }

    // Sorts the directory and writes both blob sections in the same pass, so
    // offsets and blobs share one order. Every blob is padded to the payload
    // alignment through ScmapLayout.PaddedSectionSize.
    private byte[] BuildChunks(out byte[] meshBody, out byte[] bspBody)
    {
        ScmapChunkSource[] sorted = [.. _chunks];
        Array.Sort(sorted, static (a, b) => a.Coord.CompareTo(b.Coord));

        using var meshes = new MemoryStream();
        using var bsps = new MemoryStream();

        var body = new byte[ScmapFormat.ChunkPreambleSize + (sorted.Length * ScmapFormat.ChunkRecordSize)];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)sorted.Length);

        for (int i = 0; i < sorted.Length; i++)
        {
            ScmapChunkSource source = sorted[i];

            var meshOffset = (uint)meshes.Length;
            uint meshSize = WriteChunkMesh(meshes, source);

            var bspOffset = (uint)bsps.Length;
            uint bspSize = WriteChunkBsp(bsps, source);

            var record = new ScmapChunkRecord(
                source.Coord.X,
                source.Coord.Y,
                source.Coord.Z,
                source.RenderBounds.Min,
                source.RenderBounds.Max,
                meshSize == 0 ? 0 : meshOffset,
                meshSize,
                bspSize == 0 ? 0 : bspOffset,
                bspSize);

            MemoryMarshal.Write(span[(ScmapFormat.ChunkPreambleSize + (i * ScmapFormat.ChunkRecordSize))..], in record);
        }

        meshBody = meshes.ToArray();
        bspBody = bsps.ToArray();
        return body;
    }

    // One cell's CMSH blob. Writes nothing for a cell with no render geometry.
    private static uint WriteChunkMesh(MemoryStream blobs, in ScmapChunkSource cell)
    {
        if (cell.Submeshes is not { Length: > 0 }) return 0;

        ScmapSubmeshSource[] submeshes = cell.Submeshes;
        long start = blobs.Length;

        long directory = ScmapLayout.PaddedSectionSize(
            ScmapFormat.ChunkMeshHeaderSize + ((long)submeshes.Length * ScmapFormat.ChunkSubmeshEntrySize));

        // Place the arrays first: the directory needs their offsets before they are written.
        var entries = new ScmapSubmeshEntry[submeshes.Length];
        long cursor = directory;
        for (int i = 0; i < submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = submeshes[i];

            long vertexOffset = cursor;
            cursor += ScmapLayout.PaddedSectionSize((long)submesh.Vertices.Length * sizeof(float));

            long indexOffset = cursor;
            cursor += ScmapLayout.PaddedSectionSize((long)submesh.Indices.Length * sizeof(uint));

            entries[i] = new ScmapSubmeshEntry(
                submesh.AssetIndex,
                (uint)(submesh.Vertices.Length / ScmapFormat.StandardVertexStrideFloats),
                (uint)submesh.Indices.Length,
                (uint)vertexOffset,
                (uint)indexOffset);
        }

        Span<byte> header = stackalloc byte[ScmapFormat.ChunkMeshHeaderSize];
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)submeshes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], ScmapFormat.StandardVertexStrideFloats);
        blobs.Write(header);

        Span<byte> entry = stackalloc byte[ScmapFormat.ChunkSubmeshEntrySize];
        for (int i = 0; i < entries.Length; i++)
        {
            MemoryMarshal.Write(entry, in entries[i]);
            blobs.Write(entry);
        }

        WriteZeros(blobs, start + directory - blobs.Length);

        for (int i = 0; i < submeshes.Length; i++)
        {
            ScmapSubmeshSource submesh = submeshes[i];

            blobs.Write(MemoryMarshal.AsBytes<float>(submesh.Vertices));
            WriteZeros(blobs, start + entries[i].IndexOffset - blobs.Length);

            blobs.Write(MemoryMarshal.AsBytes<uint>(submesh.Indices));
            WriteZeros(blobs, ScmapLayout.PaddedSectionSize(blobs.Length - start) - (blobs.Length - start));
        }

        return (uint)(blobs.Length - start);
    }

    // One cell's CBSP blob. Null nodes means no tree. An empty array is a
    // single leaf and still gets a blob, so the root's solid/empty code survives.
    private static uint WriteChunkBsp(MemoryStream blobs, in ScmapChunkSource cell)
    {
        if (cell.BspNodes is null) return 0;

        long start = blobs.Length;

        Span<byte> header = stackalloc byte[ScmapFormat.ChunkBspHeaderSize];
        header.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)cell.BspNodes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], cell.BspRootIndex);
        blobs.Write(header);

        blobs.Write(MemoryMarshal.AsBytes<FlatBspNode>(cell.BspNodes));
        WriteZeros(blobs, ScmapLayout.PaddedSectionSize(blobs.Length - start) - (blobs.Length - start));

        return (uint)(blobs.Length - start);
    }

    // The BRSH section, or null when no brush source was kept.
    private byte[]? BuildBrushSource()
    {
        if (_brushes.Count == 0) return null;

        int planes = 0;
        foreach (ScmapBrushSourceEntry brush in _brushes)
        {
            if (brush.NodeIndex < 0 || brush.NodeIndex >= _nodes.Count)
            {
                throw new InvalidOperationException(
                    $"A kept brush names node {brush.NodeIndex} of a {_nodes.Count}-node map. A brush that " +
                    "cannot name its node has no transform, so a load would carve it at the origin.");
            }

            planes += brush.Planes.Length;
        }

        long records = (long)_brushes.Count * ScmapFormat.BrushSourceRecordSize;
        long planeStart = ScmapLayout.PaddedSectionSize(ScmapFormat.BrushSourceHeaderSize + records);
        long faceStart = ScmapLayout.PaddedSectionSize(planeStart + ((long)planes * ScmapFormat.PlaneSize));
        long total = faceStart + ((long)planes * ScmapFormat.BrushFaceRecordSize);

        var body = new byte[total];
        Span<byte> span = body;

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)_brushes.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)planes);

        int plane = 0;
        for (int i = 0; i < _brushes.Count; i++)
        {
            ScmapBrushSourceEntry brush = _brushes[i];
            var record = new ScmapBrushRecord((uint)brush.NodeIndex, (uint)brush.Planes.Length, (uint)plane);
            MemoryMarshal.Write(
                span[(ScmapFormat.BrushSourceHeaderSize + (i * ScmapFormat.BrushSourceRecordSize))..], in record);

            for (int f = 0; f < brush.Planes.Length; f++, plane++)
            {
                MemoryMarshal.Write(
                    span[(int)(planeStart + ((long)plane * ScmapFormat.PlaneSize))..], in brush.Planes[f]);

                ScmapFaceSource face = brush.Faces[f];
                var faceRecord = new ScmapFaceRecord(
                    face.AssetIndex,
                    face.UAxis,
                    face.VAxis,
                    face.UOffset,
                    face.VOffset,
                    face.UScale,
                    face.VScale);

                MemoryMarshal.Write(
                    span[(int)(faceStart + ((long)plane * ScmapFormat.BrushFaceRecordSize))..], in faceRecord);
            }
        }

        return body;
    }

    // Padding is written, not seeked over: a gap left by a seek is not guaranteed zero.
    private static void WriteZeros(MemoryStream blobs, long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        Span<byte> zeros = stackalloc byte[ScmapFormat.PayloadAlignment];
        zeros.Clear();

        while (count > 0)
        {
            int chunk = (int)Math.Min(count, zeros.Length);
            blobs.Write(zeros[..chunk]);
            count -= chunk;
        }
    }
}
