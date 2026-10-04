using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// The CPU result of importing a model file: single-material submeshes, the
/// materials they reference and the node hierarchy that places them. Holds no
/// GPU objects, so it can be built off the render thread. Never mutate its arrays.
/// </summary>
public sealed class ModelData
{
    internal ModelData(
        string sourcePath,
        ModelMesh[] meshes,
        ModelMaterial[] materials,
        ModelNode root,
        Aabb localBounds,
        string[] warnings)
    {
        SourcePath = sourcePath;
        Meshes = meshes;
        Materials = materials;
        Root = root;
        LocalBounds = localBounds;
        Warnings = warnings;

        int vertices = 0;
        int indices = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            vertices += meshes[i].VertexCount;
            indices += meshes[i].IndexCount;
        }
        VertexCount = vertices;
        IndexCount = indices;
    }

    /// <summary>Absolute path of the file this was imported from.</summary>
    public string SourcePath { get; }

    /// <summary>Every drawable piece of the model, one per (source mesh, material) pair.</summary>
    public IReadOnlyList<ModelMesh> Meshes { get; }

    /// <summary>
    /// The materials <see cref="ModelMesh.MaterialIndex"/> indexes into,
    /// index-aligned with the source file's material table, unused slots included.
    /// </summary>
    public IReadOnlyList<ModelMaterial> Materials { get; }

    /// <summary>Root of the imported hierarchy. A flat file gets one root holding every mesh.</summary>
    public ModelNode Root { get; }

    /// <summary>
    /// AABB of the whole model in its own space, with node transforms applied.
    /// </summary>
    public Aabb LocalBounds { get; }

    /// <summary>Total vertices across every submesh.</summary>
    public int VertexCount { get; }

    /// <summary>Total indices across every submesh.</summary>
    public int IndexCount { get; }

    /// <summary>
    /// Content problems the importer degraded instead of throwing on, e.g. a
    /// dropped non-triangle mesh or a node transform that would not decompose.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// One single-material piece of a model, in the standard 8-float vertex layout
/// (<see cref="Graphics.VertexAttribute.StandardLayout"/>). One GPU mesh, one
/// draw call. When the source had no UVs, <see cref="HadTextureCoordinates"/>
/// is false and every uv is zero.
/// </summary>
public readonly record struct ModelMesh
{
    private readonly LegacySlice? _legacy;
    public string Name { get; init; }
    public int MaterialIndex { get; init; }
    public Aabb LocalBounds { get; init; }
    public bool HadNormals { get; init; }
    public bool HadTextureCoordinates { get; init; }
    public ModelGeometry Geometry { get; }
    public MeshDrawRange DrawRange { get; }
    public int VertexCount { get; }
    public int IndexCount => checked((int)DrawRange.IndexCount);

    // Copied lazily on first access. Upload and picking use Geometry/DrawRange.
    public float[] Vertices => _legacy?.Vertices ?? [];
    public uint[] Indices => _legacy?.Indices ?? [];

    public ModelMesh(string Name, int MaterialIndex, float[] Vertices, uint[] Indices,
        Aabb LocalBounds, bool HadNormals, bool HadTextureCoordinates)
        : this(Name, MaterialIndex, new ModelGeometry(Vertices, Indices),
            new(0, (uint)Indices.Length), 0, Vertices.Length / ModelVertexLayout.FloatsPerVertex,
            LocalBounds, HadNormals, HadTextureCoordinates) { }

    internal ModelMesh(string name, int materialIndex, ModelGeometry geometry, MeshDrawRange range,
        int firstVertex, int vertexCount, Aabb bounds, bool hadNormals, bool hadUvs)
    {
        Name = name; MaterialIndex = materialIndex; Geometry = geometry; DrawRange = range;
        LocalBounds = bounds; HadNormals = hadNormals; HadTextureCoordinates = hadUvs;
        VertexCount = vertexCount;
        _legacy = new(geometry, range, firstVertex, vertexCount);
    }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => IndexCount / 3;

    private sealed class LegacySlice(ModelGeometry geometry, MeshDrawRange range, int firstVertex, int vertexCount)
    {
        private float[]? _vertices;
        private uint[]? _indices;
        public float[] Vertices
        {
            get
            {
                lock (this) return _vertices ??= firstVertex == 0 && vertexCount == geometry.VertexCount
                    ? geometry.Vertices
                    : geometry.Vertices.AsSpan(firstVertex * ModelVertexLayout.FloatsPerVertex,
                        vertexCount * ModelVertexLayout.FloatsPerVertex).ToArray();
            }
        }
        public uint[] Indices
        {
            get
            {
                lock (this)
                {
                    if (_indices is not null) return _indices;
                    if (range.FirstIndex == 0 && range.IndexCount == geometry.Indices.Length && firstVertex == 0)
                        return _indices = geometry.Indices;
                    var indices = geometry.Indices.AsSpan((int)range.FirstIndex, (int)range.IndexCount).ToArray();
                    for (int i = 0; i < indices.Length; i++) indices[i] = checked(indices[i] - (uint)firstVertex);
                    return _indices = indices;
                }
            }
        }
    }
}

