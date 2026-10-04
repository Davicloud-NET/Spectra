using System.Numerics;
using System.Runtime.CompilerServices;
using SpectraEngine.Core;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Bsp;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What <see cref="SmodelReader"/> accepts and refuses, and that a refusal names what was wrong.
/// </summary>
// Files come from HandBuiltSmodel, which writes bytes from the spec and uses no
// engine types. A reader tested against its own writer only proves they agree.
public sealed class SmodelFormatTests
{
    // From the spec, not imported from the engine.
    private const ushort HasSkeletonFlag = 1 << 0;
    private const ushort HasCollisionFlag = 1 << 1;
    private const ushort Index32Flag = 1 << 2;

    private const string Source = "Models/probe.smodel";

    [Fact]
    public void The_records_cast_out_of_a_mapped_view_are_exactly_the_bytes_the_format_declares()
    {
        // Plane and Vector3 layouts are not a documented framework contract.
        Unsafe.SizeOf<Plane>().ShouldBe(16);
        Unsafe.SizeOf<Vector3>().ShouldBe(12);

        Unsafe.SizeOf<SmodelVertexAttribute>().ShouldBe(8);
        Unsafe.SizeOf<SmodelSubmesh>().ShouldBe(40);
        Unsafe.SizeOf<SmodelLod>().ShouldBe(12);
        Unsafe.SizeOf<SmodelJoint>().ShouldBe(56);
        Unsafe.SizeOf<SmodelCollisionHull>().ShouldBe(8);

        SmodelFormat.VertexAttributeSize.ShouldBe(Unsafe.SizeOf<SmodelVertexAttribute>());
        SmodelFormat.SubmeshSize.ShouldBe(Unsafe.SizeOf<SmodelSubmesh>());
        SmodelFormat.LodSize.ShouldBe(Unsafe.SizeOf<SmodelLod>());
        SmodelFormat.JointSize.ShouldBe(Unsafe.SizeOf<SmodelJoint>());
        SmodelFormat.CollisionHullSize.ShouldBe(Unsafe.SizeOf<SmodelCollisionHull>());
        SmodelFormat.CollisionPlaneSize.ShouldBe(Unsafe.SizeOf<Plane>());

        SmodelFormat.HeaderSize.ShouldBe(HandBuiltSmodel.HeaderSize);
        SmodelFormat.SectionTableOffset.ShouldBe(HandBuiltSmodel.SectionTableOffset);
        SmodelFormat.SectionSize.ShouldBe(HandBuiltSmodel.SectionSize);
        SmodelFormat.PayloadAlignment.ShouldBe(HandBuiltSmodel.PayloadAlignment);
        SmodelFormat.NameOffsetAbsent.ShouldBe(HandBuiltSmodel.NameOffsetAbsent);
    }

