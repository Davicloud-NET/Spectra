using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="WallLoss"/>: the solids on one line, added up into what they
/// take from a sound.
/// </summary>
public sealed class WallLossTests
{
    private const float Length = 10f;

    private static readonly MaterialRef Wood = MaterialRegistry.Intern("Materials/loss_wood.spectramat");
    private static readonly MaterialRef Concrete = MaterialRegistry.Intern("Materials/loss_concrete.spectramat");

    private static readonly FakeAcousticMaterials Materials = new FakeAcousticMaterials()
        .Add(Wood, AcousticPresets.Wood)
        .Add(Concrete, AcousticPresets.Concrete);

    [Fact]
    public void A_line_through_nothing_loses_nothing()
    {
        WallLoss.Sum([], Length, Materials).ShouldBe(default);
    }

    [Fact]
    public void One_wall_costs_what_its_material_takes_at_its_thickness()
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(4f, 4.05f, Wood)], Length, Materials);

        AcousticLoss table = AcousticPresets.Wood.LossThrough(0.05f);
        loss.Db.ShouldBe(table.Db, 1e-4f);
        loss.HfDb.ShouldBe(table.HfDb, 1e-4f);
    }

    [Fact]
    public void Walls_one_after_the_other_add_up()
    {
        SolidSpan[] spans = [new(2f, 2.05f, Wood), new(5f, 6f, Concrete), new(8f, 8.05f, Wood)];

        AcousticLoss loss = WallLoss.Sum(spans, Length, Materials);

        AcousticLoss table = AcousticPresets.Wood.LossThrough(0.05f)
            + AcousticPresets.Concrete.LossThrough(1f)
            + AcousticPresets.Wood.LossThrough(0.05f);
        loss.Db.ShouldBe(table.Db, 1e-3f);
        loss.HfDb.ShouldBe(table.HfDb, 1e-3f);
    }

    [Fact]
    public void A_material_nobody_named_counts_as_generic()
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(4f, 4.5f, MaterialRef.Default)], Length, Materials);

        loss.ShouldBe(AcousticPresets.Generic.LossThrough(0.5f));
    }

    [Theory]
    [InlineData(0f, 0.02f)]
    [InlineData(9.98f, 10f)]
    public void A_solid_the_line_starts_or_ends_just_inside_costs_next_to_nothing(float start, float end)
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(start, end, Concrete)], Length, Materials);

        // Two centimetres of a quarter metre: that share of the surface, and
        // the two centimetres themselves.
        loss.Db.ShouldBe((10f * 0.08f) + (25f * 0.02f), 1e-3f);
        loss.HfDb.ShouldBe((13.5f * 0.08f) + (33.5f * 0.02f), 1e-3f);
        loss.ToGains().Gain.ShouldBeGreaterThan(0.85f);
    }

    [Theory]
    [InlineData(0f, 0.25f)]
    [InlineData(0f, 0.4f)]
    [InlineData(9.6f, 10f)]
    public void A_solid_the_line_starts_or_ends_deep_inside_costs_all_of_its_surface(float start, float end)
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(start, end, Concrete)], Length, Materials);

        AcousticLoss whole = AcousticPresets.Concrete.LossThrough(end - start);
        loss.Db.ShouldBe(whole.Db, 1e-3f);
        loss.HfDb.ShouldBe(whole.HfDb, 1e-3f);
    }

    [Fact]
    public void A_wall_in_the_middle_of_the_line_costs_all_of_its_surface_however_thin()
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(5f, 5.02f, Concrete)], Length, Materials);

        loss.Db.ShouldBe(AcousticPresets.Concrete.LossThrough(0.02f).Db, 1e-4f);
    }

    [Fact]
    public void A_line_that_lies_wholly_inside_one_solid_counts_it_by_its_length()
    {
        AcousticLoss loss = WallLoss.Sum([new SolidSpan(0f, 0.1f, Concrete)], length: 0.1f, Materials);

        loss.Db.ShouldBe((10f * 0.4f) + (25f * 0.1f), 1e-3f);
    }
}
