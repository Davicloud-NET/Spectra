using SpectraEngine.Core.Entities;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// Whether a class's facing means something, in the built-in classes and
/// through the file an editor reads its classes from.
/// </summary>
public sealed class SentDefFacingTests
{
    // A Brush, a Point and an Abstract class as the build before the flags
    // wrote them: each record ends at its icon reference.
    private static byte[] ImageBeforeTheFlags =>
    [
        (byte)'S', (byte)'E', (byte)'N', (byte)'T', 1, 0, 20, 0, 3, 0, 0, 0, 104, 0, 0, 0, 36, 0, 0, 0,
        28, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        28, 0, 0, 0, 13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        28, 0, 0, 0, 24, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0,
        9, 0, .. "func_wall"u8,
        9, 0, .. "info_spot"u8,
        10, 0, .. "logic_gate"u8,
    ];

    private static EntitySchema Start(bool hasFacing) => new(
        "info_start",
        group: "Player",
        inputs: EntityPort.Named("Enable"),
        outputs: EntityPort.Named("OnUsed"),
        icon: "player-start",
        hasFacing: hasFacing);

    [Fact]
    public void A_facing_survives_the_round_trip()
    {
        EntitySchema[] read = SentDef.Read(SentDef.Write([Start(true), new EntitySchema("point_hum", icon: "sound")]));

        read[0].ClassName.ShouldBe("info_start");
        read[0].HasFacing.ShouldBeTrue();
        read[0].Icon.ShouldBe("player-start");
        read[1].HasFacing.ShouldBeFalse();
        read[1].Icon.ShouldBe("sound");
    }

    [Fact]
    public void A_class_has_no_facing_unless_it_says_so()
    {
        new EntitySchema("info_spot").HasFacing.ShouldBeFalse();
        SentDef.Read(SentDef.Write([Start(false)]))[0].HasFacing.ShouldBeFalse();
    }

    [Fact]
    public void The_facing_is_the_lowest_bit_of_the_flags_that_follow_the_icon()
    {
        byte[] plain = SentDef.Write([Start(false)]);
        byte[] facing = SentDef.Write([Start(true)]);
        int flags = SentDefImages.FirstTrailer(facing) + SentDef.TrailerFlagsOffset;

        SentDef.TrailerHasFacing.ShouldBe(1u);
        BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(flags)).ShouldBe(0u);
        BinaryPrimitives.ReadUInt32LittleEndian(facing.AsSpan(flags)).ShouldBe(SentDef.TrailerHasFacing);

        // After the flags come the types of the one input and the one output,
        // and then the string table.
        (flags + sizeof(uint) + SentDef.ParameterBytes(1, 1)).ShouldBe(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(facing.AsSpan(SentDef.HeaderStringTableOffsetOffset)));

        facing.Length.ShouldBe(plain.Length);
        for (int i = 0; i < plain.Length; i++)
        {
            if (i != flags)
                facing[i].ShouldBe(plain[i], $"byte {i}");
        }
    }

    [Fact]
    public void The_facing_did_not_take_a_new_version()
    {
        // The record grew at its end, which a reader skips by the record's own size.
        SentDef.Version.ShouldBe((ushort)1);
    }

    [Fact]
    public void A_record_that_ends_at_its_icon_reads_with_the_icon_and_no_facing()
    {
        // What the build before the flags wrote: the same record, ending at its icon.
        byte[] older = SentDefImages.WithShorterRecords(
            SentDef.Write([Start(true), new EntitySchema("zz_last", hasFacing: true)]), sizeof(uint));

        EntitySchema[] read = SentDef.Read(older);

        read.Length.ShouldBe(2);
        read[0].ClassName.ShouldBe("info_start");
        read[0].HasFacing.ShouldBeFalse();
        read[0].Icon.ShouldBe("player-start");
        read[0].InputNames.ShouldBe(["Enable"]);
        read[0].OutputNames.ShouldBe(["OnUsed"]);
        read[1].ClassName.ShouldBe("zz_last");
        read[1].HasFacing.ShouldBeFalse();
    }

    [Fact]
    public void A_record_with_no_trailer_reads_with_no_facing()
    {
        byte[] oldest = SentDefImages.WithShorterRecords(
            SentDef.Write([Start(true)]), SentDef.TypeRecordTrailerSize);

        EntitySchema read = SentDef.Read(oldest)[0];

        read.HasFacing.ShouldBeFalse();
        read.Icon.ShouldBe("");
        read.OutputNames.ShouldBe(["OnUsed"]);
    }

    [Fact]
    public void An_image_from_the_build_before_the_flags_reads_every_class_with_no_facing()
    {
        // An arrow fewer on an old file, never one that points nowhere.
        EntitySchema[] read = SentDef.Read(ImageBeforeTheFlags);

        read.Select(schema => schema.ClassName).ShouldBe(["func_wall", "info_spot", "logic_gate"]);
        read.Select(schema => schema.HasFacing).ShouldBe([false, false, false]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_flag_bit_this_build_does_not_know_changes_nothing(bool hasFacing)
    {
        byte[] image = SentDef.Write([Start(hasFacing)]);
        Span<byte> flags = image.AsSpan(SentDefImages.FirstTrailer(image) + SentDef.TrailerFlagsOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(flags, BinaryPrimitives.ReadUInt32LittleEndian(flags) | (1u << 5));

        SentDef.Read(image)[0].HasFacing.ShouldBe(hasFacing);
    }

    [Fact]
    public void The_built_in_classes_with_a_facing_are_the_two_a_builder_turns_to_aim()
    {
        // The player starts, or arrives, looking along the node's +Z.
        BuiltinEntities.Schemas
            .Where(schema => schema.HasFacing)
            .Select(schema => schema.ClassName)
            .ShouldBe(["info_player_start", "info_teleport_destination"]);
    }

    [Fact]
    public void A_sound_has_a_place_and_no_facing()
    {
        PointSound.SpectraSchema.Placement.ShouldBe(EntityPlacement.Point);
        PointSound.SpectraSchema.HasFacing.ShouldBeFalse();
    }

    [Fact]
    public void No_logic_class_has_a_facing()
    {
        EntitySchema[] logic = BuiltinEntities.Schemas
            .Where(schema => schema.Placement == EntityPlacement.Abstract)
            .ToArray();

        logic.Length.ShouldBe(7);
        foreach (EntitySchema schema in logic)
            schema.HasFacing.ShouldBeFalse(schema.ClassName);
    }

    [Fact]
    public void Every_built_in_class_carries_its_facing_through_the_file()
    {
        EntitySchemaCatalog catalog = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas));

        foreach (EntitySchema declared in BuiltinEntities.Schemas)
        {
            catalog.TryGetSchema(declared.ClassName, out EntitySchema? read).ShouldBeTrue();
            read.ShouldNotBeNull().HasFacing.ShouldBe(declared.HasFacing, declared.ClassName);
        }
    }
}
