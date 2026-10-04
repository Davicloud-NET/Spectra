using System;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// Whole-file properties of a <c>.scmap</c>, in the header's <c>Flags</c> word.
/// Each says what the cook put in the file, not what a loader should do with it.
/// </summary>
[Flags]
public enum ScmapFlags : uint
{
    /// <summary>Nothing optional was written.</summary>
    None = 0,

    /// <summary>
    /// A <c>BRSH</c> section is present: authored brush planes.
    /// </summary>
    HasBrushSource = 1u << 0,

    /// <summary>A <c>LUAS</c> section is present: Luau source.</summary>
    HasScriptSource = 1u << 1,

    /// <summary>Debug information was kept rather than stripped.</summary>
    HasDebugInfo = 1u << 2,

    /// <summary>
    /// The file was laid out region-major for streaming. Reserved; nothing sets this.
    /// </summary>
    Streamable = 1u << 3,
}

/// <summary>
/// Per-section properties, in a section-table record's <c>Flags</c> half-word.
/// </summary>
[Flags]
public enum ScmapSectionFlags : ushort
{
    /// <summary>Stored as written.</summary>
    None = 0,

    /// <summary>
    /// The section's bytes are compressed, so <c>Size</c> and
    /// <c>UncompressedSize</c> differ. Reserved; the cook never sets it, since a
    /// compressed section cannot be read in place from a mapped view.
    /// </summary>
    Compressed = 1 << 0,
}
