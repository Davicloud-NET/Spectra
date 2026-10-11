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
        inputs: EntityPort.Named("Open", "Close"),
        outputs: EntityPort.Named("OnOpened"),
        icon: icon);

    [Fact]
    public void An_icon_name_survives_the_round_trip()
    {
        EntitySchema copy = SentDef.Read(SentDef.Write([Door("door")]))[0];

        copy.Icon.ShouldBe("door");
        copy.InputNames.ShouldBe(["Open", "Close"]);
        copy.OutputNames.ShouldBe(["OnOpened"]);
    }

    [Fact]
    public void A_class_that_names_no_icon_reads_back_with_none()
    {
        SentDef.Read(SentDef.Write([Door("")]))[0].Icon.ShouldBe("");
        new EntitySchema("logic_auto").Icon.ShouldBe("");
    }

    [Fact]
    public void The_icon_is_a_string_reference_at_the_start_of_the_trailer()
    {
        byte[] image = SentDef.Write([Door("door")]);
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderSize + SentDef.TypeRecordSizeOffset));

        // One keyvalue, two inputs and an output, then the trailer and their types.
        recordSize.ShouldBe(
            SentDef.TypeRecordFixedSize + SentDef.KeyvalueRecordSize + (3 * SentDef.StringRefSize) +
            SentDef.TrailerFixedSize + SentDef.ParameterBytes(2, 1));

        uint reference = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(IconReference(image)));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            image.AsSpan(SentDef.HeaderStringTableOffsetOffset));
        int length = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(table + (int)reference));

        System.Text.Encoding.UTF8.GetString(image.AsSpan(table + (int)reference + 2, length)).ShouldBe("door");
    }

    [Fact]
    public void A_class_with_no_icon_writes_the_empty_string_reference()
    {
        byte[] image = SentDef.Write([Door("")]);

        BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(IconReference(image))).ShouldBe(0u);
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
        // What a build from before the icon wrote: the same record with no trailer.
        byte[] older = SentDefImages.WithShorterRecords(
            SentDef.Write([Door("door"), new EntitySchema("zz_last", icon: "relay")]),
            SentDef.TrailerFixedSize);

        EntitySchema[] read = SentDef.Read(older);

        read.Length.ShouldBe(2);
        read[0].ClassName.ShouldBe("func_door");
        read[0].Icon.ShouldBe("");
        read[0].Keyvalues.Count.ShouldBe(1);
        read[0].InputNames.ShouldBe(["Open", "Close"]);
        read[0].OutputNames.ShouldBe(["OnOpened"]);
        read[1].ClassName.ShouldBe("zz_last");
        read[1].Icon.ShouldBe("");
    }

    [Fact]
    public void An_icon_reference_outside_the_string_table_is_refused()
    {
        byte[] image = SentDef.Write([Door("door")]);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(IconReference(image)), 100_000u);

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

    // Where the first record's icon reference is.
    private static int IconReference(byte[] image) =>
        SentDefImages.FirstTrailer(image) + SentDef.TrailerIconOffset;
}
