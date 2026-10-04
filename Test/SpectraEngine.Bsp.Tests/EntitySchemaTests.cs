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
            inputs: ["Open", "Close"],
            outputs: ["OnFullyOpen"]);

        schema.ClassName.ShouldBe("func_door");
        schema.Placement.ShouldBe(EntityPlacement.Brush);
        schema.Origin.ShouldBe(EntityOrigin.Luau);
        schema.Keyvalues.Count.ShouldBe(2);
        schema.Keyvalues[1].HasMin.ShouldBeTrue();
        schema.Inputs[0].ShouldBe("Open");
        schema.Outputs.Count.ShouldBe(1);
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
