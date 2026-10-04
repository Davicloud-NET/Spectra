using SpectraEngine.Core.Entities;

namespace SpectraEngine.Entities.Tests;

// The only test class that reads EntityCatalog.Shared: reading freezes it,
// so every other test builds its own catalogue.
public sealed class BuiltinEntityRegistrationTests
{
    [Fact]
    public void The_anchor_puts_every_built_in_class_in_the_shared_catalogue()
    {
        BuiltinEntities.EnsureRegistered();

        EntityCatalog.Shared.TryCreate("logic_relay", out Entity? relay).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_timer", out Entity? timer).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("math_counter", out Entity? counter).ShouldBeTrue();

        relay.ShouldBeOfType<LogicRelay>();
        timer.ShouldBeOfType<LogicTimer>();
        counter.ShouldBeOfType<MathCounter>();
    }

    [Fact]
    public void The_anchor_lists_exactly_the_classes_it_claims_to()
    {
        BuiltinEntities.Schemas.Count.ShouldBe(BuiltinEntities.ClassCount);
        BuiltinEntities.EnsureRegistered();
    }

    [Fact]
    public void The_relays_generated_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = LogicRelay.SpectraSchema;

        schema.ClassName.ShouldBe("logic_relay");
        LogicRelay.SpectraClassName.ShouldBe("logic_relay");

        // Derived from the wire name; the class states no Display.
        schema.DisplayName.ShouldBe("Logic Relay");
        schema.Group.ShouldBe("Logic");
        schema.Placement.ShouldBe(EntityPlacement.Abstract);
        schema.Origin.ShouldBe(EntityOrigin.EngineCSharp);

        schema.Inputs.ShouldBe(["Trigger", "Enable", "Disable", "Toggle"]);
        schema.Outputs.ShouldBe([LogicRelay.OnTrigger]);

        schema.Keyvalues.Count.ShouldBe(1);
        KeyvalueDescriptor startDisabled = schema.Keyvalues[0];
        startDisabled.Name.ShouldBe("startdisabled");
        startDisabled.Display.ShouldBe("Start disabled");
        startDisabled.Type.ShouldBe(KeyvalueType.Bool);
        startDisabled.Default.ShouldBe("0");
        startDisabled.HasMin.ShouldBeFalse();
        startDisabled.HasMax.ShouldBeFalse();
    }

    [Fact]
    public void The_timers_generated_schema_carries_the_bound_the_class_declares()
    {
        EntitySchema schema = LogicTimer.SpectraSchema;
        KeyvalueDescriptor refire = schema.Keyvalues.Single(k => k.Name == "refiretime");

        refire.Type.ShouldBe(KeyvalueType.Float);
        refire.HasMin.ShouldBeTrue();
        refire.Min.ShouldBe(LogicTimer.MinimumInterval);

        // No bound is stored as NaN, so ask HasMax instead of comparing Max.
        refire.HasMax.ShouldBeFalse();
    }

    [Fact]
    public void Keyvalues_are_in_declaration_order_which_is_the_order_a_panel_lays_them_out()
    {
        MathCounter.SpectraSchema.Keyvalues
            .Select(keyvalue => keyvalue.Name)
            .ShouldBe(["startvalue", "min", "max"]);
    }

    [Fact]
    public void The_counters_three_outputs_are_all_declared()
    {
        MathCounter.SpectraSchema.Outputs
            .ShouldBe([MathCounter.OutValue, MathCounter.OnHitMax, MathCounter.OnHitMin]);
    }

    [Fact]
    public void An_output_constant_spells_its_own_member_name()
    {
        LogicRelay.OnTrigger.ShouldBe(nameof(LogicRelay.OnTrigger));
        LogicTimer.OnTimer.ShouldBe(nameof(LogicTimer.OnTimer));
        MathCounter.OutValue.ShouldBe(nameof(MathCounter.OutValue));
        MathCounter.OnHitMax.ShouldBe(nameof(MathCounter.OnHitMax));
        MathCounter.OnHitMin.ShouldBe(nameof(MathCounter.OnHitMin));
    }
}