    [Fact]
    public void A_hand_built_model_round_trips_through_the_reader()
    {
        byte[] file = ValidModel(out uint[] names).Build();

        SmodelModel model = SmodelReader.Read(file, Source);

        model.Source.ShouldBe(Source);
        model.SkippedSectionCount.ShouldBe(0);
        model.BoundsMin.ShouldBeCloseTo(new Vector3(-1f, -2f, -3f), 1e-6f);
        model.BoundsMax.ShouldBeCloseTo(new Vector3(4f, 5f, 6f), 1e-6f);

        model.VertexStrideFloats.ShouldBe(8u);
        model.VertexAttributes.Length.ShouldBe(3);
        model.VertexAttributes[0].Semantic.ShouldBe(SmodelSemantic.Position);
        model.VertexAttributes[0].ComponentType.ShouldBe(SmodelComponentType.Float32);
        model.VertexAttributes[0].ComponentCount.ShouldBe((byte)3);
        model.VertexAttributes[0].ByteOffset.ShouldBe((ushort)0);
        model.VertexAttributes[2].Semantic.ShouldBe(SmodelSemantic.Uv0);
        model.VertexAttributes[2].ByteOffset.ShouldBe((ushort)24);

        model.VertexCount.ShouldBe(4);
        model.Vertices.Length.ShouldBe(32);
        model.Vertices[0].ShouldBe(-1f);
        model.Vertices[8].ShouldBe(1f);

        model.Index32.ShouldBeFalse();
        model.IndexCount.ShouldBe(6);
        model.Indices32.IsEmpty.ShouldBeTrue();
        model.IndexAt(4).ShouldBe(2u);

        model.Submeshes.Length.ShouldBe(2);
        model.Submeshes[0].IndexStart.ShouldBe(0u);
        model.Submeshes[0].IndexCount.ShouldBe(3u);
        model.Submeshes[1].IndexStart.ShouldBe(3u);
        model.GetName(model.Submeshes[0].MaterialNameOffset).ShouldBe("Materials/crate.spectramat");
        model.GetName(model.Submeshes[1].MaterialNameOffset).ShouldBe("Materials/glass.spectramat");

        // Bounds are the tail of the record, so this checks the whole 40 bytes.
        model.Submeshes[1].BoundsMin.ShouldBeCloseTo(new Vector3(-1f), 1e-6f);
        model.Submeshes[1].BoundsMax.ShouldBeCloseTo(new Vector3(1f), 1e-6f);

        model.Lods.Length.ShouldBe(2);
        model.Lods[0].ScreenHeightThreshold.ShouldBe(0.5f);
        model.Lods[0].FirstSubmesh.ShouldBe(0u);
        model.Lods[0].SubmeshCount.ShouldBe(1u);
        model.Lods[1].ScreenHeightThreshold.ShouldBe(0f);
        model.Lods[1].FirstSubmesh.ShouldBe(1u);
        model.Lods[1].SubmeshCount.ShouldBe(1u);

        model.HasSkeleton.ShouldBeTrue();
        model.Joints.Length.ShouldBe(3);
        model.Joints[0].IsRoot.ShouldBeTrue();
        model.Joints[1].ParentIndex.ShouldBe(0);
        model.Joints[2].ParentIndex.ShouldBe(1);
        model.GetName(model.Joints[2].NameOffset).ShouldBe("hand");

        // Four rows of three: the fourth row is the translation.
        model.Joints[1].InverseBind.Translation.ShouldBeCloseTo(new Vector3(7f, 8f, 9f), 1e-6f);

        model.HasCollision.ShouldBeTrue();
        model.CollisionHulls.Length.ShouldBe(1);
        model.CollisionPlanes.Length.ShouldBe(6);
        model.PlanesOf(model.CollisionHulls[0]).Length.ShouldBe(6);

        // Planes are realigned to 16 inside COLL, past a 12-byte hull table.
        model.CollisionPlanes[0].Normal.ShouldBeCloseTo(new Vector3(1f, 0f, 0f), 1e-6f);
        model.CollisionPlanes[0].D.ShouldBe(-1f);
        model.CollisionPlanes[5].Normal.ShouldBeCloseTo(new Vector3(0f, 0f, -1f), 1e-6f);
        model.CollisionPlanes[5].D.ShouldBe(-1f);

        model.VertexLayoutId.ShouldBe(SmodelFormat.ComputeVertexLayoutId(model.VertexAttributes));

        model.GetName(SmodelFormat.NameOffsetAbsent).ShouldBe(string.Empty);
        names.Length.ShouldBe(5);
    }

    [Fact]
    public void A_section_the_reader_has_never_heard_of_is_stepped_over_and_its_neighbours_still_parse()
    {
        // Unknown section goes between two known ones: a reader that stopped at
        // it would still pass if it came last.
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Section("WHAT", [0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4]);
        builder.Indices16(0, 1, 2, 0, 2, 3);
        builder.Submeshes((0, 6, HandBuiltSmodel.NameOffsetAbsent));

        SmodelModel model = SmodelReader.Read(builder.Build(), Source);

        model.SkippedSectionCount.ShouldBe(1);
        model.VertexCount.ShouldBe(4);
        model.IndexCount.ShouldBe(6);
        model.Submeshes[0].IndexCount.ShouldBe(6u);
        model.Submeshes[0].HasMaterial.ShouldBeFalse();
    }

