using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// GetAttributes hands out an entity's keyvalues as they read, and
/// SetAttribute does not take every one of them back. A loop that copies the
/// table to another node is told which, and the summary says so.
/// </summary>
public sealed class AttributeCopyTests
{
    [Fact]
    public void A_keyvalue_that_is_a_node_or_has_a_name_no_attribute_has_is_handed_out_and_cannot_be_set_back()
    {
        using var level = new ScriptLevel();
        SceneNode partner = level.Part("Partner", new Vector3(3f, 1f, 3f));
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        var data = new EntityData(SignEntity.Class);
        data.SetValue("partner", KeyvalueWire.Format(partner.Id));
        data.SetValue("my note", "only the node has it");
        sign.Entity = data;
        level.AddScript(level.Scene.Root, """
            local sign, copy = workspace.Sign, workspace.Partner
            local refused = {}
            for name, value in sign:GetAttributes() do
                local taken, why = pcall(copy.SetAttribute, copy, name, value)
                if not taken then
                    table.insert(refused, name .. ": " .. (why:gsub("^.-:%d+: ", "")))
                end
            end
            table.sort(refused)
            for _, line in refused do
                print(line)
            end
            print(sign:GetAttribute("partner"), sign:GetAttribute("my note"), copy:GetAttribute("label"), copy:GetAttribute("speed"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "my note: 'my note' is not a valid attribute name. An attribute's name is 1 to 100 letters, digits and " +
            "underscores, and does not start with RBX",
            "partner: Instance is not a supported attribute type. An attribute holds a string, a boolean, a number, " +
            "a Vector3, a CFrame or a Color3, and nil takes it away",
            "Partner only the node has it Sign 1.5",
        ]);
    }

    [Fact]
    public void The_words_of_GetAttributes_say_that_SetAttribute_does_not_take_every_keyvalue_back()
    {
        ScriptFunctionDescription all = LuauScriptApi.Description.FindClass("Instance").ShouldNotBeNull()
            .Methods.Single(method => method.Name == "GetAttributes");

        all.Summary.ShouldContain("SetAttribute does not take every keyvalue back");
        all.Summary.ShouldContain("refers to a node");
        all.Summary.ShouldContain("a name no attribute has");
        LuauDefinitions.Write(LuauScriptApi.Description).ShouldContain(all.Summary);
    }
}
