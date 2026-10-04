using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Core.Maps.Compiled;

/// <summary>
/// The 64 bytes at offset 0 of a <c>.scmap</c> file, as they sit on disk.
/// A load gates on <see cref="FormatVersion"/>, <see cref="GeometryFormatVersion"/>
/// and <see cref="VertexLayoutId"/>; the other version words are informational.
/// </summary>
// Pack = 1 and every byte a declared field: this struct is cast to and from
// file bytes, so field order and size are the format.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ScmapHeader
{
    /// <summary>Always <see cref="ScmapFormat.Magic"/>; four bytes reading <c>SCMP</c>.</summary>
    public readonly uint Magic;

    /// <summary>
    /// The version this map was compiled at. Must equal
    /// <c>EngineInfo.CompiledMapFormatVersion</c>; any other value means recook.
    /// </summary>
    public readonly ushort FormatVersion;

    /// <summary>
    /// Bytes in this header. Always <see cref="ScmapFormat.HeaderSize"/> in v1,
    /// and the reader checks it.
    /// </summary>
    public readonly ushort HeaderSize;

    /// <summary>Whole-file properties. See <see cref="ScmapFlags"/>.</summary>
    public readonly uint Flags;

    /// <summary>Number of records in the section table.</summary>
    public readonly uint SectionCount;

    /// <summary>
    /// <c>XxHash128</c> of the source <c>.smap</c> bundle's canonical enumeration:
    /// sorted bundle-relative paths, each path's UTF-8 bytes then its file bytes.
    /// </summary>
    // Sorted, because directory walk order differs between machines.
    public readonly UInt128 SourceMapDigest;

    /// <summary>
    /// <c>EngineInfo.GeometryFormatVersion</c> at cook time. A mismatch refuses
    /// the load.
    /// </summary>
    public readonly uint GeometryFormatVersion;

    /// <summary>
    /// The authored map grammar the bake read, from the source document.
    /// Informational, never a load gate.
    /// </summary>
    public readonly uint MapFormatVersion;

    /// <summary>
    /// FNV-1a over the cooked vertex layout's <c>(semantic, component count)</c>
    /// pairs, so a geometry mismatch is reportable precisely.
    /// </summary>
    public readonly uint VertexLayoutId;

    /// <summary>
    /// <c>(Major &lt;&lt; 20) | (Minor &lt;&lt; 10) | Revision</c> of the engine
    /// that compiled this map. Informational, never a load gate.
    /// </summary>
    public readonly uint EngineVersion;

    /// <summary>Total bytes in the file, section padding included.</summary>
    public readonly ulong TotalSize;

    /// <summary>Reserved; written zero.</summary>
    public readonly ulong Reserved;

    /// <summary>Builds a header.</summary>
    public ScmapHeader(
        ushort formatVersion,
        ScmapFlags flags,
        uint sectionCount,
        UInt128 sourceMapDigest,
        uint geometryFormatVersion,
        uint mapFormatVersion,
        uint vertexLayoutId,
        uint engineVersion,
        ulong totalSize)
    {
        Magic = ScmapFormat.Magic;
        FormatVersion = formatVersion;
        HeaderSize = ScmapFormat.HeaderSize;
        Flags = (uint)flags;
        SectionCount = sectionCount;
        SourceMapDigest = sourceMapDigest;
        GeometryFormatVersion = geometryFormatVersion;
        MapFormatVersion = mapFormatVersion;
        VertexLayoutId = vertexLayoutId;
        EngineVersion = engineVersion;
        TotalSize = totalSize;
        Reserved = 0;
    }

    /// <summary><see cref="Flags"/> as the enum.</summary>
    public ScmapFlags FileFlags => (ScmapFlags)Flags;
}
