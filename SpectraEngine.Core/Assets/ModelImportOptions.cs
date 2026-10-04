using Silk.NET.Assimp;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Settings for <see cref="ModelImporter.Import"/>. The defaults suit static
/// prop geometry. Immutable.
/// </summary>
public sealed record ModelImportOptions
{
    /// <summary>The settings used when a caller passes none.</summary>
    public static ModelImportOptions Default { get; } = new();

    /// <summary>CPU data kept after the GPU upload. The default keeps everything.</summary>
    public ModelCpuRetention CpuRetention { get; init; } = ModelCpuRetention.Full;

    /// <summary>Split polygons into triangles. Nothing downstream can draw an n-gon.</summary>
    public bool Triangulate { get; init; } = true;

    /// <summary>
    /// Generate smoothed normals for meshes that carry none. Authored normals
    /// are kept.
    /// </summary>
    public bool GenerateMissingNormals { get; init; } = true;

    /// <summary>Merge vertices that are identical in every attribute.</summary>
    public bool JoinIdenticalVertices { get; init; } = true;

    /// <summary>Reorder triangles for vertex-cache locality.</summary>
    public bool OptimizeVertexCache { get; init; } = true;

    /// <summary>Run the importer's own consistency validation.</summary>
    public bool Validate { get; init; } = true;

    /// <summary>
    /// Flip the v texture coordinate. The engine samples v = 0 at the bottom,
    /// which OBJ already matches. Leave it off for glTF too: Assimp converts
    /// glTF's top-down v itself, so turning this on flips it back.
    /// </summary>
    public bool FlipTextureV { get; init; }

    /// <summary>
    /// Reverse triangle winding. The engine draws counter-clockwise front faces;
    /// turn this on for content exported clockwise.
    /// </summary>
    public bool FlipWinding { get; init; }

    internal uint BuildPostProcessFlags()
    {
        PostProcessSteps steps = PostProcessSteps.None;

        if (Triangulate) steps |= PostProcessSteps.Triangulate;
        if (GenerateMissingNormals) steps |= PostProcessSteps.GenerateSmoothNormals;
        if (JoinIdenticalVertices) steps |= PostProcessSteps.JoinIdenticalVertices;
        if (OptimizeVertexCache) steps |= PostProcessSteps.ImproveCacheLocality;
        if (Validate) steps |= PostProcessSteps.ValidateDataStructure;
        if (FlipTextureV) steps |= PostProcessSteps.FlipUVs;
        if (FlipWinding) steps |= PostProcessSteps.FlipWindingOrder;

        // Always on. Degenerate triangles become points/lines, get split into
        // their own meshes, and the converter drops those. Every ModelMesh is
        // then triangles only.
        steps |= PostProcessSteps.FindDegenerates | PostProcessSteps.SortByPrimitiveType;

        return (uint)steps;
    }
}
