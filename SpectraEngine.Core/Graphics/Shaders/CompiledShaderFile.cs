using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>
/// In-memory representation of a .specshadecomp file.
/// Contains compiled shader data for one or more graphics backends.
/// </summary>
public sealed class CompiledShaderFile
{
    /// <summary>Magic bytes: "SSCO" (SpectraShade Compiled Object).</summary>
    public static ReadOnlySpan<byte> MagicBytes => "SSCO"u8;

    /// <summary>The extension a compiled shader is written with, dot included.</summary>
    public const string FileExtension = ".specshadecomp";

    /// <summary>Format version of this file. Must match EngineInfo.ShaderFormatVersion to be loadable.</summary>
    public ushort FormatVersion { get; init; }

    /// <summary>Shader stages present across all pipeline entries.</summary>
    public ShaderStageFlags Stages { get; init; }

    /// <summary>Per-backend pipeline entries with their compiled data.</summary>
    public required IReadOnlyList<PipelineBlob> Pipelines { get; init; }

    /// <summary>The compiled data for a backend, or null if the file has none for it.</summary>
    public PipelineBlob? GetPipeline(GraphicsBackend backend)
    {
        for (int i = 0; i < Pipelines.Count; i++)
        {
            if (Pipelines[i].Backend == backend)
                return Pipelines[i];
        }
        return null;
    }
}

/// <summary>
/// Compiled shader data for a single graphics backend.
/// </summary>
public sealed class PipelineBlob
{
    public required GraphicsBackend Backend { get; init; }
    public required ShaderDataFormat Format { get; init; }
    public required ShaderStageFlags Stages { get; init; }

    /// <summary>Vertex stage data. Null if this stage wasn't compiled.</summary>
    public byte[]? VertexData { get; init; }

    /// <summary>Fragment/pixel stage data. Null if this stage wasn't compiled.</summary>
    public byte[]? FragmentData { get; init; }

    /// <summary>Geometry stage data. Null if this stage wasn't compiled.</summary>
    public byte[]? GeometryData { get; init; }

    /// <summary>Compute stage data. Null if this stage wasn't compiled.</summary>
    public byte[]? ComputeData { get; init; }

    /// <summary>
    /// The vertex inputs the shader declares, in the order its input struct
    /// declares them. Empty for a shader with no vertex stage. The same on every backend.
    /// </summary>
    public IReadOnlyList<VertexInputElement> VertexInputs { get; init; } = [];

    /// <summary>
    /// A second vertex stage for the same shader, with its per-instance uniform
    /// arriving as a vertex input instead. Null unless the source marked a
    /// <c>cbuffer</c> field <c>[PerInstance]</c>. Pairs with <see cref="FragmentData"/>.
    /// </summary>
    public byte[]? InstancedVertexData { get; init; }

    /// <summary>
    /// The vertex inputs <see cref="InstancedVertexData"/> declares, which is
    /// <see cref="VertexInputs"/> plus the per-instance matrix. Empty when there
    /// is no instanced variant.
    /// </summary>
    public IReadOnlyList<VertexInputElement> InstancedVertexInputs { get; init; } = [];
}