    [Fact]
    public void The_reserved_animation_fourcc_is_skipped_like_any_other_unknown()
    {
        // ANIM is reserved by the format and never written.
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Section("ANIM", new byte[64]);
        builder.Indices16(0, 1, 2);
        builder.Submeshes((0, 3, HandBuiltSmodel.NameOffsetAbsent));

        SmodelModel model = SmodelReader.Read(builder.Build(), Source);

        model.SkippedSectionCount.ShouldBe(1);
        model.IndexCount.ShouldBe(3);
    }

    [Fact]
    public void Collision_hulls_come_back_as_planes_the_brush_constructor_accepts()
    {
        byte[] file = ValidModel(out _).Build();

        SmodelModel model = SmodelReader.Read(file, Source);
        Plane[] planes = model.PlanesOf(model.CollisionHulls[0]).ToArray();

        var hull = new Brush(planes);

        hull.LocalBounds.Min.ShouldBeCloseTo(new Vector3(-1f), 1e-4f);
        hull.LocalBounds.Max.ShouldBeCloseTo(new Vector3(1f), 1e-4f);
    }

    [Fact]
    public void A_thirty_two_bit_index_buffer_reports_the_same_indices_as_a_sixteen_bit_one()
    {
        var narrow = new HandBuiltSmodel { Flags = 0 };
        narrow.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        narrow.VertexBuffer(QuadVertices());
        narrow.Indices16(0, 1, 2, 0, 2, 3);
        narrow.Submeshes((0, 6, HandBuiltSmodel.NameOffsetAbsent));

        var wide = new HandBuiltSmodel { Flags = Index32Flag };
        wide.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        wide.VertexBuffer(QuadVertices());
        wide.Indices32(0, 1, 2, 0, 2, 3);
        wide.Submeshes((0, 6, HandBuiltSmodel.NameOffsetAbsent));

        SmodelModel narrowModel = SmodelReader.Read(narrow.Build(), Source);
        SmodelModel wideModel = SmodelReader.Read(wide.Build(), Source);

        narrowModel.Index32.ShouldBeFalse();
        wideModel.Index32.ShouldBeTrue();
        narrowModel.IndexCount.ShouldBe(wideModel.IndexCount);
        for (int i = 0; i < narrowModel.IndexCount; i++)
            narrowModel.IndexAt(i).ShouldBe(wideModel.IndexAt(i), $"index {i}");
    }

    [Fact]
    public void A_file_too_short_to_hold_a_header_is_refused()
    {
        SmodelFormatException refusal = Refused(new byte[16]);

        refusal.Message.ShouldContain("16 bytes");
        refusal.Message.ShouldContain("64-byte");
    }

