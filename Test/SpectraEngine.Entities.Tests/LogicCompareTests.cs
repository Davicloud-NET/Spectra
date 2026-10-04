using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

public sealed class LogicCompareTests
{
    private const float Tick = 1f / 60f;

    private readonly List<string> _log = [];

    [Fact]
    public void A_value_below_the_compare_value_is_not_equal_and_then_less()
    {
        (EntityWorld world, LogicCompare compare) = Start(initialValue: "1", compareValue: "2");

        EntityRuntime.Send(compare, "Compare").ShouldBeTrue();
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnNotEqualTo:", "sink:OnLessThan:"]);
    }

    [Fact]
    public void A_value_above_the_compare_value_is_not_equal_and_then_greater()
    {
        (EntityWorld world, LogicCompare compare) = Start(initialValue: "3", compareValue: "2");

        EntityRuntime.Send(compare, "Compare");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnNotEqualTo:", "sink:OnGreaterThan:"]);
    }

    [Fact]
    public void A_value_the_same_as_the_compare_value_is_equal_and_nothing_else()
    {
        (EntityWorld world, LogicCompare compare) = Start(initialValue: "2", compareValue: "2");

        EntityRuntime.Send(compare, "Compare");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnEqualTo:"]);
    }

    [Fact]
    public void SetValue_stores_and_fires_nothing()
    {
        (EntityWorld world, LogicCompare compare) = Start();

        EntityRuntime.Send(compare, "SetValue", "7.5").ShouldBeTrue();
        world.Tick(Tick);

        compare.Value.ShouldBe(7.5f);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void SetCompareValue_stores_and_fires_nothing()
    {
        (EntityWorld world, LogicCompare compare) = Start();

        EntityRuntime.Send(compare, "SetCompareValue", "4").ShouldBeTrue();
        world.Tick(Tick);

        compare.CompareValue.ShouldBe(4f);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void SetValueCompare_stores_and_compares_in_one_input()
    {
        (EntityWorld world, LogicCompare compare) = Start(compareValue: "4");

        EntityRuntime.Send(compare, "SetValueCompare", "4").ShouldBeTrue();
        world.Tick(Tick);

        compare.Value.ShouldBe(4f);
        _log.ShouldBe(["sink:OnEqualTo:"]);
    }

    [Theory]
    [InlineData("1.0000001", "sink:OnGreaterThan:")]
    [InlineData("0.99999994", "sink:OnLessThan:")]
    public void Equal_means_the_same_float_so_the_nearest_neighbour_is_not_equal(string value, string direction)
    {
        (EntityWorld world, LogicCompare compare) = Start(compareValue: "1");

        EntityRuntime.Send(compare, "SetValueCompare", value);
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnNotEqualTo:", direction]);
    }

    [Fact]
    public void Two_spellings_of_one_number_are_equal()
    {
        (EntityWorld world, LogicCompare compare) = Start(compareValue: "16777216");

        EntityRuntime.Send(compare, "SetValueCompare", "1.6777216E+07");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnEqualTo:"]);
    }

    [Theory]
    [InlineData("SetValue", "many")]
    [InlineData("SetValue", "")]
    [InlineData("SetValueCompare", "many")]
    [InlineData("SetValueCompare", "NaN")]
    [InlineData("SetCompareValue", "1,5")]
    public void An_argument_that_is_not_a_number_is_refused_and_nothing_fires(string input, string argument)
    {
        (EntityWorld world, LogicCompare compare) = Start(initialValue: "1", compareValue: "2");

        EntityRuntime.Send(compare, input, argument).ShouldBeTrue();
        world.Tick(Tick);

        compare.Value.ShouldBe(1f);
        compare.CompareValue.ShouldBe(2f);
        compare.RefusedInputCount.ShouldBe(1);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void A_counter_stepping_by_ones_lands_on_the_compare_value()
    {
        var scene = new Scene("Entities");
        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        SceneNode node = EntityRuntime.Place(scene.Root, "compare", "logic_compare");
        node.Entity!.SetValue("comparevalue", "2");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(counter, MathCounter.OutValue, "compare", "SetValueCompare");
        EntityRuntime.Wire(node, LogicCompare.OnLessThan, "sink", LogicCompare.OnLessThan);
        EntityRuntime.Wire(node, LogicCompare.OnEqualTo, "sink", LogicCompare.OnEqualTo);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        var counted = EntityRuntime.Live<MathCounter>(world, counter);
        for (int i = 0; i < 2; i++)
        {
            EntityRuntime.Send(counted, "Add");
            world.Tick(Tick);
        }

        _log.ShouldBe(["sink:OnLessThan:", "sink:OnEqualTo:"]);
    }

    [Fact]
    public void An_output_sends_the_parameter_its_wire_authored()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "compare", "logic_compare");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(node, LogicCompare.OnGreaterThan, "sink", "SetValue", "0");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<LogicCompare>(world, node), "SetValueCompare", "9");
        world.Tick(Tick);

        _log.ShouldBe(["sink:SetValue:0"]);
    }

    [Fact]
    public void A_comparison_passes_its_activator_on()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "compare", "logic_compare");
        SceneNode player = EntityRuntime.Place(scene.Root, "player", "test_recorder");
        EntityRuntime.Wire(node, LogicCompare.OnEqualTo, TargetNameIndex.ActivatorToken, "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(
            EntityRuntime.Live<LogicCompare>(world, node),
            "Compare",
            activator: EntityRuntime.Live<RecordingEntity>(world, player));
        world.Tick(Tick);

        _log.ShouldBe(["player:Ping:"]);
    }

    // A compare with each output wired to the recorder under the output's own name.
    private (EntityWorld World, LogicCompare Compare) Start(
        string? initialValue = null,
        string? compareValue = null)
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "compare", "logic_compare");
        if (initialValue is not null)
            node.Entity!.SetValue("initialvalue", initialValue);
        if (compareValue is not null)
            node.Entity!.SetValue("comparevalue", compareValue);

        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        foreach (string output in LogicCompare.SpectraSchema.Outputs)
            EntityRuntime.Wire(node, output, "sink", output);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();
        return (world, EntityRuntime.Live<LogicCompare>(world, node));
    }
}
