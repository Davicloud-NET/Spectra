using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// One 32-byte section-table record: where a section's bytes are and what kind they are.
/// A reader skips a kind it does not know, but still checks its bounds and alignment.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapSection
{
    /// <summary>The four-character code naming what this section holds.</summary>
    public readonly uint Kind;

    /// <summary>This section's own version, independent of the file's. Always 1 today; no reader checks it.</summary>
    public readonly ushort Version;

    /// <summary>Per-section properties. See <see cref="ScmapSectionFlags"/>.</summary>
    public readonly ushort Flags;

    /// <summary>Absolute offset of the section's first byte. 16-byte aligned.</summary>
    public readonly ulong Offset;

    /// <summary>Bytes stored in the file.</summary>
    public readonly ulong Size;

    /// <summary>
    /// Bytes after decoding. Equal to <see cref="Size"/> when stored as written, not zero:
    /// an empty section is legal.
    /// </summary>
    public readonly ulong UncompressedSize;

    /// <summary>Builds one section-table record.</summary>
    public ScmapSection(uint kind, ulong offset, ulong size, ushort version = 1, ScmapSectionFlags flags = ScmapSectionFlags.None)
    {
        Kind = kind;
        Version = version;
        Flags = (ushort)flags;
        Offset = offset;
        Size = size;
        UncompressedSize = size;
    }

    /// <summary><see cref="Flags"/> as the enum.</summary>
    public ScmapSectionFlags SectionFlags => (ScmapSectionFlags)Flags;
}
