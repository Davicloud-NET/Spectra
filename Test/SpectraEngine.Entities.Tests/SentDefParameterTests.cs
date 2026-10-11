using SpectraEngine.Core.Entities;
using System;
using System.Buffers.Binary;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// What an input takes and an output sends, through the file an editor reads
/// its classes from.
/// </summary>
public sealed class SentDefParameterTests
{
    private const byte FloatByte = SentDef.ParameterFirstValue + (byte)KeyvalueType.Float;

    // One class as the build before the types wrote it: three inputs and two
    // outputs by name, and the record ends at its class flags.
    private static byte[] ImageBeforeTheTypes =>
    [
        (byte)'S', (byte)'E', (byte)'N', (byte)'T', 1, 0, 20, 0, 1, 0, 0, 0, 72, 0, 0, 0, 70, 0, 0, 0,
        52, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 13, 0, 0, 0, 2, 0, 0, 0, 3, 0, 2, 0,
        20, 0, 0, 0, 25, 0, 0, 0, 35, 0, 0, 0,
        42, 0, 0, 0, 52, 0, 0, 0,
        61, 0, 0, 0, 0, 0, 0, 0,
        .. GateStrings,
    ];

    // The same class as this build writes it once it says its types: the
    // record is eight bytes longer and the string table starts eight later.
    private static byte[] ImageWithTheTypes =>
    [
        (byte)'S', (byte)'E', (byte)'N', (byte)'T', 1, 0, 20, 0, 1, 0, 0, 0, 80, 0, 0, 0, 70, 0, 0, 0,
        60, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 13, 0, 0, 0, 2, 0, 0, 0, 3, 0, 2, 0,
        20, 0, 0, 0, 25, 0, 0, 0, 35, 0, 0, 0,
        42, 0, 0, 0, 52, 0, 0, 0,
        61, 0, 0, 0, 0, 0, 0, 0,
        0x84, 4, 1, 4, 1, 0, 0, 0,
        .. GateStrings,
    ];

    private static byte[] GateStrings =>
    [
        0, 0,
        9, 0, .. "math_gate"u8,
        5, 0, .. "Logic"u8,
        3, 0, .. "Add"u8,
        8, 0, .. "SetValue"u8,
        5, 0, .. "Reset"u8,
        8, 0, .. "OutValue"u8,
        7, 0, .. "OnReset"u8,
        7, 0, .. "counter"u8,
    ];

    private static readonly EntityPort[] TypedInputs =
    [
        new("Add", ParameterType.Optional(KeyvalueType.Float)),
        new("SetValue", ParameterType.Of(KeyvalueType.Float)),
        new("Reset", ParameterType.None),
    ];

    private static readonly EntityPort[] TypedOutputs =
    [
        new("OutValue", ParameterType.Of(KeyvalueType.Float)),
        new("OnReset", ParameterType.None),
    ];

    public static TheoryData<string> EveryForm => new(
    [
        "not said", "none",
        "bool", "int", "float", "string", "vec2", "vec3", "vec4", "color", "angles", "targetname", "noderef",
        "bool?", "int?", "float?", "string?", "vec2?", "vec3?", "vec4?", "color?", "angles?", "targetname?",
        "noderef?",
    ]);

    private static EntitySchema Gate(bool saysTypes) => new(
        "math_gate",
        group: "Logic",
        placement: EntityPlacement.Abstract,
        inputs: saysTypes ? TypedInputs : EntityPort.Named("Add", "SetValue", "Reset"),
        outputs: saysTypes ? TypedOutputs : EntityPort.Named("OutValue", "OnReset"),
        icon: "counter");

    [Fact]
    public void A_small_class_that_says_its_types_is_written_as_these_bytes()
    {
        SentDef.Write([Gate(saysTypes: true)]).ShouldBe(ImageWithTheTypes);
    }

