using SpectraEngine.Core.Entities;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The icon a class names, through the file an editor reads its classes from.
/// </summary>
public sealed class SentDefIconTests
{
    private static EntitySchema Door(string icon) => new(
        "func_door",
        displayName: "Door",
        group: "Movers",
        placement: EntityPlacement.Brush,
        keyvalues:
        [
            new KeyvalueDescriptor(
                "speed", "Speed", "", "100", KeyvalueType.Float, KeyvalueWidget.Auto,
                float.NaN, float.NaN, 0u, KeyvalueDescriptor.NoChoices),
        ],
        inputs: ["Open", "Close"],
        outputs: ["OnOpened"],
        icon: icon);

    [Fact]
    public void An_icon_name_survives_the_round_trip()
    {
        EntitySchema copy = SentDef.Read(SentDef.Write([Door("door")]))[0];

        copy.Icon.ShouldBe("door");
        copy.Inputs.ShouldBe(["Open", "Close"]);
        copy.Outputs.ShouldBe(["OnOpened"]);
    }

    [Fact]
    public void A_class_that_names_no_icon_reads_back_with_none()
    {
        SentDef.Read(SentDef.Write([Door("")]))[0].Icon.ShouldBe("");
        new EntitySchema("logic_auto").Icon.ShouldBe("");
    }

    [Fact]
    public void The_icon_is_a_string_reference_in_the_last_four_bytes_of_the_record()
    {
        byte[] image = SentDef.Write([Door("door")]);
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + SentDef.TypeRecordSizeOffset));

        // One keyvalue, two inputs and an output, then the icon.
        recordSize.ShouldBe(
            SentDef.TypeRecordFixedSize + SentDef.KeyvalueRecordSize + (3 * SentDef.StringRefSize) +
            SentDef.TypeRecordTrailerSize);

        uint reference = BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + recordSize - SentDef.TypeRecordTrailerSize));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderStringTableOffsetOffset));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(table + (int)reference));

        System.Text.Encoding.UTF8.GetString(image.AsSpan(table + (int)reference + 2, length)).ShouldBe("door");
    }

    [Fact]
    public void A_class_with_no_icon_writes_the_empty_string_reference()
    {
        byte[] image = SentDef.Write([Door("")]);
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + SentDef.TypeRecordSizeOffset));

        BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + recordSize - SentDef.TypeRecordTrailerSize)).ShouldBe(0u);
    }

    [Fact]
    public void The_icon_did_not_take_a_new_version()
    {
        // The record grew at its end, which a reader skips by the record's own size.
        SentDef.Version.ShouldBe((ushort)1);
    }

    [Fact]
    public void A_record_that_ends_at_its_last_output_reads_with_no_icon()
    {
        // What a build from before the icon wrote: the same record, four bytes shorter.
        byte[] older = WithoutTrailer(SentDef.Write([Door("door"), new EntitySchema("zz_last", icon: "relay")]));

        EntitySchema[] read = SentDef.Read(older);

        read.Length.ShouldBe(2);
        read[0].ClassName.ShouldBe("func_door");
        read[0].Icon.ShouldBe("");
        read[0].Keyvalues.Count.ShouldBe(1);
        read[0].Inputs.ShouldBe(["Open", "Close"]);
        read[0].Outputs.ShouldBe(["OnOpened"]);
        read[1].ClassName.ShouldBe("zz_last");
        read[1].Icon.ShouldBe("");
    }

    [Fact]
    public void An_icon_reference_outside_the_string_table_is_refused()
    {
        byte[] image = SentDef.Write([Door("door")]);
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + SentDef.TypeRecordSizeOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + recordSize - SentDef.TypeRecordTrailerSize), 100_000u);

        Should.Throw<SentDefFormatException>(() => SentDef.Read(image))
            .Message.ShouldContain("outside the");
    }

    [Fact]
    public void Every_built_in_class_carries_an_icon_through_the_file()
    {
        EntitySchemaCatalog catalog = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas));

        foreach (EntitySchema declared in BuiltinEntities.Schemas)
        {
            declared.Icon.ShouldNotBeEmpty(declared.ClassName);
            declared.Icon.ShouldBe(declared.Icon.ToLowerInvariant(), declared.ClassName);

            catalog.TryGetSchema(declared.ClassName, out EntitySchema? read).ShouldBeTrue();
            read.ShouldNotBeNull().Icon.ShouldBe(declared.Icon, declared.ClassName);
        }
    }

    [Theory]
    [InlineData("func_door", "door")]
    [InlineData("func_movelinear", "lift")]
    [InlineData("func_button", "button")]
    [InlineData("trigger_once", "trigger-once")]
    [InlineData("trigger_multiple", "trigger")]
    [InlineData("trigger_teleport", "teleport")]
    [InlineData("info_teleport_destination", "destination")]
    [InlineData("info_player_start", "player-start")]
    [InlineData("point_sound", "sound")]
    [InlineData("logic_auto", "auto")]
    [InlineData("logic_relay", "relay")]
    [InlineData("logic_timer", "timer")]
    [InlineData("math_counter", "counter")]
    [InlineData("logic_branch", "branch")]
    [InlineData("logic_case", "case")]
    [InlineData("logic_compare", "compare")]
    public void A_built_in_class_names_this_icon(string className, string icon)
    {
        BuiltinEntities.Schemas.Single(schema => schema.ClassName == className).Icon.ShouldBe(icon);
    }

    // Takes the icon reference off the end of every type record, as a build
    // from before it laid the file out.
    private static byte[] WithoutTrailer(byte[] image)
    {
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderTypeCountOffset));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderStringTableOffsetOffset));

        var older = new List<byte>(image.AsSpan(0, SentDef.HeaderSize).ToArray());
        int at = SentDef.HeaderSize;
        for (int i = 0; i < count; i++)
        {
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at));
            int kept = size - SentDef.TypeRecordTrailerSize;

            Span<byte> record = image.AsSpan(at, kept).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)kept);
            older.AddRange(record);
            at += size;
        }

        older.AddRange(image.AsSpan(table));

        byte[] bytes = [.. older];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(SentDef.HeaderStringTableOffsetOffset),
            (uint)(table - (count * SentDef.TypeRecordTrailerSize)));
        return bytes;
    }
}
