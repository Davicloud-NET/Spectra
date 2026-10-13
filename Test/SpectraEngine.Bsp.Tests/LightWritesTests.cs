using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a script sets on a light through <see cref="ScriptWrites"/>: what is
/// taken, what is refused and in which words, what the renderer then draws,
/// and that Stop puts every bit back.
/// </summary>
public sealed class LightWritesTests
{
    private readonly Scene _scene = new("Lights");

    [Fact]
    public void Each_member_of_a_light_reads_back_what_was_written()
    {
        SceneNode lamp = Lamp(LightKind.Point);
        Light light = lamp.Light.ShouldNotBeNull();
        var writes = new ScriptWrites(Started());

        Write(writes, lamp, SettableMemberId.Enabled, MemberValue.Of(false));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(2.5));
        Write(writes, lamp, SettableMemberId.Range, MemberValue.Of(40.0));
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(1f, 0.5f, 0.123f)));

        light.Enabled.ShouldBeFalse();
        light.Intensity.ShouldBe(2.5f);
        light.Range.ShouldBe(40f);
        light.Color.ShouldBe(ColorSpace.SrgbToLinear(new Vector3(1f, 0.5f, 0.123f)));
        ScriptWrites.Read(lamp, SettableMemberId.Enabled).ShouldBe(MemberValue.Of(false));
        ScriptWrites.Read(lamp, SettableMemberId.Brightness).ShouldBe(MemberValue.Of(2.5));
        ScriptWrites.Read(lamp, SettableMemberId.Range).ShouldBe(MemberValue.Of(40.0));
        ScriptWrites.Read(lamp, SettableMemberId.Color).ShouldBe(MemberValue.Of(new Color3(1f, 0.5f, 0.123f)));
    }

    [Fact]
    public void Stop_puts_back_every_bit_of_a_light()
    {
        SceneNode lamp = Lamp(LightKind.Spot);
        Light light = lamp.Light.ShouldNotBeNull();
        light.Color = new Vector3(0.8f, -0f, 3f);
        light.Intensity = 0.1f;
        light.Range = 7.3f;
        (bool, uint, uint, Vector3) authored = Bits(light);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        Write(writes, lamp, SettableMemberId.Enabled, MemberValue.Of(false));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(9.0));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(0.0));
        Write(writes, lamp, SettableMemberId.Range, MemberValue.Of(1.0));
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(0f, 0f, 0f)));
        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(0.2f, 0.9f, 1f)));
        world.Journal.Count.ShouldBe(4);

        world.Deactivate();

        Bits(light).ShouldBe(authored);
        BitConverter.SingleToUInt32Bits(light.Color.Y).ShouldBe(BitConverter.SingleToUInt32Bits(-0f));
        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_write_of_the_value_a_light_already_has_records_nothing_and_changes_nothing()
    {
        SceneNode lamp = Lamp(LightKind.Point);
        Light light = lamp.Light.ShouldNotBeNull();

        // No colour a script holds becomes this linear one.
        light.Color = new Vector3(0.8f, 0.3f, 0.05f);
        (bool, uint, uint, Vector3) authored = Bits(light);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        foreach (SettableMemberId member in new[]
                 {
                     SettableMemberId.Enabled, SettableMemberId.Brightness, SettableMemberId.Range, SettableMemberId.Color,
                 })
        {
            Write(writes, lamp, member, ScriptWrites.Read(lamp, member));
        }

        world.Journal.Count.ShouldBe(0);
        Bits(light).ShouldBe(authored);
    }

    // The editor takes any brightness of 0 or more, so a level can hold one
    // no script may set. Writing back what was read is still no error.
    [Fact]
    public void A_colour_out_of_range_that_the_light_already_has_is_taken()
    {
        SceneNode lamp = Lamp(LightKind.Point);
        lamp.Light.ShouldNotBeNull().Color = new Vector3(-0.5f, 1f, 1f);
        EntityWorld world = Started();

        Write(new ScriptWrites(world), lamp, SettableMemberId.Color, ScriptWrites.Read(lamp, SettableMemberId.Color));

        world.Journal.Count.ShouldBe(0);
    }

    // What Roblox Studio answered: light.Brightness = -3 reads back 0, with
    // no error.
    [Theory]
    [InlineData(-3.0)]
    [InlineData(-0.001)]
    [InlineData(-0.0)]
    [InlineData(-1e30)]
    public void A_brightness_below_zero_is_taken_as_zero_as_Studio_takes_it(double value)
    {
        SceneNode lamp = Lamp(LightKind.Point);
        Light light = lamp.Light.ShouldNotBeNull();
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);

        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(value));

        light.Intensity.ShouldBe(0f);
        BitConverter.SingleToInt32Bits(light.Intensity).ShouldBe(0, "minus zero is zero too");
        ScriptWrites.Read(lamp, SettableMemberId.Brightness).ShouldBe(MemberValue.Of(0.0));
        world.Journal.Count.ShouldBe(1);

        // A light that is dark already records nothing for another such write.
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(-7.0));
        world.Journal.Count.ShouldBe(1);

        world.Deactivate();
        light.Intensity.ShouldBe(1f);
    }

    [Theory]
    [InlineData(double.NaN, "Brightness cannot be NaN: it takes a number that is not infinite.")]
    [InlineData(double.PositiveInfinity, "Brightness cannot be Infinity: it takes a number that is not infinite.")]
    [InlineData(double.NegativeInfinity, "Brightness cannot be -Infinity: it takes a number that is not infinite.")]
    [InlineData(1e300, "Brightness cannot be Infinity: it takes a number that is not infinite.")]
    public void A_brightness_that_is_no_number_or_infinite_is_refused(double value, string words)
    {
        SceneNode lamp = Lamp(LightKind.Point);
        EntityWorld world = Started();

        new ScriptWrites(world).TryWrite(lamp, SettableMemberId.Brightness, MemberValue.Of(value), out string? refusal)
            .ShouldBeFalse();

        refusal.ShouldBe(words);
        lamp.Light.ShouldNotBeNull().Intensity.ShouldBe(1f);
        world.Journal.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(0.0, "Range cannot be 0: it takes a number above 0 that is not infinite.")]
    [InlineData(-3.5, "Range cannot be -3.5: it takes a number above 0 that is not infinite.")]
    [InlineData(double.NaN, "Range cannot be NaN: it takes a number above 0 that is not infinite.")]
    [InlineData(double.NegativeInfinity, "Range cannot be -Infinity: it takes a number above 0 that is not infinite.")]
    public void A_range_that_is_not_above_zero_is_refused(double value, string words)
    {
        SceneNode lamp = Lamp(LightKind.Disc);
        EntityWorld world = Started();

        new ScriptWrites(world).TryWrite(lamp, SettableMemberId.Range, MemberValue.Of(value), out string? refusal)
            .ShouldBeFalse();

        refusal.ShouldBe(words);
        lamp.Light.ShouldNotBeNull().Range.ShouldBe(10f);
        world.Journal.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(-0.1f, 0f, 0f, "Color cannot be -0.1, 0, 0")]
    [InlineData(0f, float.NaN, 0f, "Color cannot be 0, NaN, 0")]
    [InlineData(0f, 0f, float.PositiveInfinity, "Color cannot be 0, 0, Infinity")]
    public void A_colour_with_a_part_below_zero_or_no_number_is_refused(float r, float g, float b, string start)
    {
        SceneNode lamp = Lamp(LightKind.Point);
        EntityWorld world = Started();

        new ScriptWrites(world)
            .TryWrite(lamp, SettableMemberId.Color, MemberValue.Of(new Color3(r, g, b)), out string? refusal)
            .ShouldBeFalse();

        refusal.ShouldBe(start + ": each of red, green and blue is a number of 0 or more that is not infinite.");
        lamp.Light.ShouldNotBeNull().Color.ShouldBe(Vector3.One);
        world.Journal.Count.ShouldBe(0);
    }

    [Fact]
    public void A_colour_part_above_one_is_taken_as_a_brighter_light()
    {
        SceneNode lamp = Lamp(LightKind.Point);

        Write(new ScriptWrites(Started()), lamp, SettableMemberId.Color, MemberValue.Of(new Color3(2f, 1f, 1f)));

        lamp.Light.ShouldNotBeNull().Color.X.ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused_in_Robloxs_words()
    {
        SceneNode lamp = Lamp(LightKind.Point);
        var writes = new ScriptWrites(Started());

        writes.TryWrite(lamp, SettableMemberId.Enabled, MemberValue.Of(1.0), out string? first).ShouldBeFalse();
        writes.TryWrite(lamp, SettableMemberId.Brightness, MemberValue.Of("bright"), out string? second).ShouldBeFalse();
        writes.TryWrite(lamp, SettableMemberId.Color, MemberValue.Of(Vector3.One), out string? third).ShouldBeFalse();

        first.ShouldBe("Unable to assign property Enabled. boolean expected, got number");
        second.ShouldBe("Unable to assign property Brightness. number expected, got string");
        third.ShouldBe("Unable to assign property Color. Color3 expected, got Vector3");
    }

    [Fact]
    public void A_directional_light_has_no_range_and_a_part_has_no_member_of_a_light()
    {
        SceneNode sun = Lamp(LightKind.Directional);
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        var writes = new ScriptWrites(Started());

        writes.TryWrite(sun, SettableMemberId.Range, MemberValue.Of(5.0), out string? ofTheSun).ShouldBeFalse();
        writes.TryWrite(part, SettableMemberId.Brightness, MemberValue.Of(5.0), out string? ofThePart).ShouldBeFalse();

        ofTheSun.ShouldBe("Range is not a valid member of DirectionalLight \"Lamp\"");
        ofThePart.ShouldBe("Brightness is not a valid member of Part \"Crate\"");
        ScriptWrites.Read(sun, SettableMemberId.Range).ShouldBe(MemberValue.None);
        ScriptWrites.Has(sun, SettableMemberId.Brightness).ShouldBeTrue();
        ScriptWrites.Has(part, SettableMemberId.Enabled).ShouldBeFalse();
    }

    // A light is changed in place and raises no signal. The renderer asks
    // the scene for its lights on every frame, and that is how it learns.
    [Fact]
    public void The_next_frame_draws_a_light_as_a_script_left_it()
    {
        SceneNode lamp = Lamp(LightKind.Point);
        var writes = new ScriptWrites(Started());
        var view = new RenderView();
        _scene.BuildRenderView(_scene.Camera, view);
        view.LightCount.ShouldBe(1);

        Write(writes, lamp, SettableMemberId.Color, MemberValue.Of(new Color3(1f, 0f, 0f)));
        Write(writes, lamp, SettableMemberId.Brightness, MemberValue.Of(3.0));
        Write(writes, lamp, SettableMemberId.Range, MemberValue.Of(25.0));
        _scene.BuildRenderView(_scene.Camera, view);

        RenderLight drawn = view.Lights[0];
        new Vector3(drawn.ColorIntensity.X, drawn.ColorIntensity.Y, drawn.ColorIntensity.Z).ShouldBe(new Vector3(3f, 0f, 0f));
        drawn.PositionRange.W.ShouldBe(25f);

        Write(writes, lamp, SettableMemberId.Enabled, MemberValue.Of(false));
        _scene.BuildRenderView(_scene.Camera, view);

        view.LightCount.ShouldBe(0);
    }

    private SceneNode Lamp(LightKind kind)
    {
        SceneNode lamp = _scene.Root.CreateChild("Lamp");
        lamp.Light = new Light { Kind = kind };
        return lamp;
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        return world;
    }

    // Every bit a script can change, so -0 and 0 cannot pass as equal.
    private static (bool, uint, uint, Vector3) Bits(Light light) => (
        light.Enabled,
        BitConverter.SingleToUInt32Bits(light.Intensity),
        BitConverter.SingleToUInt32Bits(light.Range),
        light.Color);

    private static void Write(ScriptWrites writes, SceneNode node, SettableMemberId member, MemberValue value) =>
        writes.TryWrite(node, member, in value, out string? refusal).ShouldBeTrue(refusal);
}