    [Fact]
    public void Those_bytes_read_back_as_the_class_that_wrote_them()
    {
        EntitySchema read = SentDef.Read(ImageWithTheTypes)[0];

        read.Inputs.ShouldBe(TypedInputs);
        read.Outputs.ShouldBe(TypedOutputs);
        read.Icon.ShouldBe("counter");
    }

    [Fact]
    public void An_image_from_the_build_before_the_types_loads_with_no_type_said()
    {
        EntitySchema read = SentDef.Read(ImageBeforeTheTypes)[0];

        read.ClassName.ShouldBe("math_gate");
        read.InputNames.ShouldBe(["Add", "SetValue", "Reset"]);
        read.OutputNames.ShouldBe(["OutValue", "OnReset"]);
        read.Icon.ShouldBe("counter");
        read.Inputs.Concat(read.Outputs).ShouldAllBe(port => port.Parameter == ParameterType.NotSaid);
    }

    [Fact]
    public void An_input_that_takes_nothing_reads_differently_from_one_in_a_file_too_old_to_say()
    {
        SentDef.Read(ImageWithTheTypes)[0].TryGetInput("Reset", out EntityPort said).ShouldBeTrue();
        SentDef.Read(ImageBeforeTheTypes)[0].TryGetInput("Reset", out EntityPort unsaid).ShouldBeTrue();

        said.Parameter.IsSaid.ShouldBeTrue();
        said.Parameter.IsNone.ShouldBeTrue();
        unsaid.Parameter.IsSaid.ShouldBeFalse();
        unsaid.Parameter.IsNone.ShouldBeFalse();
    }

    [Fact]
    public void This_builds_image_without_its_types_is_the_image_the_older_build_wrote()
    {
        // The other suites imitate an older file this way, so it has to be one.
        SentDefImages.WithoutParameterTypes(SentDef.Write([Gate(saysTypes: true)])).ShouldBe(ImageBeforeTheTypes);
    }

    [Fact]
    public void Everything_an_older_reader_reads_is_where_it_was()
    {
        // It reads a record up to its class flags, skips the rest by the
        // record's size, and finds the strings by the header's offset.
        const int olderRecord = 52;
        int afterSize = SentDef.HeaderSize + sizeof(uint);

        ImageWithTheTypes.AsSpan(afterSize, olderRecord - sizeof(uint)).ToArray()
            .ShouldBe(ImageBeforeTheTypes.AsSpan(afterSize, olderRecord - sizeof(uint)).ToArray());
        ImageWithTheTypes.AsSpan(80).ToArray().ShouldBe(ImageBeforeTheTypes.AsSpan(72).ToArray());
    }

    [Fact]
    public void A_class_built_from_names_alone_writes_zeros_and_reads_back_with_no_type_said()
    {
        byte[] image = SentDef.Write([Gate(saysTypes: false)]);

        image.Length.ShouldBe(ImageWithTheTypes.Length);
        image.AsSpan(SentDefImages.FirstParameters(image), 8).ToArray().ShouldBe(new byte[8]);
        SentDef.Read(image)[0].Inputs.ShouldBe(EntityPort.Named("Add", "SetValue", "Reset"));
    }

