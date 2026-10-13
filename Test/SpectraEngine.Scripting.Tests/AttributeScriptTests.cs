using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// GetAttribute: what a level builder typed into an entity's keyvalues, read
/// by a script as a value of the type the entity's class gives it.
/// </summary>
public sealed class AttributeScriptTests
{
    [Fact]
    public void A_keyvalue_reads_as_a_value_of_the_type_its_class_gives_it()
    {
        using var level = new ScriptLevel();
        SceneNode partner = level.Part("Partner", new Vector3(3f, 1f, 3f));
        EntityData sign = Sign(level).Entity.ShouldNotBeNull();
        sign.SetValue("lit", "0");
        sign.SetValue("count", "-42");
        sign.SetValue("speed", "0.1");
        sign.SetValue("reach", "12.5");
        sign.SetValue("label", "Way out");
        sign.SetValue("mode", "blink");
        sign.SetValue("offset", "1 -2.5 3");
        sign.SetValue("facing", "0 180 0");
        sign.SetValue("tint", KeyvalueWire.FormatColor(ColorSpace.SrgbToLinear(new Vector3(1f, 128f / 255f, 0f))));
        sign.SetValue("partner", KeyvalueWire.Format(partner.Id));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            for _, name in { "lit", "count", "speed", "reach", "label", "mode", "offset", "facing", "tint", "partner" } do
                local value = sign:GetAttribute(name)
                print(name, typeof(value), value)
            end
            print(sign:GetAttribute("tint") == Color3.fromRGB(255, 128, 0), sign:GetAttribute("partner") == workspace.Partner)
            print(sign:GetAttribute("speed") + 1, sign:GetAttribute("offset").Y, sign:GetAttribute("count") % 5)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(
        [
            "lit boolean false",
            "count number -42",
            "speed number 0.1",
            "reach number 12.5",
            "label string Way out",
            "mode string blink",
            "offset Vector3 1, -2.5, 3",
            "facing Vector3 0, 180, 0",
            "tint Color3 1, 0.501961, 0",
            "partner Instance Partner",
            "true true",
            "1.1 -2.5 3",
        ]);
    }

    [Fact]
    public void A_keyvalue_the_builder_left_alone_reads_as_the_class_gives_it()
    {
        using var level = new ScriptLevel();
        Sign(level);
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            print(sign:GetAttribute("lit"), sign:GetAttribute("count"), sign:GetAttribute("speed"), sign:GetAttribute("label"))
            print(sign:GetAttribute("facing"), sign:GetAttribute("tint"), sign:GetAttribute("partner"))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["true 3 1.5 Sign", "0, 90, 0 1, 1, 1 nil"]);
    }

    // The script API has no value of two numbers, so they stay text.
    [Fact]
    public void A_name_the_entity_does_not_have_is_nil_and_what_scripts_have_no_value_for_is_text()
    {
        using var level = new ScriptLevel();
        EntityData sign = Sign(level).Entity.ShouldNotBeNull();
        sign.SetValue("note", "kept as it is");
        sign.SetValue("count", "many");
        sign.SetValue("partner", KeyvalueWire.Format(Guid.NewGuid()));
        level.Part("Crate", new Vector3(3f, 1f, 3f));
        level.AddScript(level.Scene.Root, """
            local sign = workspace.Sign
            print(sign:GetAttribute("Speed"), sign:GetAttribute(""), sign:GetAttribute("nothing"))
            print(sign:GetAttribute("note"), typeof(sign:GetAttribute("size")), sign:GetAttribute("size"))
            print(sign:GetAttribute("count"), sign:GetAttribute("partner"))
            print(workspace.Crate:GetAttribute("speed"), workspace:GetAttribute("speed"), script:GetAttribute("lit"))
            print(sign.GetAttribute(sign, "label"), pcall(sign.GetAttribute, sign))
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed[..4].ShouldBe(
        [
            "nil nil nil",
            "kept as it is string 1 2",
            "3 nil",
            "nil nil nil",
        ]);
        level.Printed[4].ShouldStartWith("Sign false ");
        level.Printed[4].ShouldEndWith("invalid argument #1 to 'GetAttribute' (string expected, got nil)");
    }

    // The runtime copies an entity's keyvalues when the level starts and
    // never writes back, so the node still holds what was built.
    [Fact]
    public void An_attribute_is_what_the_level_was_built_with_whatever_the_entity_does_while_it_runs()
    {
        using var level = new ScriptLevel();
        SceneNode relay = level.Scene.Root.CreateChild("Relay");
        relay.Entity = new EntityData(SayingEntity.Class);
        relay.Entity.SetValue("note", "as built");
        level.AddScript(level.Scene.Root, """
            local relay = workspace.Relay
            print(relay:GetAttribute("note"))
            relay.Name = "Renamed"
            task.wait(0.1)
            print(relay:GetAttribute("note"))
            """);

        level.Play().Tick(10);

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["as built", "as built"]);
        relay.Entity.TryGetValue("note", out string kept).ShouldBeTrue();
        kept.ShouldBe("as built");
    }

    private static SceneNode Sign(ScriptLevel level)
    {
        SceneNode sign = level.Scene.Root.CreateChild("Sign");
        sign.Entity = new EntityData(SignEntity.Class);
        return sign;
    }
}
