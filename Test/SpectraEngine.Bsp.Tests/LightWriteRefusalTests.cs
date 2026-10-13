using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What <see cref="ScriptWrites"/> refuses of a light beyond a value out of
/// range: a colour or a brightness that is a number and makes the light's
/// colour none, and a light that sits on a part.
/// </summary>
public sealed class LightWriteRefusalTests
{
    private const string TooMuch = "that is more light than a number holds.";

    private readonly Scene _scene = new("Lights");

    // A colour is raised to the power 2.4 on its way to the light, so a part
    // far below the largest float is already past it.
    [Theory]
    [InlineData(1e17f, "Color cannot be 0, 1E+17, 0: " + TooMuch)]
    [InlineData(3e38f, "Color cannot be 0, 3E+38, 0: " + TooMuch)]
    public void A_colour_whose_part_is_a_number_and_too_large_to_light_with_is_refused(float part, string words)
    {
        SceneNode lamp = Lamp();
        EntityWorld world = Started();

        new ScriptWrites(world)
            .TryWrite(lamp, SettableMemberId.Color, MemberValue.Of(new Color3(0f, part, 0f)), out string? refusal)
            .ShouldBeFalse();

        refusal.ShouldBe(words);
        lamp.Light.ShouldNotBeNull().Color.ShouldBe(Vector3.One);
        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_brightness_that_is_a_number_and_too_large_for_the_lights_colour_is_refused()
    {
        SceneNode lamp = Lamp();
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(4f, 4f, 4f)));

        writes.TryWrite(lamp, SettableMemberId.Brightness, MemberValue.Of(3e38), out string? refusal).ShouldBeFalse();

        refusal.ShouldBe("Brightness cannot be 3E+38: with the light's Color " + TooMuch);
        lamp.Light.ShouldNotBeNull().Intensity.ShouldBe(1f);
        world.Journal.Count.ShouldBe(1);
    }

    [Fact]
    public void A_colour_the_light_could_take_is_refused_when_its_brightness_makes_it_too_much()
    {
        SceneNode lamp = Lamp();
        var writes = new ScriptWrites(Started());
        var bright = new Color3(1e5f, 0f, 0f);
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(bright));
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(1f, 1f, 1f)));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(1e30));

        writes.TryWrite(lamp, SettableMemberId.Color, MemberValue.Of(bright), out string? refusal).ShouldBeFalse();

        refusal.ShouldBe("Color cannot be 100000, 0, 0: with the light's Brightness " + TooMuch);
        lamp.Light.ShouldNotBeNull().Color.ShouldBe(Vector3.One);
    }

    // The largest brightness a white light takes, and the darkest light of all.
    [Theory]
    [InlineData(1f, 3.4e38)]
    [InlineData(1e17f, 0.0)]
    [InlineData(0f, 3.4e38)]
    public void Whatever_the_door_takes_the_renderer_is_handed_numbers(float part, double brightness)
    {
        SceneNode lamp = Lamp();
        var writes = new ScriptWrites(Started());
        var view = new RenderView();

        writes.TryWrite(lamp, SettableMemberId.Brightness, MemberValue.Of(brightness), out _);
        writes.TryWrite(lamp, SettableMemberId.Color, MemberValue.Of(new Color3(part, part, part)), out _);
        _scene.BuildRenderView(_scene.Camera, view);

        Vector4 drawn = view.Lights[0].ColorIntensity;
        (float.IsFinite(drawn.X) && float.IsFinite(drawn.Y) && float.IsFinite(drawn.Z)).ShouldBeTrue(drawn.ToString());
    }

    // A node with a brush is a Part to a script, and the row of a light's
    // member names lights only.
    [Fact]
    public void A_part_that_carries_a_light_has_no_member_of_a_light()
    {
        SceneNode bulb = EntityMotion.BrushNode(_scene.Root, "Bulb", BrushKind.Part, Vector3.Zero);
        bulb.Light = new Light { Kind = LightKind.Point };
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        writes.TryWrite(bulb, SettableMemberId.Enabled, MemberValue.Of(false), out string? ofEnabled).ShouldBeFalse();
        writes.TryWrite(bulb, SettableMemberId.Range, MemberValue.Of(3.0), out string? ofRange).ShouldBeFalse();

        ofEnabled.ShouldBe("Enabled is not a valid member of Part \"Bulb\"");
        ofRange.ShouldBe("Range is not a valid member of Part \"Bulb\"");
        ScriptWrites.Read(bulb, SettableMemberId.Brightness).ShouldBe(MemberValue.None);
        ScriptWrites.Read(bulb, SettableMemberId.Color).ShouldBe(MemberValue.None);
        ScriptWrites.Has(bulb, SettableMemberId.CanCollide).ShouldBeTrue();
        bulb.Light.ShouldNotBeNull().Enabled.ShouldBeTrue();
        world.Journal.Count.ShouldBe(0);
    }

    private SceneNode Lamp()
    {
        SceneNode lamp = _scene.Root.CreateChild("Lamp");
        lamp.Light = new Light { Kind = LightKind.Point };
        return lamp;
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        return world;
    }

    private static void Write(ScriptWrites writes, SceneNode node, SettableMemberId member, MemberValue value) =>
        writes.TryWrite(node, member, in value, out string? refusal).ShouldBeTrue(refusal);
}