    [Fact]
    public void The_types_are_a_byte_for_each_input_and_then_each_output_after_the_flags()
    {
        byte[] image = SentDef.Write([Gate(saysTypes: true)]);
        int types = SentDefImages.FirstParameters(image);

        SentDef.TrailerParametersOffset.ShouldBe(SentDef.TrailerFlagsOffset + sizeof(uint));
        image[types].ShouldBe((byte)(FloatByte | SentDef.ParameterOptional));
        image[types + 1].ShouldBe(FloatByte);
        image[types + 2].ShouldBe(SentDef.ParameterNone);
        image[types + 3].ShouldBe(FloatByte);
        image[types + 4].ShouldBe(SentDef.ParameterNone);

        // Three bytes of padding, then the string table.
        (types + SentDef.ParameterBytes(3, 2)).ShouldBe(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderStringTableOffsetOffset)));
    }

    [Fact]
    public void The_bytes_a_type_is_written_as_are_fixed()
    {
        // In files already written. A type's byte is two more than its own.
        SentDef.ParameterNotSaid.ShouldBe((byte)0);
        SentDef.ParameterNone.ShouldBe((byte)1);
        SentDef.ParameterFirstValue.ShouldBe((byte)2);
        SentDef.ParameterOptional.ShouldBe((byte)0x80);
        FloatByte.ShouldBe((byte)4);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 4)]
    [InlineData(2, 2, 4)]
    [InlineData(3, 2, 8)]
    [InlineData(1, 17, 20)]
    public void The_types_are_padded_so_a_record_stays_a_multiple_of_four_bytes(int inputs, int outputs, int bytes)
    {
        SentDef.ParameterBytes(inputs, outputs).ShouldBe(bytes);

        var names = new string[inputs + outputs];
        for (int i = 0; i < names.Length; i++)
            names[i] = $"Port{i}";

        byte[] image = SentDef.Write(
        [
            new EntitySchema(
                "test_ports",
                inputs: EntityPort.Named(names[..inputs]),
                outputs: EntityPort.Named(names[inputs..])),
        ]);

        (BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderSize)) % 4).ShouldBe(0u);
    }

    [Theory]
    [MemberData(nameof(EveryForm))]
    public void Every_type_a_port_can_say_survives_the_round_trip(string form)
    {
        ParameterType parameter = Form(form);
        var schema = new EntitySchema(
            "test_port",
            inputs: [new EntityPort("In", parameter), new EntityPort("Plain", ParameterType.None)],
            outputs: [new EntityPort("Out", parameter)]);

        EntitySchema read = SentDef.Read(SentDef.Write([schema]))[0];

        read.Inputs.ShouldBe(schema.Inputs);
        read.Outputs.ShouldBe(schema.Outputs);
        read.Inputs[0].Parameter.ToString().ShouldBe(form);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void A_record_that_ends_partway_through_its_types_reads_with_none_said(int bytesCut)
    {
        // Five types and three bytes of padding. Cutting into the five leaves
        // a record no build wrote, and a guess at half of it helps nobody.
        byte[] image = CutFromFirstRecord(SentDef.Write([Gate(saysTypes: true)]), 3 + bytesCut);

        EntitySchema read = SentDef.Read(image)[0];

        read.InputNames.ShouldBe(["Add", "SetValue", "Reset"]);
        read.Icon.ShouldBe("counter");
        read.Inputs.Concat(read.Outputs).ShouldAllBe(port => !port.Parameter.IsSaid);
    }

    [Fact]
    public void A_record_that_lost_only_its_padding_still_says_its_types()
    {
        byte[] image = CutFromFirstRecord(SentDef.Write([Gate(saysTypes: true)]), 3);

        SentDef.Read(image)[0].Inputs.ShouldBe(TypedInputs);
    }

    [Theory]
    [InlineData(SentDef.ParameterFirstValue + (byte)KeyvalueType.AssetSound)]
    [InlineData(SentDef.ParameterFirstValue + (byte)KeyvalueType.Choices)]
    [InlineData(SentDef.ParameterFirstValue + (byte)KeyvalueType.Flags)]
    [InlineData(SentDef.ParameterFirstValue + (byte)KeyvalueType.Distance)]
    [InlineData(SentDef.ParameterFirstValue + 18)]
    [InlineData(0x7F)]
    [InlineData(0xFF)]
    [InlineData(0x80)]
    public void A_type_byte_this_build_cannot_use_reads_as_not_said_and_fails_nothing(int stored)
    {
        // A newer build may name a type this one does not. Nothing is
        // enforced by the type, so the class still loads.
        byte[] image = SentDef.Write([Gate(saysTypes: true)]);
        image[SentDefImages.FirstParameters(image) + 1] = (byte)stored;

        EntitySchema read = SentDef.Read(image)[0];

        read.Inputs[1].ShouldBe(new EntityPort("SetValue", ParameterType.NotSaid));
        read.Inputs[0].ShouldBe(TypedInputs[0]);
        read.Inputs[2].ShouldBe(TypedInputs[2]);
        read.Outputs.ShouldBe(TypedOutputs);
    }

    [Fact]
    public void The_bit_for_leaving_a_value_out_means_nothing_on_no_value()
    {
        byte[] image = SentDef.Write([Gate(saysTypes: true)]);
        image[SentDefImages.FirstParameters(image) + 2] = SentDef.ParameterNone | SentDef.ParameterOptional;

        SentDef.Read(image)[0].Inputs[2].Parameter.ShouldBe(ParameterType.None);
    }

    [Fact]
    public void The_types_did_not_take_a_new_version()
    {
        // The record grew at its end, which a reader skips by the record's own size.
        SentDef.Version.ShouldBe((ushort)1);
    }

    [Fact]
    public void Every_built_in_class_carries_its_types_through_the_file()
    {
        EntitySchemaCatalog catalog = EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas));

        foreach (EntitySchema declared in BuiltinEntities.Schemas)
        {
            catalog.TryGetSchema(declared.ClassName, out EntitySchema? read).ShouldBeTrue();
            read.ShouldNotBeNull().Inputs.ShouldBe(declared.Inputs, declared.ClassName);
            read.Outputs.ShouldBe(declared.Outputs, declared.ClassName);
        }
    }

    [Fact]
    public void The_built_in_classes_read_from_an_older_file_keep_their_names_and_say_no_type()
    {
        byte[] older = SentDefImages.WithoutParameterTypes(SentDef.Write(BuiltinEntities.Schemas));

        EntitySchemaCatalog catalog = EntitySchemaCatalog.LoadFromSentDef(older);

        catalog.Count.ShouldBe(BuiltinEntities.ClassCount);
        foreach (EntitySchema declared in BuiltinEntities.Schemas)
        {
            catalog.TryGetSchema(declared.ClassName, out EntitySchema? read).ShouldBeTrue();
            read.ShouldNotBeNull().InputNames.ShouldBe(declared.InputNames, declared.ClassName);
            read.OutputNames.ShouldBe(declared.OutputNames, declared.ClassName);
            read.Icon.ShouldBe(declared.Icon);
            read.HasFacing.ShouldBe(declared.HasFacing);
            read.Inputs.Concat(read.Outputs).ShouldAllBe(port => !port.Parameter.IsSaid, declared.ClassName);
        }
    }

    // "float?" is a float that may be left out.
    private static ParameterType Form(string form)
    {
        if (form == "not said")
            return ParameterType.NotSaid;
        if (form == "none")
            return ParameterType.None;

        bool optional = form.EndsWith('?');
        KeyvalueType type = Enum.Parse<KeyvalueType>(form.TrimEnd('?'), ignoreCase: true);
        return optional ? ParameterType.Optional(type) : ParameterType.Of(type);
    }

    // Takes bytes off the end of the first type record, as a file cut short
    // would have them missing.
    private static byte[] CutFromFirstRecord(byte[] image, int bytes)
    {
        int recordSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderSize));
        int table = (int)BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(SentDef.HeaderStringTableOffsetOffset));
        int end = SentDef.HeaderSize + recordSize;

        byte[] cut = [.. image.AsSpan(0, end - bytes), .. image.AsSpan(end)];
        BinaryPrimitives.WriteUInt32LittleEndian(cut.AsSpan(SentDef.HeaderSize), (uint)(recordSize - bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(
            cut.AsSpan(SentDef.HeaderStringTableOffsetOffset), (uint)(table - bytes));
        return cut;
    }
}
