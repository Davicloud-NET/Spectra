namespace SpectraEngine.Core.Graphics.Shaders;

// Byte layout of a .specshadecomp file. Both readers and the writer take their
// sizes from here; a second copy of this arithmetic misparses without throwing.
internal static class ShaderFileLayout
{
    // Magic, format version, stage flags, pipeline count.
    public const int HeaderSize = 8;

    // Backend, format, stages, reserved, offset, size.
    public const int EntrySize = 12;

    // Location, span, component count, rate, name byte length. The name follows.
    public const int VertexInputRecordSize = 15;

    // Entry DataOffset values are measured from here.
    public static long DataSectionStart(int pipelineCount) =>
        HeaderSize + (pipelineCount * (long)EntrySize);
}
