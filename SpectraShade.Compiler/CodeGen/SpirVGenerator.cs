using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.Shaders;
using SpectraShade.Compiler.Syntax;

namespace SpectraShade.Compiler.CodeGen;

/// <summary>SPIR-V generator for the Vulkan backend. Not implemented yet.</summary>
public sealed class SpirVGenerator : ICodeGenerator
{
    public GraphicsBackend Backend => GraphicsBackend.Vulkan;
    public ShaderDataFormat OutputFormat => ShaderDataFormat.SpirV;

    public PipelineBlob Generate(CompilationUnit unit)
    {
        throw new NotImplementedException("SPIR-V code generation is not yet implemented");
    }
}
