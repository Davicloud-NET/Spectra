using SpectraEngine.Core.Audio.Acoustics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The table of acoustic presets. The numbers are tuning data, so these pin
/// the order of things and not the values.
/// </summary>
public sealed class AcousticPresetsTests
{
    // Metres. A sheet, a door, a wall, a thick wall, a bunker, a hillside.
    private static readonly float[] Thicknesses = [0f, 0.02f, 0.05f, 0.1f, 0.2f, 0.5f, 1f, 3f, 20f];

    public static TheoryData<string> Names()
    {
        var names = new TheoryData<string>();
        foreach (AcousticPreset preset in AcousticPresets.All)
            names.Add(preset.Name);
        return names;
    }

    [Fact]
    public void The_table_holds_the_presets_a_material_can_name()
    {
        string[] names = ["generic", "fabric", "plaster", "wood", "glass", "metal", "brick", "concrete", "rock"];

        AcousticPresets.All.Select(preset => preset.Name).ShouldBe(names);
        AcousticPresets.NameList.ShouldBe(string.Join(", ", names));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_preset_is_found_by_its_name_in_any_case(string name)
    {
        AcousticPresets.TryFind(name, out AcousticPreset? exact).ShouldBeTrue();
        AcousticPresets.TryFind(name.ToUpperInvariant(), out AcousticPreset? shouted).ShouldBeTrue();

        exact.ShouldNotBeNull().Name.ShouldBe(name);
        shouted.ShouldBeSameAs(exact);
        name.ShouldBe(name.ToLowerInvariant());
    }

    [Fact]
    public void A_name_the_table_does_not_hold_is_not_found()
    {
        AcousticPresets.TryFind("wod", out AcousticPreset? preset).ShouldBeFalse();
        preset.ShouldBeNull();

        AcousticPresets.TryFind("", out _).ShouldBeFalse();
        AcousticPresets.TryFind("wood door", out _).ShouldBeFalse();
    }

    [Fact]
    public void Thin_wood_passes_more_than_a_metre_of_concrete_in_both_bands()
    {
        AcousticGains door = AcousticPresets.Wood.GainsThrough(0.04f);
        AcousticGains bunker = AcousticPresets.Concrete.GainsThrough(1f);

        door.Gain.ShouldBeGreaterThan(bunker.Gain);
        door.GainHf.ShouldBeGreaterThan(bunker.GainHf);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_high_end_never_comes_through_better_than_the_rest(string name)
    {
        AcousticPreset preset = Find(name);

        preset.SurfaceHfDb.ShouldBeGreaterThanOrEqualTo(0f);
        preset.PerMeterHfDb.ShouldBeGreaterThanOrEqualTo(0f);

        foreach (float thickness in Thicknesses)
        {
            AcousticGains gains = preset.GainsThrough(thickness);

            gains.GainHf.ShouldBeLessThanOrEqualTo(1f, $"{name} at {thickness} m");
            gains.Gain.ShouldBeLessThanOrEqualTo(1f, $"{name} at {thickness} m");
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_thicker_wall_never_passes_more(string name)
    {
        AcousticPreset preset = Find(name);
        AcousticGains thinner = preset.GainsThrough(0f);

        for (float thickness = 0.01f; thickness < 5f; thickness += 0.01f)
        {
            AcousticGains thicker = preset.GainsThrough(thickness);

            thicker.Gain.ShouldBeLessThanOrEqualTo(thinner.Gain, $"{name} at {thickness} m");
            thicker.GainHf.ShouldBeLessThanOrEqualTo(thinner.GainHf, $"{name} at {thickness} m");
            thinner = thicker;
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_wall_of_no_thickness_still_costs_its_surface(string name)
    {
        AcousticPreset preset = Find(name);

        preset.LossThrough(0f).ShouldBe(new AcousticLoss(preset.SurfaceDb, preset.SurfaceHfDb));

        AcousticGains gains = preset.GainsThrough(0f);
        gains.Gain.ShouldBeLessThan(1f);
        gains.GainHf.ShouldBeLessThan(1f);

        // Nothing is thinner than nothing.
        preset.GainsThrough(-1f).ShouldBe(gains);
        preset.GainsThrough(float.NaN).ShouldBe(gains);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Nothing_is_silenced_however_thick(string name)
    {
        AcousticGains gains = Find(name).GainsThrough(float.PositiveInfinity);

        gains.Gain.ShouldBe(MathF.Pow(10f, -AcousticLoss.MaxDb / 20f));
        gains.GainHf.ShouldBe(MathF.Pow(10f, -AcousticLoss.MaxHfDb / 20f));
        gains.Gain.ShouldBeGreaterThan(0f);
        gains.GainHf.ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void Fabric_passes_the_most_and_rock_the_least()
    {
        foreach (float thickness in Thicknesses)
        {
            AcousticGains fabric = AcousticPresets.Fabric.GainsThrough(thickness);
            AcousticGains rock = AcousticPresets.Rock.GainsThrough(thickness);

            foreach (AcousticPreset preset in AcousticPresets.All)
            {
                AcousticGains gains = preset.GainsThrough(thickness);
                string where = $"{preset.Name} at {thickness} m";

                gains.Gain.ShouldBeLessThanOrEqualTo(fabric.Gain, where);
                gains.GainHf.ShouldBeLessThanOrEqualTo(fabric.GainHf, where);
                gains.Gain.ShouldBeGreaterThanOrEqualTo(rock.Gain, where);
                gains.GainHf.ShouldBeGreaterThanOrEqualTo(rock.GainHf, where);
            }
        }
    }

    [Fact]
    public void A_material_with_no_line_sits_between_wood_and_concrete()
    {
        foreach (float thickness in Thicknesses)
        {
            AcousticGains generic = AcousticPresets.Generic.GainsThrough(thickness);
            AcousticGains wood = AcousticPresets.Wood.GainsThrough(thickness);
            AcousticGains concrete = AcousticPresets.Concrete.GainsThrough(thickness);

            generic.Gain.ShouldBeInRange(concrete.Gain, wood.Gain);
            generic.GainHf.ShouldBeInRange(concrete.GainHf, wood.GainHf);
        }
    }

    [Fact]
    public void Walls_along_a_path_add_up_before_they_become_gains()
    {
        AcousticLoss door = AcousticPresets.Wood.LossThrough(0.05f);
        AcousticLoss pane = AcousticPresets.Glass.LossThrough(0.01f);

        AcousticLoss both = door + pane;

        both.Db.ShouldBe(door.Db + pane.Db);
        both.HfDb.ShouldBe(door.HfDb + pane.HfDb);

        // Decibels add where gains multiply.
        AcousticGains gains = both.ToGains();
        gains.Gain.ShouldBe(door.ToGains().Gain * pane.ToGains().Gain, 1e-5f);
        gains.GainHf.ShouldBe(door.ToGains().GainHf * pane.ToGains().GainHf, 1e-5f);
    }

    [Fact]
    public void A_loss_becomes_the_gain_its_decibels_say()
    {
        AcousticGains gains = new AcousticLoss(20f, 6.0206f).ToGains();

        gains.Gain.ShouldBe(0.1f, 1e-6f);
        gains.GainHf.ShouldBe(0.5f, 1e-5f);
    }

    [Fact]
    public void A_loss_that_is_negative_or_not_a_number_is_no_loss()
    {
        new AcousticLoss(0f, 0f).ToGains().ShouldBe(new AcousticGains(1f, 1f));
        new AcousticLoss(-6f, -6f).ToGains().ShouldBe(new AcousticGains(1f, 1f));
        new AcousticLoss(float.NaN, float.NaN).ToGains().ShouldBe(new AcousticGains(1f, 1f));
    }

    private static AcousticPreset Find(string name)
    {
        AcousticPresets.TryFind(name, out AcousticPreset? preset).ShouldBeTrue(name);
        return preset.ShouldNotBeNull();
    }
}
