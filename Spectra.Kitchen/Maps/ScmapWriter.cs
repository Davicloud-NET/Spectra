using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SpectraEngine.Core;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Maps;

/// <summary>
/// Writes one section's body. Must write the number of bytes that was declared.
/// </summary>
public delegate void ScmapSectionBodyWriter(Stream stream);

/// <summary>
/// Writes a <c>.scmap</c> container: the header, the section table and the
/// section bodies.
/// </summary>
// A section declares its size before its bytes exist, so a large body can be
// streamed. Write checks every section against the layout. Padding is written
// as zeros, never seeked over.
public sealed class ScmapWriter
{
    private readonly List<PendingSection> _sections = [];
    private readonly ScmapFlags _flags;
    private readonly UInt128 _sourceMapDigest;
    private readonly uint _mapFormatVersion;

    /// <summary>Creates a writer.</summary>
    /// <param name="sourceMapDigest">See <see cref="MapBundleDigest"/>.</param>
    /// <param name="mapFormatVersion">
    /// The authored map format version the bake read. Informational; a load does
    /// not check it.
    /// </param>
    public ScmapWriter(ScmapFlags flags, UInt128 sourceMapDigest, uint mapFormatVersion)
    {
        _flags = flags;
        _sourceMapDigest = sourceMapDigest;
        _mapFormatVersion = mapFormatVersion;
    }

    /// <summary>Number of sections added.</summary>
    public int Count => _sections.Count;

    /// <summary>Adds a section whose body is already in hand.</summary>
    public void AddSection(uint kind, ReadOnlySpan<byte> body)
    {
        byte[] copy = body.ToArray();
        AddSection(kind, copy.Length, stream => stream.Write(copy));
    }

    /// <summary>
    /// Adds a section that declares its length now and writes its bytes later.
    /// Throws for a reserved code or one already added.
    /// </summary>
    public void AddSection(uint kind, long bodySize, ScmapSectionBodyWriter write)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentOutOfRangeException.ThrowIfNegative(bodySize);

        // A reader skips unknown codes, so nothing downstream would catch these.
        if (kind == ScmapFormat.RegionIndexSection || kind == ScmapFormat.BrushModelSection)
        {
            throw new ArgumentException(
                $"Section '{ScmapFormat.DescribeFourCc(kind)}' is reserved and has no producer: " +
                "RGNI belongs to a streaming design nothing builds, and BMDL to a fused brush model whose " +
                "mechanism was overturned. Emitting either spends a code the format has promised elsewhere, " +
                "and a reader would step over it in silence.", nameof(kind));
        }

        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].Kind != kind) continue;

            throw new ArgumentException(
                $"Section '{ScmapFormat.DescribeFourCc(kind)}' was added twice.", nameof(kind));
        }

        _sections.Add(new PendingSection(kind, bodySize, write));
    }

    /// <summary>Writes the file to <paramref name="path"/>.</summary>
    public void WriteToFile(string path)
    {
        using FileStream stream = File.Create(path);
        Write(stream);
    }

    /// <summary>
    /// Writes the file. Can be called again for identical output. Throws if a
    /// section writes a different number of bytes than it declared.
    /// </summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ScmapFormat.RequireLittleEndian();

        var sizes = new ScmapSectionSize[_sections.Count];
        for (int i = 0; i < _sections.Count; i++)
        {
            sizes[i] = new ScmapSectionSize(_sections[i].Kind, _sections[i].BodySize);
        }

        ScmapLayout layout = ScmapLayout.Compute(sizes);

        var header = new ScmapHeader(
            EngineInfo.CompiledMapFormatVersion,
            _flags,
            (uint)_sections.Count,
            _sourceMapDigest,
            EngineInfo.GeometryFormatVersion,
            _mapFormatVersion,
            ScmapFormat.StandardVertexLayoutId,
            EngineVersionWord,
            (ulong)layout.TotalSize);

        var region = new RegionStream(stream);

        Span<byte> headerBytes = stackalloc byte[ScmapFormat.HeaderSize];
        MemoryMarshal.Write(headerBytes, in header);
        region.Write(headerBytes);

        Span<byte> sectionBytes = stackalloc byte[ScmapFormat.SectionSize];
        for (int i = 0; i < _sections.Count; i++)
        {
            var record = new ScmapSection(
                layout.KindAt(i),
                (ulong)layout.OffsetAt(i),
                (ulong)layout.BodySizeAt(i));

            MemoryMarshal.Write(sectionBytes, in record);
            region.Write(sectionBytes);
        }

        // Zero bytes today; the layout decides where the first section starts.
        long firstBody = _sections.Count > 0 ? layout.OffsetAt(0) : layout.TotalSize;
        region.WriteZeros(firstBody - region.Position);

        for (int i = 0; i < _sections.Count; i++)
        {
            PendingSection section = _sections[i];

            if (region.Position != layout.OffsetAt(i))
            {
                throw new InvalidOperationException(
                    $"Compiled map layout disagrees with what was written: section " +
                    $"'{ScmapFormat.DescribeFourCc(section.Kind)}' was placed at byte {layout.OffsetAt(i)} " +
                    $"and the writer is at byte {region.Position}.");
            }

            long start = region.Position;
            section.Write(region);
            long written = region.Position - start;

            if (written != section.BodySize)
            {
                throw new InvalidOperationException(
                    $"Section '{ScmapFormat.DescribeFourCc(section.Kind)}' declared {section.BodySize} bytes " +
                    $"and wrote {written}. The layout was computed from the declaration, so every later " +
                    "section is now placed somewhere the table does not say.");
            }

            region.WriteZeros(layout.PaddedSizeAt(i) - written);
        }

        if (region.Position != layout.TotalSize)
        {
            throw new InvalidOperationException(
                $"Compiled map layout disagrees with what was written: the header declares " +
                $"{layout.TotalSize} bytes and the writer ended at {region.Position}.");
        }
    }

    private static uint EngineVersionWord =>
        ((uint)EngineInfo.MajorVersion << 20) | ((uint)EngineInfo.MinorVersion << 10) | EngineInfo.RevisionVersion;

    private readonly record struct PendingSection(uint Kind, long BodySize, ScmapSectionBodyWriter Write);

    // Counts bytes itself: the destination stream may not be seekable.
    private sealed class RegionStream(Stream inner) : Stream
    {
        private static readonly byte[] Zeros = new byte[ScmapFormat.PayloadAlignment];

        private long _position;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _position;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public void WriteZeros(long count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            while (count > 0)
            {
                int chunk = (int)Math.Min(count, Zeros.Length);
                Write(Zeros.AsSpan(0, chunk));
                count -= chunk;
            }
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            _position += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void WriteByte(byte value)
        {
            Span<byte> one = [value];
            Write(one);
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
