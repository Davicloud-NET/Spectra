using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Entities;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Where an entity's keyvalues and a script's attributes meet. A script reads
/// the keyvalue until a script sets its name, and from then on what was set.
/// The entity goes on with what the level was built with.
/// </summary>
public sealed class AttributeKeyvalueTests
{
    [Fact]
    public void A_keyvalue_reads_until_a_script_sets_its_name_and_then_what_the_script_set()
    {
        using var level = new ScriptLevel();
        EntityData sign = Sign(level).Entity.ShouldNotBeNull();
        sign.SetValue("speed", "0.5");
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            print(sign:GetAttribute("speed"), sign:GetAttribute("label"), sign:GetAttribute("lit"))
            sign:SetAttribute("speed", 9)
            sign:SetAttribute("label", Vector3.new(1, 2, 3))
            print(sign:GetAttribute("speed"), sign:GetAttribute("label"), sign:GetAttribute("lit"))
            sign:SetAttribute("Score", 1)
            print(sign:GetAttribute("Score"), sign:GetAttribute("count"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["0.5 Sign true", "9 1, 2, 3 true", "1 3"]);
    }

    [Fact]
    public void GetAttributes_has_the_keyvalues_of_the_entity_and_over_them_what_scripts_set()
    {
        using var level = new ScriptLevel();
        SceneNode partner = level.Part("Partner", new Vector3(3f, 1f, 3f));
        EntityData sign = Sign(level).Entity.ShouldNotBeNull();
        sign.SetValue("note", "only the node has it");
        sign.SetValue("partner", KeyvalueWire.Format(partner.Id));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            local function list()
                local names = {}
                for name, value in sign:GetAttributes() do
                    table.insert(names, name .. "=" .. typeof(value) .. ":" .. tostring(value))
                end
                table.sort(names)
                print(table.concat(names, " "))
            end
            list()
            sign:SetAttribute("speed", 9)
            sign:SetAttribute("lit", nil)
            sign:SetAttribute("note", "set by a script")
            sign:SetAttribute("Score", 1)
            sign:SetAttribute("partner", "nobody")
            list()
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "count=number:3 facing=Vector3:0, 90, 0 label=string:Sign lit=boolean:true mode=string:steady " +
            "note=string:only the node has it offset=Vector3:0, 0, 0 partner=Instance:Partner reach=number:8 " +
            "size=string:1 2 speed=number:1.5 tint=Color3:1, 1, 1",
            "Score=number:1 count=number:3 facing=Vector3:0, 90, 0 label=string:Sign mode=string:steady " +
            "note=string:set by a script offset=Vector3:0, 0, 0 partner=string:nobody reach=number:8 " +
            "size=string:1 2 speed=number:9 tint=Color3:1, 1, 1",
        ]);
    }

    // The reference the builder left empty reads as nil, and so is no entry.
    [Fact]
    public void A_keyvalue_that_reads_as_nil_is_not_among_the_attributes()
    {
        using var level = new ScriptLevel();
        Sign(level);
        level.AddScript(level.Scene.Root, """
            local all = workspace.Sign:GetAttributes()
            print(all.partner, all.lit, next(workspace:GetAttributes()))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil true nil"]);
    }

    [Fact]
    public void Nil_takes_a_keyvalue_away_for_scripts_and_it_does_not_show_through_again()
    {
        using var level = new ScriptLevel();
        Sign(level);
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            sign:SetAttribute("speed", nil)
            print(sign:GetAttribute("speed"), sign:GetAttributes().speed)
            sign:SetAttribute("speed", nil)
            print(sign:GetAttribute("speed"))
            sign:SetAttribute("speed", 2)
            sign:SetAttribute("speed", nil)
            print(sign:GetAttribute("speed"), sign:GetAttribute("count"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["nil nil", "nil", "nil 3"]);
    }

    // What a keyvalue reads as is its value: a set of the same fires nothing.
    [Fact]
    public void A_set_of_the_value_a_keyvalue_has_fires_nothing_and_a_set_of_another_fires()
    {
        using var level = new ScriptLevel();
        SceneNode partner = level.Part("Partner", new Vector3(3f, 1f, 3f));
        EntityData sign = Sign(level).Entity.ShouldNotBeNull();
        sign.SetValue("partner", KeyvalueWire.Format(partner.Id));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            sign.AttributeChanged:Connect(function(name) print("changed", name, sign:GetAttribute(name)) end)
            sign:SetAttribute("speed", 1.5)
            sign:SetAttribute("lit", true)
            sign:SetAttribute("label", "Sign")
            sign:SetAttribute("tint", sign:GetAttribute("tint"))
            sign:SetAttribute("facing", Vector3.new(0, 90, 0))
            sign:SetAttribute("count", 3)
            sign:SetAttribute("nothing", nil)
            print("set what it had")
            sign:SetAttribute("count", "3")
            sign:SetAttribute("lit", 1)
            sign:SetAttribute("partner", nil)
            sign:SetAttribute("speed", 1.5)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["set what it had", "changed count 3", "changed lit 1", "changed partner nil"]);
    }

    [Fact]
    public void A_door_goes_on_with_the_speed_it_was_built_with_whatever_a_script_sets()
    {
        using var level = new ScriptLevel();
        SceneNode door = level.Part("Door", new Vector3(24f, 1f, 20f));
        var data = new EntityData("func_door");
        data.SetValue("distance", "1");
        data.SetValue("speed", "6");
        data.SetValue("wait", "-1");
        door.Entity = data;
        level.AddScript(door, """
            local door = script.Parent
            door:SetAttribute("speed", 600)
            door:SetAttribute("distance", nil)
            print(door:GetAttribute("speed"), door:GetAttribute("distance"))
            door:Open()
            """);

        level.Play().Tick(3);
        FuncDoor live = level.Entities.Entities.OfType<FuncDoor>().Single();

        // A metre at six a second is ten ticks. At six hundred it would be one.
        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["600 nil"]);
        live.TravelTicks.ShouldBe(10);
        live.TicksTravelled.ShouldBeInRange(1, 9);
        data.TryGetValue("speed", out string speed).ShouldBeTrue();
        speed.ShouldBe("6");
        data.TryGetValue("distance", out string distance).ShouldBeTrue();
        distance.ShouldBe("1");
    }

    private static SceneNode Sign(ScriptLevel level)
    {
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        sign.Entity = new EntityData(SignEntity.Class);
        return sign;
    }
}
