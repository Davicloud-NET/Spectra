using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;

namespace SpectraEngine.Entities.Tests;

public sealed class LogicBranchTests
{
    private const float Tick = 1f / 60f;

    private readonly List<string> _log = [];

    [Fact]
    public void Test_reports_the_value_the_branch_holds()
    {
        (EntityWorld world, LogicBranch branch) = Start();

        EntityRuntime.Send(branch, "Test").ShouldBeTrue();
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnFalse:"]);
    }

    [Fact]
    public void A_branch_starts_at_its_initial_value()
    {
        (EntityWorld world, LogicBranch branch) = Start(initialValue: "1");

        branch.Value.ShouldBeTrue();

        EntityRuntime.Send(branch, "Test");
        world.Tick(Tick);

        _log.ShouldBe(["sink:OnTrue:"]);
    }

    [Fact]
    public void SetValue_stores_and_fires_nothing()
    {
        (EntityWorld world, LogicBranch branch) = Start();

        EntityRuntime.Send(branch, "SetValue", "1").ShouldBeTrue();
        world.Tick(Tick);

        branch.Value.ShouldBeTrue();
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void SetValueTest_stores_and_reports_in_one_input()
    {
        (EntityWorld world, LogicBranch branch) = Start();

        EntityRuntime.Send(branch, "SetValueTest", "1").ShouldBeTrue();
        world.Tick(Tick);
        EntityRuntime.Send(branch, "SetValueTest", "0");
        world.Tick(Tick);

        branch.Value.ShouldBeFalse();
        _log.ShouldBe(["sink:OnTrue:", "sink:OnFalse:"]);
    }

    [Fact]
    public void Toggle_flips_the_value_and_fires_nothing()
    {
        (EntityWorld world, LogicBranch branch) = Start();

        EntityRuntime.Send(branch, "Toggle").ShouldBeTrue();
        world.Tick(Tick);

        branch.Value.ShouldBeTrue();
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void ToggleTest_flips_the_value_and_reports_it()
    {
        (EntityWorld world, LogicBranch branch) = Start();

        EntityRuntime.Send(branch, "ToggleTest").ShouldBeTrue();
        world.Tick(Tick);
        EntityRuntime.Send(branch, "ToggleTest");
        world.Tick(Tick);

        branch.Value.ShouldBeFalse();
        _log.ShouldBe(["sink:OnTrue:", "sink:OnFalse:"]);
    }

    [Theory]
    [InlineData("SetValue", "true")]
    [InlineData("SetValue", "")]
    [InlineData("SetValueTest", "2")]
    [InlineData("SetValueTest", "")]
    public void An_argument_that_is_not_0_or_1_is_refused_and_nothing_fires(string input, string argument)
    {
        (EntityWorld world, LogicBranch branch) = Start(initialValue: "1");

        EntityRuntime.Send(branch, input, argument).ShouldBeTrue();
        world.Tick(Tick);

        branch.Value.ShouldBeTrue();
        branch.RefusedInputCount.ShouldBe(1);
        _log.ShouldBeEmpty();
    }

    [Fact]
    public void An_output_sends_the_parameter_its_wire_authored()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "branch", "logic_branch");
        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(node, LogicBranch.OnTrue, "sink", "Add", "5");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(EntityRuntime.Live<LogicBranch>(world, node), "SetValueTest", "1");
        world.Tick(Tick);

        _log.ShouldBe(["sink:Add:5"]);
    }

    [Fact]
    public void A_test_passes_its_activator_on()
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "branch", "logic_branch");
        SceneNode player = EntityRuntime.Place(scene.Root, "player", "test_recorder");
        EntityRuntime.Wire(node, LogicBranch.OnFalse, TargetNameIndex.ActivatorToken, "Ping");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();

        EntityRuntime.Send(
            EntityRuntime.Live<LogicBranch>(world, node),
            "Test",
            activator: EntityRuntime.Live<RecordingEntity>(world, player));
        world.Tick(Tick);

        _log.ShouldBe(["player:Ping:"]);
    }

    // A branch with each output wired to the recorder under the output's own name.
    private (EntityWorld World, LogicBranch Branch) Start(string? initialValue = null)
    {
        var scene = new Scene("Entities");
        SceneNode node = EntityRuntime.Place(scene.Root, "branch", "logic_branch");
        if (initialValue is not null)
            node.Entity!.SetValue("initialvalue", initialValue);

        EntityRuntime.Place(scene.Root, "sink", "test_recorder");
        EntityRuntime.Wire(node, LogicBranch.OnTrue, "sink", LogicBranch.OnTrue);
        EntityRuntime.Wire(node, LogicBranch.OnFalse, "sink", LogicBranch.OnFalse);

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog(_log));
        world.Activate();
        return (world, EntityRuntime.Live<LogicBranch>(world, node));
    }
}
