using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A light as a script reads and sets it: Enabled, Brightness, Color and
/// Range under Roblox's names, what each refuses, and what the renderer and
/// Stop make of a write.
/// </summary>
public sealed class LightScriptTests
{
    [Fact]
    public void A_script_reads_a_light_as_it_was_built()
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level, LightKind.Spot).Light.ShouldNotBeNull();
        light.Intensity = 2.5f;
        light.Range = 12f;
        light.Enabled = false;

        // What a colour picker makes of #FF8000.
        light.Color = ColorSpace.SrgbToLinear(new Vector3(1f, 128f / 255f, 0f));
        level.AddScript(level.Scene.Root, """
            local lamp = workspace.Lamp
            print(lamp.Enabled, lamp.Brightness, lamp.Range, lamp.Color)
            print(lamp.Color == Color3.fromRGB(255, 128, 0), lamp.Color:ToHex(), lamp.ClassName)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["false 2.5 12 1, 0.501961, 0", "true ff8000 SpotLight"]);
    }

    [Fact]
    public void A_light_reads_back_what_a_script_wrote_and_the_engine_holds_it()
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level, LightKind.Point).Light.ShouldNotBeNull();
        level.AddScript(level.Scene.Root, """
            local lamp = workspace.Lamp
            local dim = Color3.new(0.3, 0.02, 0.9)
            lamp.Enabled = false
            lamp.Brightness = 0.75
            lamp.Range = 30
            lamp.Color = dim
            print(lamp.Enabled, lamp.Brightness, lamp.Range, lamp.Color == dim)
            """);

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        level.Printed.ShouldBe(["false 0.75 30 true"]);
        light.Enabled.ShouldBeFalse();
        light.Intensity.ShouldBe(0.75f);
        light.Range.ShouldBe(30f);
        light.Color.ShouldBe(ColorSpace.SrgbToLinear(new Vector3(0.3f, 0.02f, 0.9f)));
    }

    // A light is changed in place and raises nothing. The renderer asks the
    // scene for its lights each frame, so the next one has the change.
    [Fact]
    public void A_light_a_script_switched_off_is_gone_from_the_next_frame_and_back_when_it_is_on()
    {
        using var level = new ScriptLevel();
        Lamp(level, LightKind.Point);
        level.AddScript(level.Scene.Root, """
            local lamp = workspace.Lamp
            task.wait()
            lamp.Enabled = false
            task.wait()
            lamp.Enabled = true
            lamp.Color = Color3.new(1, 0, 0)
            lamp.Brightness = 4
            """);
        level.Play().Tick();
        int lit = Drawn(level).Length;

        level.Tick();
        Drawn(level).Length.ShouldBe(lit - 1);

        level.Tick();
        RenderLight[] drawn = Drawn(level);
        drawn.Length.ShouldBe(lit);
        drawn.ShouldContain(light => light.ColorIntensity == new Vector4(4f, 0f, 0f, (float)(int)RenderLightType.Point));
        level.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("lamp.Brightness = -math.huge", "Brightness cannot be -Infinity: it takes a number that is not infinite.")]
    [InlineData("lamp.Brightness = 0/0", "Brightness cannot be NaN: it takes a number that is not infinite.")]
    [InlineData("lamp.Brightness = math.huge", "Brightness cannot be Infinity: it takes a number that is not infinite.")]
    [InlineData("lamp.Range = 0", "Range cannot be 0: it takes a number above 0 that is not infinite.")]
    [InlineData("lamp.Range = -math.huge", "Range cannot be -Infinity: it takes a number above 0 that is not infinite.")]
    [InlineData(
        "lamp.Color = Color3.new(1, -0.5, 0)",
        "Color cannot be 1, -0.5, 0: each of red, green and blue is a number of 0 or more that is not infinite.")]
    [InlineData(
        "lamp.Color = Color3.new(0/0, 0, 0)",
        "Color cannot be NaN, 0, 0: each of red, green and blue is a number of 0 or more that is not infinite.")]
    [InlineData("lamp.Enabled = 1", "Unable to assign property Enabled. boolean expected, got number")]
    [InlineData("lamp.Enabled = nil", "Unable to assign property Enabled. boolean expected, got nil")]
    [InlineData("lamp.Brightness = true", "Unable to assign property Brightness. number expected, got boolean")]
    [InlineData("lamp.Brightness = 'bright'", "Unable to assign property Brightness. number expected, got string")]
    [InlineData("lamp.Color = Vector3.new(1, 0, 0)", "Unable to assign property Color. Color3 expected, got Vector3")]
    [InlineData("lamp.Color = 'red'", "Unable to assign property Color. Color3 expected, got string")]
    [InlineData("lamp.Range = workspace", "Unable to assign property Range. number expected, got Instance")]
    [InlineData("lamp.Shadows = true", "Shadows is not a valid member of PointLight \"Lamp\"")]
    public void A_light_refuses_what_it_cannot_take_and_says_why(string write, string words)
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level, LightKind.Point).Light.ShouldNotBeNull();
        level.AddScript(level.Scene.Root, $"local lamp = workspace.Lamp\nprint(select(2, pcall(function() {write} end)))");

        level.Play().Tick();

        level.Printed.ShouldHaveSingleItem().ShouldEndWith(words);
        (light.Enabled, light.Intensity, light.Range, light.Color).ShouldBe((true, 1f, 10f, Vector3.One));
    }

    // Roblox takes a string that reads as a number for a number, and so
    // does Luau's own arithmetic.
    [Fact]
    public void A_string_that_reads_as_a_number_is_taken_for_one()
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level, LightKind.Point).Light.ShouldNotBeNull();
        level.AddScript(level.Scene.Root, "workspace.Lamp.Brightness = '2.5'");

        level.Play().Tick();

        level.Errors.ShouldBeEmpty();
        light.Intensity.ShouldBe(2.5f);
    }

    [Fact]
    public void A_range_belongs_to_every_light_but_the_one_that_shines_everywhere()
    {
        using var level = new ScriptLevel();
        foreach (LightKind kind in Enum.GetValues<LightKind>())
            level.Scene.Root.CreateChild(kind.ToString()).Light = new Light { Kind = kind, Range = 6f };

        level.AddScript(level.Scene.Root, """
            for _, name in { "Point", "Spot", "Rect", "Disc", "Directional" } do
                local light = workspace[name]
                print(name, pcall(function() light.Range += 1 return light.Range end))
            end
            print(pcall(function() return workspace.Directional.Range end))
            """);

        level.Play().Tick();

        level.Printed[..4].ShouldBe(["Point true 7", "Spot true 7", "Rect true 7", "Disc true 7"]);
        level.Printed[4].ShouldEndWith("Range is not a valid member of DirectionalLight \"Directional\"");
        level.Printed[5].ShouldEndWith("Range is not a valid member of DirectionalLight \"Directional\"");
    }

    [Fact]
    public void A_part_has_no_member_of_a_light()
    {
        using var level = new ScriptLevel();
        level.Part("Crate", Vector3.Zero);
        level.AddScript(level.Scene.Root, """
            local crate = workspace.Crate
            print(select(2, pcall(function() crate.Brightness = 2 end)))
            print(select(2, pcall(function() return crate.Enabled end)))
            print(select(2, pcall(function() crate.Color = Color3.new(1, 0, 0) end)))
            print(select(2, pcall(function() workspace.Range = 5 end)))
            """);

        level.Play().Tick();

        level.Printed[0].ShouldEndWith("Brightness is not a valid member of Part \"Crate\"");
        level.Printed[1].ShouldEndWith("Enabled is not a valid member of Part \"Crate\"");
        level.Printed[2].ShouldEndWith("Color is not a valid member of Part \"Crate\"");
        level.Printed[3].ShouldEndWith("Range is not a valid member of Folder \"Root\"");
    }

    [Fact]
    public void A_write_of_what_a_light_already_has_records_nothing_and_Stop_puts_back_the_rest()
    {
        using var level = new ScriptLevel();
        Light light = Lamp(level, LightKind.Point).Light.ShouldNotBeNull();
        light.Color = new Vector3(0.8f, 0.3f, -0f);
        level.AddScript(level.Scene.Root, """
            local lamp = workspace.Lamp
            lamp.Enabled, lamp.Brightness, lamp.Range, lamp.Color = lamp.Enabled, lamp.Brightness, lamp.Range, lamp.Color
            task.wait()
            lamp.Enabled, lamp.Brightness, lamp.Range, lamp.Color = false, 9, 99, Color3.new(0, 1, 0)
            """);
        var before = new LevelState(level.Scene);

        level.Play().Tick();
        level.Entities.Journal.Count.ShouldBe(0);
        new LevelState(level.Scene).IsSavedAs(before).ShouldBeTrue();

        level.Tick();
        level.Entities.Journal.Count.ShouldBe(4);
        new LevelState(level.Scene).IsSavedAs(before).ShouldBeFalse();

        level.Stop();

        level.Errors.ShouldBeEmpty();
        new LevelState(level.Scene).ShouldBe(before, "after Stop");
    }

    private static SceneNode Lamp(ScriptLevel level, LightKind kind)
    {
        SceneNode lamp = level.Scene.Root.CreateChild("Lamp");
        lamp.LocalPosition = new Vector3(0f, 3f, 0f);
        lamp.Light = new Light { Kind = kind };
        return lamp;
    }

    // The lights the next frame would draw, as the renderer is handed them.
    private static RenderLight[] Drawn(ScriptLevel level)
    {
        var view = new RenderView();
        level.Scene.BuildRenderView(level.Scene.Camera, view);
        return view.Lights.ToArray();
    }
}
