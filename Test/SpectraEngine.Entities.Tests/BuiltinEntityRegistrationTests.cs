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

        EntityCatalog.Shared.TryCreate("func_door", out Entity? door).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("func_movelinear", out Entity? moveLinear).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("info_player_start", out Entity? start).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_auto", out Entity? auto).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_branch", out Entity? branch).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_case", out Entity? cases).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_compare", out Entity? compare).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_relay", out Entity? relay).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("logic_timer", out Entity? timer).ShouldBeTrue();
        EntityCatalog.Shared.TryCreate("math_counter", out Entity? counter).ShouldBeTrue();

        door.ShouldBeOfType<FuncDoor>();
        moveLinear.ShouldBeOfType<FuncMoveLinear>();
        start.ShouldBeOfType<InfoPlayerStart>();
        auto.ShouldBeOfType<LogicAuto>();
        branch.ShouldBeOfType<LogicBranch>();
        cases.ShouldBeOfType<LogicCase>();
        compare.ShouldBeOfType<LogicCompare>();
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
    public void The_built_in_roster_is_these_classes()
    {
        BuiltinEntities.Schemas
            .Select(schema => schema.ClassName)
            .ShouldBe(
            [
                "func_door", "func_movelinear",
                "info_player_start",
                "logic_auto", "logic_branch", "logic_case", "logic_compare",
                "logic_relay", "logic_timer", "math_counter",
            ]);
    }

    [Fact]
    public void Every_logic_class_is_filed_under_Logic_and_placed_as_an_abstract_node()
    {
        EntitySchema[] logic = BuiltinEntities.Schemas
            .Where(schema => schema.ClassName.StartsWith("logic_", StringComparison.Ordinal)
                          || schema.ClassName.StartsWith("math_", StringComparison.Ordinal))
            .ToArray();

        logic.Length.ShouldBe(7);
        foreach (EntitySchema schema in logic)
        {
            bool isLogic = schema.ClassName.StartsWith("logic_", StringComparison.Ordinal)
                || schema.ClassName == "math_counter";
            if (!isLogic)
                continue;

            schema.Group.ShouldBe("Logic", schema.ClassName);
            schema.Placement.ShouldBe(EntityPlacement.Abstract, schema.ClassName);
        }
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
    public void The_autos_schema_is_one_output_and_nothing_to_set_or_send()
    {
        EntitySchema schema = LogicAuto.SpectraSchema;

        schema.ClassName.ShouldBe("logic_auto");
        schema.DisplayName.ShouldBe("Logic Auto");
        schema.Keyvalues.ShouldBeEmpty();
        schema.Inputs.ShouldBeEmpty();
        schema.Outputs.ShouldBe([LogicAuto.OnMapSpawn]);
    }

    [Fact]
    public void The_branchs_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = LogicBranch.SpectraSchema;

        schema.ClassName.ShouldBe("logic_branch");
        schema.Inputs.ShouldBe(["SetValue", "SetValueTest", "Toggle", "ToggleTest", "Test"]);
        schema.Outputs.ShouldBe([LogicBranch.OnTrue, LogicBranch.OnFalse]);

        schema.Keyvalues.Count.ShouldBe(1);
        KeyvalueDescriptor initial = schema.Keyvalues[0];
        initial.Name.ShouldBe("initialvalue");
        initial.Type.ShouldBe(KeyvalueType.Bool);
        initial.Default.ShouldBe("0");
    }

    [Fact]
    public void The_cases_schema_numbers_sixteen_cases_and_one_default()
    {
        EntitySchema schema = LogicCase.SpectraSchema;
        IEnumerable<int> numbers = Enumerable.Range(1, LogicCase.CaseCount);

        schema.ClassName.ShouldBe("logic_case");
        schema.Inputs.ShouldBe(["InValue"]);
        schema.Outputs.ShouldBe([.. numbers.Select(n => $"OnCase{n:00}"), LogicCase.OnDefault]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(numbers.Select(n => $"case{n:00}"));
        schema.Keyvalues.Select(keyvalue => keyvalue.Display).ShouldBe(numbers.Select(n => $"Case {n:00}"));
        schema.Keyvalues.ShouldAllBe(keyvalue => keyvalue.Type == KeyvalueType.String && keyvalue.Default == "");
    }

    [Fact]
    public void The_compares_schema_describes_what_the_class_declares()
    {
        EntitySchema schema = LogicCompare.SpectraSchema;

        schema.ClassName.ShouldBe("logic_compare");
        schema.Inputs.ShouldBe(["SetValue", "SetValueCompare", "SetCompareValue", "Compare"]);
        schema.Outputs.ShouldBe(
        [
            LogicCompare.OnLessThan, LogicCompare.OnEqualTo, LogicCompare.OnNotEqualTo, LogicCompare.OnGreaterThan,
        ]);

        schema.Keyvalues.Select(keyvalue => keyvalue.Name).ShouldBe(["initialvalue", "comparevalue"]);
        schema.Keyvalues.ShouldAllBe(keyvalue => keyvalue.Type == KeyvalueType.Float && keyvalue.Default == "0");
    }

    [Fact]
    public void An_output_constant_spells_its_own_member_name()
    {
        LogicAuto.OnMapSpawn.ShouldBe(nameof(LogicAuto.OnMapSpawn));
        LogicBranch.OnTrue.ShouldBe(nameof(LogicBranch.OnTrue));
        LogicBranch.OnFalse.ShouldBe(nameof(LogicBranch.OnFalse));
        LogicCase.OnCase01.ShouldBe(nameof(LogicCase.OnCase01));
        LogicCase.OnCase16.ShouldBe(nameof(LogicCase.OnCase16));
        LogicCase.OnDefault.ShouldBe(nameof(LogicCase.OnDefault));
        LogicCompare.OnLessThan.ShouldBe(nameof(LogicCompare.OnLessThan));
        LogicCompare.OnEqualTo.ShouldBe(nameof(LogicCompare.OnEqualTo));
        LogicCompare.OnNotEqualTo.ShouldBe(nameof(LogicCompare.OnNotEqualTo));
        LogicCompare.OnGreaterThan.ShouldBe(nameof(LogicCompare.OnGreaterThan));
        LogicRelay.OnTrigger.ShouldBe(nameof(LogicRelay.OnTrigger));
        LogicTimer.OnTimer.ShouldBe(nameof(LogicTimer.OnTimer));
        MathCounter.OutValue.ShouldBe(nameof(MathCounter.OutValue));
        MathCounter.OnHitMax.ShouldBe(nameof(MathCounter.OnHitMax));
        MathCounter.OnHitMin.ShouldBe(nameof(MathCounter.OnHitMin));
    }
}
