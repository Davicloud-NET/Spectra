using Spectra.Kitchen.Models;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Models;
using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// <see cref="SmodelWriter"/> against the engine's reader, and against
/// <see cref="HandBuiltSmodel"/>, a transcription of the format spec that
/// shares no code with either.
/// </summary>
public class SmodelCodecTests
{
    private const string Material = "Materials/fixture.spectramat";

    // Two triangles sharing vertices, so an index range is not a vertex slice.
    private static readonly float[] Vertices = Interleave(
        [
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 0f, 0.5f),
            new Vector3(0f, 3f, 1.5f),
            new Vector3(-4f, 1f, -2f),
        ]);

    private static readonly uint[] Indices = [0, 1, 2, 2, 1, 3];

    [Fact]
    public void What_the_writer_wrote_is_what_the_reader_reads()
    {
        byte[] file = SmodelWriter.Write(
            Vertices,
            Indices,
            [
                new SmodelSubmeshSpec(0, 3, Material),
                new SmodelSubmeshSpec(3, 3, null),
            ]);

        SmodelModel model = SmodelReader.Read(file, "Models/fixture.smodel");

        model.VertexCount.ShouldBe(4);
        model.VertexStrideFloats.ShouldBe(8u);
        model.VertexLayoutId.ShouldBe(SmodelStandardLayout.LayoutId);
        model.Vertices.ToArray().ShouldBe(Vertices);

        model.Index32.ShouldBeFalse("four vertices fit in sixteen bits");
        model.IndexCount.ShouldBe(6);
        for (int i = 0; i < Indices.Length; i++) model.IndexAt(i).ShouldBe(Indices[i]);

        model.Submeshes.Length.ShouldBe(2);
        model.Submeshes[0].IndexStart.ShouldBe(0u);
        model.Submeshes[0].IndexCount.ShouldBe(3u);
        model.GetName(model.Submeshes[0].MaterialNameOffset).ShouldBe(Material);

        // A sentinel, not zero: zero is the first name record's offset.
        model.Submeshes[1].HasMaterial.ShouldBeFalse();
        model.Submeshes[1].MaterialNameOffset.ShouldBe(SmodelFormat.NameOffsetAbsent);

        model.SkippedSectionCount.ShouldBe(0);
        model.HasSkeleton.ShouldBeFalse();
        model.HasCollision.ShouldBeFalse();
    }

    [Fact]
    public void Bounds_are_computed_per_submesh_from_the_vertices_its_own_indices_name()
    {
        byte[] file = SmodelWriter.Write(
            Vertices,
            Indices,
            [
                new SmodelSubmeshSpec(0, 3, null),
                new SmodelSubmeshSpec(3, 3, null),
            ]);

        SmodelModel model = SmodelReader.Read(file, "Models/fixture.smodel");

        model.Submeshes[0].BoundsMin.ShouldBe(new Vector3(0f, 0f, 0f));
        model.Submeshes[0].BoundsMax.ShouldBe(new Vector3(2f, 3f, 1.5f));

        model.Submeshes[1].BoundsMin.ShouldBe(new Vector3(-4f, 0f, -2f));
        model.Submeshes[1].BoundsMax.ShouldBe(new Vector3(2f, 3f, 1.5f));

        model.BoundsMin.ShouldBe(new Vector3(-4f, 0f, -2f));
        model.BoundsMax.ShouldBe(new Vector3(2f, 3f, 1.5f));
    }

    [Fact]
    public void One_material_named_twice_is_one_name_record()
    {
        byte[] file = SmodelWriter.Write(
            Vertices,
            Indices,
            [
                new SmodelSubmeshSpec(0, 3, Material),
                new SmodelSubmeshSpec(3, 3, Material),
            ]);

        SmodelModel model = SmodelReader.Read(file, "Models/fixture.smodel");

        model.Submeshes[0].MaterialNameOffset.ShouldBe(model.Submeshes[1].MaterialNameOffset);

        // The whole blob is one length-prefixed record.
        model.Names.Length.ShouldBe(sizeof(ushort) + Material.Length);
    }

    [Fact]
    public void The_bytes_are_the_ones_a_transcription_of_the_specification_produces()
    {
        byte[] written = SmodelWriter.Write(Vertices, Indices, [new SmodelSubmeshSpec(0, 6, Material)]);

        byte[] transcribed = new HandBuiltSmodel
        {
            FormatVersion = EngineInfo.ModelFormatVersion,
            GeometryFormatVersion = EngineInfo.GeometryFormatVersion,
            Bounds = [-4f, 0f, -2f, 2f, 3f, 1.5f],
        }
            .VertexLayout(
                strideFloats: 8,
                (Semantic: (byte)0, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)0),
                (Semantic: (byte)1, ComponentType: (byte)0, ComponentCount: (byte)3, ByteOffset: (ushort)12),
                (Semantic: (byte)3, ComponentType: (byte)0, ComponentCount: (byte)2, ByteOffset: (ushort)24))
            .VertexBuffer(Vertices)
            .Indices16(0, 1, 2, 2, 1, 3)
            .Submeshes((0u, 6u, 0u, [-4f, 0f, -2f, 2f, 3f, 1.5f]))
            .Names(out _, Material)
            .Build();

        written.ShouldBe(transcribed);
    }

    [Fact]
    public void A_model_past_sixteen_bit_indices_says_so_in_its_flags_and_its_buffer()
    {
        // Index width follows the vertex count, not the largest index used.
        int vertexCount = SmodelWriter.MaxVertexCountForIndex16 + 1;
        var vertices = new float[vertexCount * 8];
        for (int v = 0; v < vertexCount; v++) vertices[v * 8] = v;

        uint[] indices = [0, 1, (uint)(vertexCount - 1)];

        byte[] file = SmodelWriter.Write(vertices, indices, [new SmodelSubmeshSpec(0, 3, null)]);
        SmodelModel model = SmodelReader.Read(file, "Models/big.smodel");

        model.Index32.ShouldBeTrue();
        model.IndexAt(2).ShouldBe((uint)(vertexCount - 1));

        // One vertex fewer still fits in 16 bits.
        var smaller = new float[SmodelWriter.MaxVertexCountForIndex16 * 8];
        SmodelReader.Read(
            SmodelWriter.Write(smaller, [0, 1, 2], [new SmodelSubmeshSpec(0, 3, null)]),
            "Models/edge.smodel").Index32.ShouldBeFalse();
    }

    [Fact]
    public void Every_section_starts_on_the_payload_alignment()
    {
        // VBUF, IBUF and the collision planes are cast in place.
        byte[] file = SmodelWriter.Write(Vertices, Indices, [new SmodelSubmeshSpec(0, 6, Material)]);

        uint sections = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x0C));
        sections.ShouldBe(5u, "VTXL, VBUF, IBUF, SUBM and NAME");

        for (int i = 0; i < sections; i++)
        {
            ReadOnlySpan<byte> record = file.AsSpan(
                SmodelFormat.SectionTableOffset + (i * SmodelFormat.SectionSize), SmodelFormat.SectionSize);

            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            ulong length = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);

            (offset % SmodelFormat.PayloadAlignment).ShouldBe(0ul);
            (offset + length).ShouldBeLessThanOrEqualTo((ulong)file.Length);
        }
    }

    [Fact]
    public void A_model_with_no_material_at_all_carries_no_name_section()
    {
        byte[] file = SmodelWriter.Write(Vertices, Indices, [new SmodelSubmeshSpec(0, 6, null)]);

        BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(0x0C)).ShouldBe(4u);
        SmodelReader.Read(file, "Models/plain.smodel").Names.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void An_index_past_the_vertex_buffer_is_refused()
    {
        Refuse(() => SmodelWriter.Write(Vertices, [0, 1, 9], [new SmodelSubmeshSpec(0, 3, null)]), "vertex 9");
    }

    [Fact]
    public void A_submesh_range_past_the_index_buffer_is_refused()
    {
        Refuse(
            () => SmodelWriter.Write(Vertices, Indices, [new SmodelSubmeshSpec(3, 6, null)]),
            "indices 3 to 9");
    }

    [Fact]
    public void Geometry_that_is_not_whole_triangles_or_whole_vertices_is_refused()
    {
        Refuse(() => SmodelWriter.Write(Vertices, [0, 1], [new SmodelSubmeshSpec(0, 2, null)]), "triangles");
        Refuse(
            () => SmodelWriter.Write(Vertices.AsSpan(0, 7), Indices, [new SmodelSubmeshSpec(0, 6, null)]),
            "8-float vertices");
        Refuse(() => SmodelWriter.Write(Vertices, Indices, []), "at least one submesh");
    }

    private static void Refuse(Action write, string expected) =>
        Should.Throw<ArgumentException>(write).Message.ShouldContain(expected);

    private static float[] Interleave(Vector3[] positions)
    {
        var vertices = new float[positions.Length * 8];
        for (int v = 0; v < positions.Length; v++)
        {
            vertices[v * 8] = positions[v].X;
            vertices[(v * 8) + 1] = positions[v].Y;
            vertices[(v * 8) + 2] = positions[v].Z;
            vertices[(v * 8) + 5] = 1f;                 // unit normal
            vertices[(v * 8) + 6] = v * 0.25f;          // uv varies per vertex
        }

        return vertices;
    }
}
