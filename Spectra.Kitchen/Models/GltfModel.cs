using System.Collections.Generic;
using System.Numerics;

namespace Spectra.Kitchen.Models;

/// <summary>
/// One drawable piece of a read glTF: one per (node, mesh primitive) pair, with
/// the node's world transform already applied to positions and normals.
/// </summary>
public sealed class GltfSubmesh
{
    internal GltfSubmesh(
        string name,
        int materialIndex,
        float[] vertices,
        uint[] indices,
        Vector3 boundsMin,
        Vector3 boundsMax)
    {
        Name = name;
        MaterialIndex = materialIndex;
        Vertices = vertices;
        Indices = indices;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
    }

    /// <summary>The node and primitive this came from, for a diagnostic.</summary>
    public string Name { get; }

    /// <summary>Index into <see cref="GltfModel.Materials"/>, or -1 for none.</summary>
    public int MaterialIndex { get; }

    /// <summary>Interleaved position, normal and UV0: eight floats per vertex.</summary>
    public float[] Vertices { get; }

    /// <summary>Three indices per triangle, zero-based within this submesh.</summary>
    public uint[] Indices { get; }

    /// <summary>Minimum corner of the transformed positions.</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Maximum corner of the transformed positions.</summary>
    public Vector3 BoundsMax { get; }

    /// <summary>Number of vertices.</summary>
    public int VertexCount => Vertices.Length / 8;
}

/// <summary>A glTF material, reduced to what a cook can act on.</summary>
/// <param name="Name">
/// The name an authored <c>.spectramat</c> is matched by. Empty when the file
/// named none.
/// </param>
/// <param name="BaseColorImageUri">
/// The base colour image URI as the file wrote it, or null when there is none or
/// it is embedded. Used in diagnostics only; not resolved to a content path.
/// </param>
public readonly record struct GltfMaterial(string Name, string? BaseColorImageUri);

/// <summary>What <see cref="GltfReader"/> made of one file.</summary>
public sealed class GltfModel
{
    internal GltfModel(
        IReadOnlyList<GltfSubmesh> submeshes,
        IReadOnlyList<GltfMaterial> materials,
        Vector3 boundsMin,
        Vector3 boundsMax,
        IReadOnlyList<string> dropped)
    {
        Submeshes = submeshes;
        Materials = materials;
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;
        Dropped = dropped;
    }

    /// <summary>Every drawable piece, in scene walk order.</summary>
    public IReadOnlyList<GltfSubmesh> Submeshes { get; }

    /// <summary>The file's material table, index-aligned with what it declared.</summary>
    public IReadOnlyList<GltfMaterial> Materials { get; }

    /// <summary>Minimum corner over every submesh.</summary>
    public Vector3 BoundsMin { get; }

    /// <summary>Maximum corner over every submesh.</summary>
    public Vector3 BoundsMax { get; }

    /// <summary>
    /// What the file carried that a <c>.smodel</c> cannot (vertex colours,
    /// tangents, a second UV, skins), each named once.
    /// </summary>
    public IReadOnlyList<string> Dropped { get; }
}
