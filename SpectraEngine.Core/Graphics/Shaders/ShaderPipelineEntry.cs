namespace SpectraEngine.Core.Graphics.Shaders;

/// <summary>One entry in the pipeline table of a .specshadecomp file.</summary>
public readonly struct ShaderPipelineEntry
{
    public GraphicsBackend Backend { get; }

    public ShaderDataFormat Format { get; }

    public ShaderStageFlags Stages { get; }

    /// <summary>Byte offset of the blob from the start of the data section.</summary>
    public uint DataOffset { get; }

    public uint DataSize { get; }

    public ShaderPipelineEntry(GraphicsBackend backend, ShaderDataFormat format, ShaderStageFlags stages, uint dataOffset, uint dataSize)
    {
        Backend = backend;
        Format = format;
        Stages = stages;
        DataOffset = dataOffset;
        DataSize = dataSize;
    }
}