    [Fact]
    public void A_file_that_does_not_begin_with_SMDL_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.Magic = 'S' | ('P' << 8) | ('A' << 16) | ((uint)'K' << 24);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("SPAK");
        refusal.Message.ShouldContain("SMDL");
    }

    [Fact]
    public void A_model_at_another_format_version_is_refused_naming_both_and_saying_recook()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.FormatVersion = 99;

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("99");
        refusal.Message.ShouldContain(EngineInfo.ModelFormatVersion.ToString());
        refusal.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_model_at_another_geometry_format_version_is_refused_naming_both_and_saying_recook()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.GeometryFormatVersion = 7;

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("7");
        refusal.Message.ShouldContain(EngineInfo.GeometryFormatVersion.ToString());
        refusal.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_section_table_that_would_end_past_the_file_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.SectionCountOverride = 1000;

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("1000 sections");
    }

    [Fact]
    public void A_section_reaching_past_the_end_of_the_file_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.SectionAt("XTRA", offset: 0x1000_0000, length: 16);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("XTRA");
        refusal.Message.ShouldContain("past the");
    }

    [Fact]
    public void A_section_that_does_not_start_on_the_payload_alignment_is_refused()
    {
        // Alignment is checked for unknown sections too, not only parsed ones.
        HandBuiltSmodel builder = ValidModel(out _);
        builder.SectionAt("XTRA", offset: 1, length: 0);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("XTRA");
        refusal.Message.ShouldContain("byte 1");
        refusal.Message.ShouldContain("multiple of 16");
    }

    [Fact]
    public void A_section_declared_twice_is_refused_naming_the_fourcc()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.Section("VBUF", new byte[32]);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("VBUF");
        refusal.Message.ShouldContain("more than once");
    }

    [Fact]
    public void A_model_missing_its_index_buffer_is_refused_naming_the_section()
    {
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Submeshes((0, 0, HandBuiltSmodel.NameOffsetAbsent));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("IBUF");
        refusal.Message.ShouldContain("must carry");
    }

    [Fact]
    public void A_vertex_buffer_that_is_not_a_whole_number_of_vertices_is_refused()
    {
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(1f, 2f, 3f);   // 12 bytes against a 32-byte stride
        builder.Indices16(0, 1, 2);
        builder.Submeshes((0, 3, HandBuiltSmodel.NameOffsetAbsent));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("VBUF");
        refusal.Message.ShouldContain("12 bytes");
        refusal.Message.ShouldContain("32-byte records");
    }

    [Fact]
    public void An_index_buffer_that_is_not_a_whole_number_of_indices_is_refused()
    {
        // MemoryMarshal.Cast drops a partial trailing element without complaint.
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Section("IBUF", new byte[3]);
        builder.Submeshes((0, 1, HandBuiltSmodel.NameOffsetAbsent));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("IBUF");
        refusal.Message.ShouldContain("3 bytes");
        refusal.Message.ShouldContain("2-byte records");
    }

    [Fact]
    public void A_submesh_reaching_past_the_index_buffer_is_refused_naming_the_submesh()
    {
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Indices16(0, 1, 2, 0, 2, 3);
        builder.Submeshes(
            (0, 3, HandBuiltSmodel.NameOffsetAbsent),
            (3, 9, HandBuiltSmodel.NameOffsetAbsent));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("submesh 1");
        refusal.Message.ShouldContain("indices 3 to 12");
        refusal.Message.ShouldContain("6 indices");
    }

    [Fact]
    public void A_layout_id_that_does_not_hash_the_layout_it_stamps_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.VertexLayoutIdOverride = 0xDEADBEEF;

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("0xDEADBEEF");
        refusal.Message.ShouldContain("VTXL");
    }

    [Fact]
    public void A_joint_whose_parent_does_not_precede_it_is_refused_naming_the_joint()
    {
        HandBuiltSmodel builder = ValidModelWithSkeleton(
            (HandBuiltSmodel.NameOffsetAbsent, -1, Identity3x4()),
            (HandBuiltSmodel.NameOffsetAbsent, 2, Identity3x4()),
            (HandBuiltSmodel.NameOffsetAbsent, 1, Identity3x4()));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("joint 1");
        refusal.Message.ShouldContain("parent 2");
        refusal.Message.ShouldContain("one forward pass");
    }

    [Fact]
    public void A_joint_that_is_its_own_parent_is_refused_by_the_same_rule()
    {
        HandBuiltSmodel builder = ValidModelWithSkeleton(
            (HandBuiltSmodel.NameOffsetAbsent, -1, Identity3x4()),
            (HandBuiltSmodel.NameOffsetAbsent, 1, Identity3x4()));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("joint 1");
        refusal.Message.ShouldContain("parent 1");
    }

    [Fact]
    public void A_collision_hull_with_too_few_planes_for_a_brush_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _, hulls: [(0, 3)]);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("hull 0");
        refusal.Message.ShouldContain("3 planes");
        refusal.Message.ShouldContain("at least 4");
    }

    [Fact]
    public void A_collision_hull_reaching_past_the_plane_array_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _, hulls: [(0, 6), (4, 6)]);

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("hull 1");
        refusal.Message.ShouldContain("planes 4 to 10");
        refusal.Message.ShouldContain("6 in COLL");
    }

    [Fact]
    public void A_header_flag_disagreeing_with_the_section_table_is_refused()
    {
        HandBuiltSmodel builder = ValidModel(out _);
        builder.Flags = HasCollisionFlag;   // SKEL is present and unannounced

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("HasSkeleton");
        refusal.Message.ShouldContain("SKEL");
    }

    [Fact]
    public void A_material_name_offset_outside_the_name_blob_is_refused()
    {
        var builder = new HandBuiltSmodel { Flags = 0 };
        builder.Names(out _, "Materials/crate.spectramat");
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Indices16(0, 1, 2);
        builder.Submeshes((0, 3, 9999));

        SmodelFormatException refusal = Refused(builder.Build());

        refusal.Message.ShouldContain("submesh 0");
        refusal.Message.ShouldContain("9999");
        refusal.Message.ShouldContain("NAME section");
    }

    private static SmodelFormatException Refused(byte[] file) =>
        Should.Throw<SmodelFormatException>(() => SmodelReader.Read(file, Source));

    // Three attributes, four vertices, two submeshes, two LODs, a three-joint
    // chain and one box hull.
    private static HandBuiltSmodel ValidModel(
        out uint[] names,
        (uint PlaneStart, uint PlaneCount)[]? hulls = null)
    {
        var builder = new HandBuiltSmodel { Flags = HasSkeletonFlag | HasCollisionFlag };

        builder.Names(
            out names,
            "Materials/crate.spectramat",
            "Materials/glass.spectramat",
            "root",
            "spine",
            "hand");

        builder.VertexLayout(
            strideFloats: 8,
            (0, 0, 3, 0),     // Position, float32, 3 components, byte 0
            (1, 0, 3, 12),    // Normal
            (3, 0, 2, 24));   // Uv0

        builder.VertexBuffer(QuadVertices());
        builder.Indices16(0, 1, 2, 0, 2, 3);
        builder.Submeshes((0, 3, names[0]), (3, 3, names[1]));
        builder.Lods((0.5f, 0, 1), (0f, 1, 1));
        builder.Skeleton(
            (names[2], -1, Identity3x4()),
            (names[3], 0, Translation3x4(7f, 8f, 9f)),
            (names[4], 1, Identity3x4()));
        builder.Collision(hulls ?? [(0, 6)], UnitBoxPlanes());

        return builder;
    }

    private static HandBuiltSmodel ValidModelWithSkeleton(
        params (uint NameOffset, int Parent, float[] InverseBind)[] joints)
    {
        var builder = new HandBuiltSmodel { Flags = HasSkeletonFlag };
        builder.VertexLayout(8, (0, 0, 3, 0), (1, 0, 3, 12), (3, 0, 2, 24));
        builder.VertexBuffer(QuadVertices());
        builder.Indices16(0, 1, 2);
        builder.Submeshes((0, 3, HandBuiltSmodel.NameOffsetAbsent));
        builder.Skeleton(joints);
        return builder;
    }

    // Four vertices of eight floats: position, normal, uv.
    private static float[] QuadVertices() =>
    [
        -1f, 0f, -1f, 0f, 1f, 0f, 0f, 0f,
        1f, 0f, -1f, 0f, 1f, 0f, 1f, 0f,
        1f, 0f, 1f, 0f, 1f, 0f, 1f, 1f,
        -1f, 0f, 1f, 0f, 1f, 0f, 0f, 1f,
    ];

    // Four rows of three. The omitted fourth column is (0, 0, 0, 1).
    private static float[] Identity3x4() =>
    [
        1f, 0f, 0f,
        0f, 1f, 0f,
        0f, 0f, 1f,
        0f, 0f, 0f,
    ];

    private static float[] Translation3x4(float x, float y, float z) =>
    [
        1f, 0f, 0f,
        0f, 1f, 0f,
        0f, 0f, 1f,
        x, y, z,
    ];

    // Brush.CreateBox's sign convention: normals point out, D is minus the half extent.
    private static (float Nx, float Ny, float Nz, float D)[] UnitBoxPlanes() =>
    [
        (1f, 0f, 0f, -1f),
        (-1f, 0f, 0f, -1f),
        (0f, 1f, 0f, -1f),
        (0f, -1f, 0f, -1f),
        (0f, 0f, 1f, -1f),
        (0f, 0f, -1f, -1f),
    ];
}
