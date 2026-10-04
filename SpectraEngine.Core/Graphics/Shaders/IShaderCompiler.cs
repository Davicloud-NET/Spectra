namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>Compiles SpectraShade (.spectrashade) source for one or more backends.</summary>
public interface IShaderCompiler
{
    /// <summary>Compiles the source for every backend in <paramref name="targets"/>.</summary>
    CompiledShaderFile Compile(string source, ReadOnlySpan<GraphicsBackend> targets);
}
