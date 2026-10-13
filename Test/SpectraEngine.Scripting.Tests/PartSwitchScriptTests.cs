using System.Numerics;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Scripting.Tests.PlayerScripts;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The switches of a part and a mesh as a script reads and sets them:
/// CanCollide, CanQuery, CanTouch and Visible. What a world brush answers,
/// and what the player, a ray and a trigger make of a switch that changed.
/// </summary>
public sealed class PartSwitchScriptTests
{
    private const string Switches = """
        local function switches(part)
            return `{part.CanCollide} {part.CanQuery} {part.CanTouch} {part.Visible}`
        end
        """;

    [Fact]
    public void A_script_reads_the_switches_of_a_part_and_a_mesh_as_they_were_built_and_sets_each()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(5f, 1f, 5f));
        crate.CanQuery = false;
        SceneNode prop = SpatialTestHelpers.CreateMeshNode(level.Scene.Root, "Prop", new Vector3(-5f, 1f, 5f));
        prop.IsRendered = false;
        level.AddScript(level.Scene.Root, Switches + """

            local crate, prop = workspace.Crate, workspace.Prop
            print(switches(crate), switches(prop))
            crate.CanCollide, crate.CanQuery, crate.CanTouch, crate.Visible = false, true, false, false
            prop.CanCollide, prop.CanQuery, prop.CanTouch, prop.Visible = false, false, false, true
            print(switches(crate), switches(prop))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "true false true true true true true false",
            "false true false false false false false true",
        ]);
        (crate.CanCollide, crate.CanQuery, crate.CanTouch, crate.IsRendered).ShouldBe((false, true, false, false));
        (prop.CanCollide, prop.CanQuery, prop.CanTouch, prop.IsRendered).ShouldBe((false, false, false, true));
        level.Scene.DrawableNodes.ShouldNotContain(crate);
        level.Scene.DrawableNodes.ShouldContain(prop);
    }

    [Theory]
    [InlineData("CanCollide")]
    [InlineData("CanQuery")]
    [InlineData("CanTouch")]
    [InlineData("Visible")]
    public void A_world_brush_reads_its_switches_and_refuses_a_write_with_words_that_say_to_use_a_part(string member)
    {
        using var level = new ScriptLevel();
        level.AddScript(level.Scene.Root, $"""
            local ground = workspace.Baseplate
            print(ground.{member}, ground:IsA("BasePart"))
            print(select(2, pcall(function() ground.{member} = false end)))
            """);

        level.Play().Tick();

        level.Printed[0].ShouldBe("true true");
        level.Printed[1].ShouldEndWith(
            $"Baseplate cannot change its {member}: it is a world brush, fused into the level, and only parts can " +
            "change while a level plays. Convert the brush to a part.");
        level.Baseplate.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
        level.Baseplate.IsRendered.ShouldBeTrue();
    }

    [Theory]
    [InlineData("crate.CanCollide = 0", "Unable to assign property CanCollide. boolean expected, got number")]
    [InlineData("crate.Visible = 'no'", "Unable to assign property Visible. boolean expected, got string")]
    [InlineData("crate.CanTouch = nil", "Unable to assign property CanTouch. boolean expected, got nil")]
    [InlineData("crate.Transparency = 1", "Transparency is not a valid member of Part \"Crate\"")]
    [InlineData("workspace.Lamp.CanCollide = false", "CanCollide is not a valid member of PointLight \"Lamp\"")]
    [InlineData("workspace.Visible = false", "Visible is not a valid member of Folder \"Root\"")]
    [InlineData("script.CanQuery = false", "CanQuery is not a valid member of Script \"Script\"")]
    public void A_switch_refuses_what_is_no_boolean_and_only_a_part_or_a_mesh_has_one(string write, string words)
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(5f, 1f, 5f));
        level.Scene.Root.CreateChild("Lamp").Light = new Light { Kind = LightKind.Point };
        level.AddScript(level.Scene.Root, $"local crate = workspace.Crate\nprint(select(2, pcall(function() {write} end)))");

        level.Play().Tick();

        level.Printed.ShouldHaveSingleItem().ShouldEndWith(words);
        crate.PhysicsFlags.ShouldBe(PhysicsFlags.Default);
    }

    [Fact]
    public void A_ray_stops_finding_a_part_whose_CanQuery_a_script_turned_off()
    {
        using var level = new ScriptLevel();
        SceneNode crate = level.Part("Crate", new Vector3(5f, 1f, 5f));
        level.AddScript(crate, """
            task.wait()
            script.Parent.CanQuery = false
            task.wait()
            script.Parent.CanQuery = true
            """);
        var down = new Ray3(new Vector3(5f, 4f, 5f), -Vector3.UnitY);
        level.Play().Tick();
        level.Scene.Raycast(in down, out SceneRaycastHit first).ShouldBeTrue();
        first.Node.ShouldBeSameAs(crate);

        level.Tick();
        level.Scene.Raycast(in down, out SceneRaycastHit through).ShouldBeTrue();
        through.Node.ShouldBeSameAs(level.Baseplate);

        level.Tick();
        level.Scene.Raycast(in down, out SceneRaycastHit again).ShouldBeTrue();
        again.Node.ShouldBeSameAs(crate);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_write_of_what_a_switch_already_has_records_nothing_and_Stop_puts_back_the_rest()
    {
        using var level = new ScriptLevel();
        SceneNode zone = level.Part("Zone", new Vector3(5f, 1f, 5f));
        zone.CanCollide = false;
        zone.IsRendered = false;
        level.AddScript(zone, """
            local zone = script.Parent
            zone.CanCollide, zone.CanQuery, zone.CanTouch, zone.Visible = false, true, true, false
            task.wait()
            zone.CanCollide, zone.CanQuery, zone.CanTouch, zone.Visible = true, false, false, true
            """);
        var before = new LevelState(level.Scene);

        level.Play().Tick();
        level.Entities.Journal.Count.ShouldBe(0);

        level.Tick();
        level.Entities.Journal.Count.ShouldBe(4);
        new LevelState(level.Scene).IsSavedAs(before).ShouldBeFalse();

        level.Stop();

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).ShouldBe(before, "after Stop");
        level.Scene.DrawableNodes.ShouldNotContain(zone);
    }

    // The mover asks each part near the player whether it is solid on every
    // tick, so the tick after a write walks by the new answer.
    [Fact]
    public void The_player_cannot_pass_a_wall_until_a_script_turns_its_collision_off_and_is_stopped_again_when_it_is_back()
    {
        const float NearFace = 3.75f;
        const float FarFace = 4.25f;
        using var level = new ScriptLevel();
        SceneNode wall = Wall(level, new Vector3(4f, 1.5f, 0f), new Vector3(0.25f, 1.5f, 3f));
        level.AddScript(wall, """
            local wall = script.Parent
            local ticks = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                ticks += 1
                if ticks == 100 then wall.CanCollide = false end
                if ticks == 200 then wall.CanCollide = true end
            end)
            """);
        level.Play();
        float radius = Walker(level).Tuning.Radius;

        Tick(level, Forward(), 100);
        float stopped = Walker(level).State.Position.X;
        stopped.ShouldBe(NearFace - radius, 0.05f);

        Tick(level, Forward());
        Walker(level).State.Position.X.ShouldBeGreaterThan(stopped + 0.01f);

        Tick(level, Forward(), 99);
        Walker(level).State.Position.X.ShouldBeGreaterThan(FarFace + radius + 1f);
        wall.CanCollide.ShouldBeTrue();

        Tick(level, Forward(MathF.PI), 200);
        Walker(level).State.Position.X.ShouldBe(FarFace + radius, 0.05f);
        level.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void A_trigger_stops_firing_while_a_script_has_its_CanTouch_off_and_fires_again_when_it_is_on()
    {
        using var level = new ScriptLevel();
        SceneNode zone = Wall(level, new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f));
        zone.Name = "Zone";
        zone.Entity = new SpectraEngine.Core.Entities.EntityData(SensingEntity.Class);
        zone.CanCollide = false;
        level.AddScript(zone, """
            local zone = script.Parent
            local ticks = 0
            game:GetService("RunService").Heartbeat:Connect(function()
                ticks += 1
                if ticks == 20 then zone.CanTouch = false end
                if ticks == 40 then zone.CanTouch = true end
            end)
            """);

        level.Play().Tick(20);
        level.Printed.ShouldBe([SensingEntity.Touched]);

        level.Tick();
        level.Printed.ShouldBe([SensingEntity.Touched, SensingEntity.Left]);

        level.Tick(19);
        level.Printed.ShouldBe([SensingEntity.Touched, SensingEntity.Left]);

        level.Tick();
        level.Printed.ShouldBe([SensingEntity.Touched, SensingEntity.Left, SensingEntity.Touched]);
        level.Errors.ShouldBeEmpty();
    }

    // A part box of any size, where the level's own parts are half a unit.
    private static SceneNode Wall(ScriptLevel level, Vector3 center, Vector3 half)
    {
        SceneNode wall = level.Scene.Root.CreateChild("Wall");
        wall.LocalPosition = center;
        wall.BrushKind = BrushKind.Part;
        wall.Brush = SpectraEngine.Core.Bsp.Brush.CreateBox(-half, half);
        return wall;
    }
}
