using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using SpectraEngine.Core.Assets;

namespace SpectraEngine.Core.Bsp;

// Per-cell mesh stage of a static-world compile. The union of the per-cell
// triangles must equal the monolithic CsgWorld.BuildMesh, triangle for triangle.
//
// Reuse is decided by welded-array identity, not by CsgWorld.DirtyCells: a long
// brush re-carved at one end changes surfaces in an owner cell that may not be
// in the dirty set.
internal static class ChunkMeshBuilder
{
    // Returns the meshes in ascending cell order. Pure CPU, safe off the render thread.
    internal static IReadOnlyList<ChunkMesh> Build(
        ChunkGrid chunks,
        Polygon[][] weldedPerBrush,
        CsgMeshCache? previousCache,
        bool produceCache,
        out CsgMeshCache? nextCache,
        out CsgMeshStats stats)
    {
        var geometryCells = new List<WorldChunk>();
        foreach (WorldChunk chunk in chunks.OrderedChunks)
        {
            if (chunk.WeldedSurfaces.Count > 0)
                geometryCells.Add(chunk);
        }

        var meshes = new ChunkMesh[geometryCells.Count];
        CsgMeshCache.Entry[]? entries = produceCache ? new CsgMeshCache.Entry[geometryCells.Count] : null;

        var needsBuild = new List<int>();
        int reused = 0;
        for (int c = 0; c < geometryCells.Count; c++)
        {
            WorldChunk chunk = geometryCells[c];
            if (previousCache is not null &&
                previousCache.TryGet(chunk.Coord, out CsgMeshCache.Entry? cached) &&
                CsgMeshCache.OwnersMatch(cached, chunk, weldedPerBrush))
            {
                meshes[c] = cached.Mesh;
                if (entries is not null)
                    entries[c] = cached;
                reused++;
                continue;
            }
            needsBuild.Add(c);
        }

        // Each cell writes only its own slots. One cell is the common edit
        // case and skips Parallel.For.
        if (needsBuild.Count == 1)
        {
            BuildCell(needsBuild[0]);
        }
        else if (needsBuild.Count > 1)
        {
            Parallel.For(0, needsBuild.Count, i => BuildCell(needsBuild[i]));
        }

        stats = new CsgMeshStats(reused, needsBuild.Count);
        nextCache = entries is not null ? CsgMeshCache.FromEntries(geometryCells, entries) : null;
        return meshes;

        void BuildCell(int c)
        {
            WorldChunk chunk = geometryCells[c];
            ChunkMesh mesh = BuildArtifact(chunk);
            meshes[c] = mesh;
            if (entries is not null)
            {
                // Same order CsgMeshCache.OwnersMatch walks.
                IReadOnlyList<int> owned = chunk.OwnedBrushIndices;
                var ownedWelded = new Polygon[owned.Count][];
                for (int k = 0; k < ownedWelded.Length; k++)
                    ownedWelded[k] = weldedPerBrush[owned[k]];
                entries[c] = new CsgMeshCache.Entry(ownedWelded, mesh);
            }
        }
    }

    // Shared with the incremental compile so both produce identical arrays.
    internal static ChunkMesh BuildArtifact(WorldChunk chunk)
    {
        IReadOnlyList<Polygon> surfaces = chunk.WeldedSurfaces;
        ChunkSubmesh[] submeshes = BuildSubmeshes(surfaces);

        // Bounds cover every owned surface, including degenerate ones the
        // material split dropped.
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int s = 0; s < surfaces.Count; s++)
        {
            Aabb b = surfaces[s].Bounds;
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
        }

        return new ChunkMesh(chunk.Coord, submeshes, new Aabb(min, max));
    }

    // One submesh per material, ascending material id, emission order kept
    // inside each. A group with no triangles yields no submesh.
    internal static ChunkSubmesh[] BuildSubmeshes(IReadOnlyList<Polygon> surfaces)
    {
        if (surfaces.Count == 0)
            return [];

        MaterialRef uniform = surfaces[0].Face.Material;
        bool isUniform = true;
        for (int i = 1; i < surfaces.Count; i++)
        {
            if (surfaces[i].Face.Material != uniform)
            {
                isUniform = false;
                break;
            }
        }

        if (isUniform)
        {
            (float[] vertices, uint[] indices) = CsgWorld.BuildMeshArrays(surfaces);
            return indices.Length == 0 ? [] : [new ChunkSubmesh(uniform, vertices, indices)];
        }

        // Key is (material id, position) packed in a long, so the unstable
        // Array.Sort still keeps emission order inside a group.
        var keys = new long[surfaces.Count];
        for (int i = 0; i < surfaces.Count; i++)
            keys[i] = ((long)surfaces[i].Face.Material.Id << 32) | (uint)i;
        Array.Sort(keys);

        var submeshes = new List<ChunkSubmesh>();
        var group = new List<Polygon>();
        int groupId = (int)(keys[0] >> 32);
        for (int k = 0; k <= keys.Length; k++)
        {
            // Extra pass with a sentinel id closes the last group.
            int id = k < keys.Length ? (int)(keys[k] >> 32) : int.MinValue;
            if (id != groupId)
            {
                (float[] vertices, uint[] indices) = CsgWorld.BuildMeshArrays(group);
                if (indices.Length > 0)
                    submeshes.Add(new ChunkSubmesh(group[0].Face.Material, vertices, indices));
                if (k == keys.Length)
                    break;
                group.Clear();
                groupId = id;
            }
            group.Add(surfaces[(int)(keys[k] & 0xFFFFFFFFL)]);
        }

        return [.. submeshes];
    }
}
