using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.CodeGen;

/// <summary>Generates one backend's shader code from a SpectraShade AST.</summary>
public interface ICodeGenerator
{
    GraphicsBackend Backend { get; }
    ShaderDataFormat OutputFormat { get; }

    /// <summary>Generates the per-stage output for <paramref name="unit"/>.</summary>
    PipelineBlob Generate(CompilationUnit unit);
}

/// <summary>Attaches a shader's instanced variant to its blob.</summary>
// Not an interface member: the variant comes from running the same generator
// over a rewritten AST.
public static class InstancedBlob
{
    /// <summary>
    /// Returns <paramref name="blob"/> carrying the instanced vertex stage from
    /// <paramref name="instancedBlob"/>.
    /// </summary>
    public static PipelineBlob With(PipelineBlob blob, PipelineBlob instancedBlob) => new()
    {
        Backend = blob.Backend,
        Format = blob.Format,
        Stages = blob.Stages,
        VertexData = blob.VertexData,
        FragmentData = blob.FragmentData,
        GeometryData = blob.GeometryData,
        ComputeData = blob.ComputeData,
        VertexInputs = blob.VertexInputs,
        InstancedVertexData = instancedBlob.VertexData,
        InstancedVertexInputs = instancedBlob.VertexInputs,
    };
}