/// <summary>
/// A material as the model file described it. <see cref="AssetManager"/>
/// resolves it into a real <see cref="Graphics.Material"/> at load time.
/// </summary>
/// <param name="Name">
/// The material's name in the source file. Also the lookup key for an override:
/// <c>Materials/&lt;name&gt;.spectramat</c> wins over what the file said.
/// </param>
/// <param name="DiffuseTexturePath">
/// Content-root-relative path of the diffuse texture. Null when the material
/// named none, one outside the content root, or an embedded texture.
/// </param>
/// <param name="BaseColor">Diffuse colour, white when the file carried none.</param>
/// <param name="AssetPath">
/// The <c>.spectramat</c> path a cooked model recorded for this material. Null
/// for an imported material. Use it as given; do not rebuild it from the name.
/// </param>
public readonly record struct ModelMaterial(
    string Name,
    string? DiffuseTexturePath,
    Vector3 BaseColor,
    string? AssetPath = null);

/// <summary>
/// A node in the imported hierarchy: a name, a local transform, its submeshes
/// and its children.
/// </summary>
public sealed class ModelNode
{
    internal ModelNode(
        string name,
        Matrix4x4 localMatrix,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        bool transformIsExact,
        int[] meshIndices,
        ModelNode[] children)
    {
        Name = name;
        LocalMatrix = localMatrix;
        Position = position;
        Rotation = rotation;
        Scale = scale;
        TransformIsExact = transformIsExact;
        MeshIndices = meshIndices;
        Children = children;
    }

    /// <summary>The node's name in the source file.</summary>
    public string Name { get; }

    /// <summary>
    /// The node's transform relative to its parent, in the engine's row-vector
    /// convention.
    /// </summary>
    public Matrix4x4 LocalMatrix { get; }

    /// <summary>Translation component of <see cref="LocalMatrix"/>.</summary>
    public Vector3 Position { get; }

    /// <summary>Rotation component of <see cref="LocalMatrix"/>.</summary>
    public Quaternion Rotation { get; }

    /// <summary>Scale component of <see cref="LocalMatrix"/>.</summary>
    public Vector3 Scale { get; }

    /// <summary>
    /// False when <see cref="LocalMatrix"/> could not be decomposed (a sheared
    /// or mirrored node). Position, rotation and scale are then identity.
    /// </summary>
    public bool TransformIsExact { get; }

    /// <summary>Indices into <see cref="ModelData.Meshes"/> drawn at this node.</summary>
    public IReadOnlyList<int> MeshIndices { get; }

    /// <summary>Child nodes, in the source file's order.</summary>
    public IReadOnlyList<ModelNode> Children { get; }
}

/// <summary>
/// The interleaved vertex layout every imported submesh uses, matching
/// <see cref="Graphics.VertexAttribute.StandardLayout"/>.
/// </summary>
public static class ModelVertexLayout
{
    /// <summary>Floats per vertex: position (3) + normal (3) + uv (2).</summary>
    public const int FloatsPerVertex = 8;

    /// <summary>Float offset of the position within a vertex.</summary>
    public const int PositionOffset = 0;

    /// <summary>Float offset of the normal within a vertex.</summary>
    public const int NormalOffset = 3;

    /// <summary>Float offset of the uv within a vertex.</summary>
    public const int TexCoordOffset = 6;
}
