using SpectraEngine.Core.Entities;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Entity schema rules: NaN bounds and reserved flag bits.</summary>
public sealed class EntitySchemaTests
{
    private static KeyvalueDescriptor Speed(float min = float.NaN, float max = float.NaN, uint flags = 0) =>
        new("speed", "Speed", "How fast the door moves.", "100", KeyvalueType.Float,
            KeyvalueWidget.Auto, min, max, flags, KeyvalueDescriptor.NoChoices);

    [Fact]
    public void An_unbounded_descriptor_reports_no_bounds()
    {
        // NaN is unequal to itself, so `Min == float.NaN` always says bounded.
        KeyvalueDescriptor unbounded = Speed();

        unbounded.HasMin.ShouldBeFalse();
        unbounded.HasMax.ShouldBeFalse();
#pragma warning disable CS1718 // Comparison made to same variable: that is the point.
        (unbounded.Min == unbounded.Min).ShouldBeFalse();
#pragma warning restore CS1718

        KeyvalueDescriptor bounded = Speed(min: 0f, max: 600f);
        bounded.HasMin.ShouldBeTrue();
        bounded.HasMax.ShouldBeTrue();
    }

    [Fact]
    public void A_zero_bound_is_a_real_bound()
    {
        // Zero must be a real bound, not a sentinel.
        Speed(min: 0f).HasMin.ShouldBeTrue();
    }

    [Fact]
    public void The_defined_flag_bits_are_kept_and_the_reserved_ones_are_dropped()
    {
        // Bits 3 to 7 are reserved; a newer tool's bits are dropped on read.
        uint written = KeyvalueFlags.ReadOnly | KeyvalueFlags.RequiresRestart | (1u << 5);

        KeyvalueFlags.Mask(written).ShouldBe(KeyvalueFlags.ReadOnly | KeyvalueFlags.RequiresRestart);

        KeyvalueDescriptor descriptor = Speed(flags: KeyvalueFlags.Mask(written));
        descriptor.IsReadOnly.ShouldBeTrue();
        descriptor.RequiresRestart.ShouldBeTrue();
        descriptor.IsHiddenInEditor.ShouldBeFalse();
    }

    [Fact]
    public void The_widget_vocabulary_is_closed()
    {
        KeyvalueWidget.IsDefined(KeyvalueWidget.Auto).ShouldBeTrue();
        KeyvalueWidget.IsDefined(KeyvalueWidget.Flags).ShouldBeTrue();
        KeyvalueWidget.IsDefined(6).ShouldBeFalse();
    }

    [Fact]
    public void A_schema_carries_its_keyvalues_in_declaration_order()
    {
        // Declaration order is panel layout order and exported record order.
        var schema = new EntitySchema(
            "func_door",
            displayName: "Door",
            group: "Brush Entities",
            placement: EntityPlacement.Brush,
            origin: EntityOrigin.Luau,
            keyvalues: [Speed(), Speed(min: 0f)],
            inputs: EntityPort.Named("Open", "Close"),
            outputs: EntityPort.Named("OnFullyOpen"));

        schema.ClassName.ShouldBe("func_door");
        schema.Placement.ShouldBe(EntityPlacement.Brush);
        schema.Origin.ShouldBe(EntityOrigin.Luau);
        schema.Keyvalues.Count.ShouldBe(2);
        schema.Keyvalues[1].HasMin.ShouldBeTrue();
        schema.Inputs[0].Name.ShouldBe("Open");
        schema.InputNames.ShouldBe(["Open", "Close"]);
        schema.Outputs.Count.ShouldBe(1);
        schema.OutputNames.ShouldBe(["OnFullyOpen"]);
    }

    [Fact]
    public void An_input_and_an_output_are_listed_with_what_they_carry()
    {
        var schema = new EntitySchema(
            "math_counter",
            inputs:
            [
                new EntityPort("Add", ParameterType.Optional(KeyvalueType.Float)),
                new EntityPort("GetValue", ParameterType.None),
            ],
            outputs: [new EntityPort("OutValue", ParameterType.Of(KeyvalueType.Float))]);

        schema.Inputs[0].Parameter.ShouldBe(ParameterType.Optional(KeyvalueType.Float));
        schema.Inputs[1].Parameter.IsNone.ShouldBeTrue();
        schema.Outputs[0].Parameter.ValueType.ShouldBe(KeyvalueType.Float);
        schema.InputNames.ShouldBe(["Add", "GetValue"]);
        schema.OutputNames.ShouldBe(["OutValue"]);
    }

    [Fact]
    public void The_names_are_one_list_for_the_life_of_the_schema()
    {
        // A dropdown bound to the list loses its selection when handed a new one.
        var schema = new EntitySchema("func_door", inputs: EntityPort.Named("Open"), outputs: EntityPort.Named("OnOpen"));

        schema.InputNames.ShouldBeSameAs(schema.InputNames);
        schema.OutputNames.ShouldBeSameAs(schema.OutputNames);
    }

    [Fact]
    public void An_input_or_an_output_is_found_by_its_exact_name()
    {
        var schema = new EntitySchema(
            "func_door",
            inputs: [new EntityPort("Open", ParameterType.None)],
            outputs: [new EntityPort("OnOpen", ParameterType.None)]);

        schema.TryGetInput("Open", out EntityPort open).ShouldBeTrue();
        open.ShouldBe(new EntityPort("Open", ParameterType.None));
        schema.TryGetOutput("OnOpen", out _).ShouldBeTrue();

        // Case matters, as it does on a wire, and an input is not an output.
        schema.TryGetInput("open", out EntityPort missing).ShouldBeFalse();
        missing.ShouldBe(default);
        schema.TryGetInput("OnOpen", out _).ShouldBeFalse();
        schema.TryGetOutput("Open", out _).ShouldBeFalse();
        schema.TryGetInput(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_placement_numbers_are_the_bytes_a_schema_file_stores()
    {
        // Append only: a renumber changes what every exported schema says.
        ((byte)EntityPlacement.Point).ShouldBe((byte)0);
        ((byte)EntityPlacement.Brush).ShouldBe((byte)1);
        ((byte)EntityPlacement.Abstract).ShouldBe((byte)2);
        ((byte)EntityPlacement.Volume).ShouldBe((byte)3);
    }

    [Fact]
    public void A_schema_with_no_class_name_is_refused()
    {
        Should.Throw<ArgumentException>(() => new EntitySchema(""));
    }

    [Fact]
    public void A_schema_declaring_nothing_carries_empty_lists_rather_than_nulls()
    {
        var schema = new EntitySchema("info_player_start");

        schema.Keyvalues.Count.ShouldBe(0);
        schema.Inputs.Count.ShouldBe(0);
        schema.Outputs.Count.ShouldBe(0);
        schema.DisplayName.ShouldBe("");
    }
}
