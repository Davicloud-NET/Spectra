using System;
using System.IO;
using System.Runtime.InteropServices;
using Spectra.Kitchen.Maps;
using SpectraEngine.Core;
using SpectraEngine.Core.Maps.Compiled;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The compiled-map container: header, section table, padding, and the check
/// that each section lands where the layout put it.
/// </summary>
public class ScmapWriterTests
{
    [Fact]
    public void A_section_that_writes_fewer_bytes_than_it_declared_is_refused_by_name()
    {
        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, new byte[8]);
        writer.AddSection(ScmapFormat.MetaSection, bodySize: 48, stream => stream.Write(new byte[32]));
        writer.AddSection(ScmapFormat.NodeSection, new byte[16]);

        using var buffer = new MemoryStream();
        InvalidOperationException failure = Should.Throw<InvalidOperationException>(() => writer.Write(buffer));

        failure.Message.ShouldContain("META");
        failure.Message.ShouldContain("48");
        failure.Message.ShouldContain("32");
    }

    [Fact]
    public void A_section_that_writes_more_bytes_than_it_declared_is_refused_by_name()
    {
        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, bodySize: 16, stream => stream.Write(new byte[17]));
        writer.AddSection(ScmapFormat.NodeSection, new byte[16]);

        using var buffer = new MemoryStream();
        InvalidOperationException failure = Should.Throw<InvalidOperationException>(() => writer.Write(buffer));

        failure.Message.ShouldContain("STRT");
    }

    [Fact]
    public void A_layout_and_a_write_that_agree_produce_sections_at_the_declared_offsets()
    {
        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, new byte[7]);
        writer.AddSection(ScmapFormat.AssetSection, []);
        writer.AddSection(ScmapFormat.MetaSection, new byte[48]);
        writer.AddSection(ScmapFormat.NodeSection, new byte[96]);
        writer.AddSection(ScmapFormat.ChunkDirectorySection, new byte[1]);

        using var buffer = new MemoryStream();
        writer.Write(buffer);
        byte[] file = buffer.ToArray();

        ScmapHeader header = MemoryMarshal.Read<ScmapHeader>(file);
        header.SectionCount.ShouldBe(5u);
        header.TotalSize.ShouldBe((ulong)file.Length);
        header.HeaderSize.ShouldBe((ushort)ScmapFormat.HeaderSize);
        header.GeometryFormatVersion.ShouldBe(EngineInfo.GeometryFormatVersion);
        header.VertexLayoutId.ShouldBe(ScmapFormat.StandardVertexLayoutId);

        long cursor = ScmapFormat.SectionTableOffset + (5 * ScmapFormat.SectionSize);
        for (int i = 0; i < 5; i++)
        {
            ScmapSection section = MemoryMarshal.Read<ScmapSection>(
                file.AsSpan(ScmapFormat.SectionTableOffset + (i * ScmapFormat.SectionSize)));

            ((long)section.Offset % ScmapFormat.PayloadAlignment).ShouldBe(0);
            ((long)section.Offset).ShouldBe(cursor);
            section.UncompressedSize.ShouldBe(section.Size);

            cursor += ScmapLayout.PaddedSectionSize((long)section.Size);
        }

        cursor.ShouldBe(file.Length);
    }

    [Fact]
    public void The_padding_between_sections_is_written_rather_than_seeked_over()
    {
        // Pre-filled with 0xCD, so a gap that was seeked over shows.
        var backing = new byte[512];
        backing.AsSpan().Fill(0xCD);

        using var buffer = new MemoryStream(backing, 0, backing.Length, writable: true, publiclyVisible: true);

        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, new byte[] { 0xAB });
        writer.Write(buffer);

        int expected = ScmapFormat.HeaderSize + ScmapFormat.SectionSize + ScmapFormat.PayloadAlignment;
        buffer.Position.ShouldBe(expected);

        backing[expected - 16].ShouldBe((byte)0xAB);
        for (int i = expected - 15; i < expected; i++) backing[i].ShouldBe((byte)0, $"padding byte {i}");

        // Nothing past the end was touched.
        backing[expected].ShouldBe((byte)0xCD);
    }

    [Fact]
    public void Two_writes_of_one_writer_produce_the_same_bytes()
    {
        var writer = new ScmapWriter(ScmapFlags.HasDebugInfo, new UInt128(7, 9), EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, new byte[] { 1, 2, 3 });
        writer.AddSection(ScmapFormat.NodeSection, new byte[80]);

        using var first = new MemoryStream();
        using var second = new MemoryStream();
        writer.Write(first);
        writer.Write(second);

        second.ToArray().ShouldBe(first.ToArray());
    }

    [Fact]
    public void A_reserved_section_code_is_refused_by_name()
    {
        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);

        Should.Throw<ArgumentException>(() => writer.AddSection(ScmapFormat.RegionIndexSection, new byte[4]))
            .Message.ShouldContain("RGNI");

        Should.Throw<ArgumentException>(() => writer.AddSection(ScmapFormat.BrushModelSection, new byte[4]))
            .Message.ShouldContain("BMDL");
    }

    [Fact]
    public void A_section_added_twice_is_refused()
    {
        var writer = new ScmapWriter(ScmapFlags.None, 0, EngineInfo.MapFormatVersion);
        writer.AddSection(ScmapFormat.StringSection, new byte[4]);

        Should.Throw<ArgumentException>(() => writer.AddSection(ScmapFormat.StringSection, new byte[4]))
            .Message.ShouldContain("STRT");
    }

    [Fact]
    public void The_layouts_padded_size_is_the_only_expression_of_what_a_section_costs()
    {
        ScmapLayout.PaddedSectionSize(0).ShouldBe(0);
        ScmapLayout.PaddedSectionSize(1).ShouldBe(16);
        ScmapLayout.PaddedSectionSize(16).ShouldBe(16);
        ScmapLayout.PaddedSectionSize(17).ShouldBe(32);

        ScmapLayout layout = ScmapLayout.Compute([
            new ScmapSectionSize(ScmapFormat.StringSection, 17),
            new ScmapSectionSize(ScmapFormat.NodeSection, 3),
        ]);

        long tableEnd = ScmapFormat.SectionTableOffset + (2 * ScmapFormat.SectionSize);
        layout.OffsetAt(0).ShouldBe(tableEnd);
        layout.OffsetAt(1).ShouldBe(tableEnd + 32);
        layout.TotalSize.ShouldBe(tableEnd + 32 + 16);
    }
}
