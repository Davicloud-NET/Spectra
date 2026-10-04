using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

public sealed class LogicCaseTests
{
    private const float Tick = 1f / 60f;

    private readonly List<string> _log = [];

    [Fact]
    public void A_value_fires_the_output_of_the_case_it_equals_and_no_other()
    {
        (EntityWorld world, LogicCase cases) = Start(("case01", "red"), ("case02", "green"));

        EntityRuntime.Send(cases, "InValue", "green").ShouldBeTrue();
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnCase02:"]);
    }

    [Fact]
    public void Each_of_the_sixteen_cases_has_a_keyvalue_and_an_output_of_its_own()
    {
        var keyvalues = new (string Key, string Value)[LogicCase.CaseCount];
        for (int i = 0; i < keyvalues.Length; i++)
            keyvalues[i] = ($"case{i + 1:00}", $"value{i + 1}");

        (EntityWorld world, LogicCase cases) = Start(keyvalues);

        var expected = new List<string>();
        for (int i = 0; i < keyvalues.Length; i++)
        {
            EntityRuntime.Send(cases, "InValue", keyvalues[i].Value);
            world.Tick(Tick);
            expected.Add($"sink:OnCase{i + 1:00}:");
        }

        _log.ShouldBe(expected);
    }

    [Fact]
    public void A_value_no_case_holds_fires_OnDefault_carrying_the_value()
    {
        (EntityWorld world, LogicCase cases) = Start(("case01", "red"));

        EntityRuntime.Send(cases, "InValue", "purple");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnDefault:purple"]);
    }

    [Fact]
    public void The_lowest_matching_case_wins()
    {
        (EntityWorld world, LogicCase cases) = Start(("case03", "red"), ("case07", "red"));

        EntityRuntime.Send(cases, "InValue", "red");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnCase03:"]);
    }

    [Fact]
    public void An_empty_case_is_unused_so_a_blank_value_goes_to_OnDefault()
    {
        (EntityWorld world, LogicCase cases) = Start(("case01", ""), ("case02", "red"));

        EntityRuntime.Send(cases, "InValue");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnDefault:"]);
    }

    [Theory]
    [InlineData("3.0", "3")]
    [InlineData("3", " 3 ")]
    [InlineData("10000000000", "1E+10")]
    public void Two_numbers_match_as_numbers_however_they_are_spelled(string authored, string incoming)
    {
        (EntityWorld world, LogicCase cases) = Start(("case01", authored));

        EntityRuntime.Send(cases, "InValue", incoming);
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnCase01:"]);
    }

    [Theory]
    [InlineData("Red", "red")]
    [InlineData("red", " red")]
    [InlineData("3", "three")]
    [InlineData("three", "3")]
    public void Text_matches_ordinally_and_never_matches_a_number(string authored, string incoming)
    {
        (EntityWorld world, LogicCase cases) = Start(("case01", authored));

        EntityRuntime.Send(cases, "InValue", incoming);
        world.Tick(Tick);

        _log.ShouldBe([$"sink:OnDefault:{incoming}"]);
    }

    [Fact]
    public void A_counters_value_picks_the_case_authored_for_it()
    {
        var scene = new Scene("Entities");
        SceneNode counter = EntityRuntime.Place(scene.Root, "counter", "math_counter");
        SceneNode node = EntityRuntime.Place(scene.Root, "cases", "logic_case");
        node.Entity!.SetValue("case01", "1.0");
        node.Entity!.SetValue("case02", "2.0");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(counter, MathCounter.OutValue, "cases", "InValue");
        EntityRuntime.Wire(node, LogicCase.OnCase02, "sink", LogicCase.OnCase02);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<MathCounter>(world, counter), "Add", "2");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnCase02:"]);
    }

    [Fact]
    public void A_numbered_output_sends_the_parameter_its_wire_authored()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "cases", "logic_case");
        node.Entity!.SetValue("case01", "red");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(node, LogicCase.OnCase01, "sink", "Add", "5");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<LogicCase>(world, node), "InValue", "red");
        world.Tick(Tick);

        _log.ShouldBe(["sink:Add:5"]);
    }

    [Fact]
    public void OnDefault_hands_the_value_to_a_second_logic_case()
    {
        var scene = new Scene("Entities");
        SceneNode first = EntityRuntime.Place(scene.Root, "first", "logic_case");
        first.Entity!.SetValue("case01", "1");
        SceneNode second = EntityRuntime.Place(scene.Root, "second", "logic_case");
        second.Entity!.SetValue("case01", "17");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(first, LogicCase.OnDefault, "second", "InValue");
        EntityRuntime.Wire(second, LogicCase.OnCase01, "sink", "Seventeen");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<LogicCase>(world, first), "InValue", "17");
        world.Tick(Tick);

        _log.ShouldBe(["sink:Seventeen:"]);
    }

    [Fact]
    public void A_match_and_a_default_both_pass_the_activator_on()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "cases", "logic_case");
        node.Entity!.SetValue("case01", "red");
        SceneNode player = EntityRuntime.Place(scene.Root, "player", "test_recorder");
        EntityRuntime.Wire(node, LogicCase.OnCase01, TargetNameIndex.ActivatorToken, "Matched");
        EntityRuntime.Wire(node, LogicCase.OnDefault, TargetNameIndex.ActivatorToken, "Missed");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        var cases = EntityRuntime.Live<LogicCase>(world, node);
        var activator = EntityRuntime.Live<RecordingEntity>(world, player);
        EntityRuntime.Send(cases, "InValue", "red", activator);
        EntityRuntime.Send(cases, "InValue", "blue", activator);
        world.Tick(Tick);

        _log.ShouldBe(["player:Matched:", "player:Missed:blue"]);
    }

    [Fact]
    public void There_is_no_PickRandom_input()
    {
        (_, LogicCase cases) = Start(("case01", "red"));

        EntityRuntime.Send(cases, "PickRandom").ShouldBeFalse();
    }

    // A logic_case with every output wired to the recorder under the output's own name.
    private (EntityWorld World, LogicCase Cases) Start(params (string Key, string Value)[] keyvalues)
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "cases", "logic_case");
        foreach ((string key, string value) in keyvalues)
            node.Entity!.SetValue(key, value);

        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        foreach (string output in LogicCase.SpectraSchema.Outputs)
            EntityRuntime.Wire(node, output, "sink", output);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();
        return (world, EntityRuntime.Live<LogicCase>(world, node));
    }
}
