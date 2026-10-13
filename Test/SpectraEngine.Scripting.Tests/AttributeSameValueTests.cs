using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// Which value counts as the one an attribute has where two are equal and
/// not alike: 0 and -0. A set of one over the other is a set of the value it
/// has, as it is for a node's own members, so the one set first stays.
/// </summary>
public sealed class AttributeSameValueTests
{
    [Fact]
    public void A_set_of_minus_zero_over_zero_or_of_zero_over_minus_zero_writes_nothing_and_fires_nothing()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local heard = {}
            door.AttributeChanged:Connect(function(name) table.insert(heard, name) end)
            door:SetAttribute("Zero", 0)
            door:SetAttribute("Zero", -0)
            door:SetAttribute("Minus", -0)
            door:SetAttribute("Minus", 0)
            task.wait()
            print(table.concat(heard, " "))
            print(1 / door:GetAttribute("Zero"), 1 / door:GetAttribute("Minus"), door:GetAttribute("Zero") == door:GetAttribute("Minus"))
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Zero Minus", "inf -inf true"]);
    }

    [Fact]
    public void The_same_holds_for_a_part_of_a_Vector3_a_CFrame_and_a_Color3()
    {
        using ScriptLevel level = House.With("""
            local door = workspace.House.Door
            local heard = {}
            door.AttributeChanged:Connect(function(name) table.insert(heard, name) end)
            door:SetAttribute("Point", Vector3.new(0, 1, 0))
            door:SetAttribute("Point", Vector3.new(-0, 1, -0))
            door:SetAttribute("Frame", CFrame.new(0, 2, 0))
            door:SetAttribute("Frame", CFrame.new(-0, 2, -0))
            door:SetAttribute("Tint", Color3.new(0, 0, 1))
            door:SetAttribute("Tint", Color3.new(-0, -0, 1))
            task.wait()
            print(table.concat(heard, " "))
            print(1 / door:GetAttribute("Point").X, 1 / door:GetAttribute("Frame").Z, 1 / door:GetAttribute("Tint").R)
            """);

        level.Play().Tick(2);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["Point Frame Tint", "inf inf inf"]);
    }

    // A keyvalue that reads as 0 is the value the name has until a script sets it.
    [Fact]
    public void A_set_of_minus_zero_over_a_keyvalue_that_reads_zero_fires_nothing()
    {
        using var level = new ScriptLevel();
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        sign.Entity = new EntityData(SignEntity.Class);
        sign.Entity.SetValue("speed", "0");
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            sign.AttributeChanged:Connect(function(name) print("changed", name) end)
            sign:SetAttribute("speed", -0)
            sign:SetAttribute("offset", Vector3.new(-0, 0, -0))
            print(1 / sign:GetAttribute("speed"), 1 / sign:GetAttribute("offset").X)
            sign:SetAttribute("speed", 0.5)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["inf inf", "changed speed"]);
    }
}
