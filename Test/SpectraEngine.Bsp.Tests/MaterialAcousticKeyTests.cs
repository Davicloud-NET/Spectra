using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio.Acoustics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The <c>acoustic</c> key of a <c>.spectramat</c>: what a surface is made of,
/// for sound.
/// </summary>
public sealed class MaterialAcousticKeyTests
{
    [Fact]
    public void The_key_names_a_preset()
    {
        MaterialDefinition definition = MaterialParser.Parse("acoustic = wood", "door.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldBeSameAs(AcousticPresets.Wood);
    }

    [Fact]
    public void Every_preset_can_be_named()
    {
        foreach (AcousticPreset preset in AcousticPresets.All)
        {
            MaterialDefinition definition = MaterialParser.Parse($"acoustic = {preset.Name}", "any.spectramat");

            definition.Warnings.ShouldBeEmpty(Describe(definition));
            definition.Acoustic.ShouldBeSameAs(preset);
        }
    }

    [Theory]
    [InlineData("acoustic = Wood")]
    [InlineData("acoustic = WOOD")]
    [InlineData("ACOUSTIC = wood")]
    [InlineData("Acoustic=wOOd")]
    public void Case_does_not_matter_and_the_name_is_kept_in_lower_case(string line)
    {
        MaterialDefinition definition = MaterialParser.Parse(line, "door.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldNotBeNull().Name.ShouldBe("wood");
    }

    [Fact]
    public void A_file_without_the_line_has_no_preset()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            shader = lit
            float uRoughness = 0.8
            """, "plain.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldBeNull();
    }

    [Fact]
    public void Naming_generic_is_not_the_same_as_leaving_the_line_out()
    {
        MaterialDefinition named = MaterialParser.Parse("acoustic = generic", "named.spectramat");
        MaterialDefinition silent = MaterialParser.Parse("shader = lit", "silent.spectramat");

        named.Acoustic.ShouldBeSameAs(AcousticPresets.Generic);
        silent.Acoustic.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_name_warns_with_its_file_and_line_and_leaves_no_preset()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            shader = lit

            acoustic = wod
            float uRoughness = 0.8
            """, "Materials/door.spectramat");

        string warning = definition.Warnings.ShouldHaveSingleItem();
        warning.ShouldStartWith("Materials/door.spectramat(3): ");
        warning.ShouldContain("unknown acoustic preset 'wod'");
        warning.ShouldContain(AcousticPresets.NameList);

        definition.Acoustic.ShouldBeNull();
        definition.ShaderName.ShouldBe("lit");
        definition.TryGetParameter("uRoughness", out _).ShouldBeTrue();
    }

    [Fact]
    public void A_line_with_no_value_warns_and_leaves_no_preset()
    {
        MaterialDefinition definition = MaterialParser.Parse("acoustic =", "empty.spectramat");

        definition.Warnings.ShouldHaveSingleItem().ShouldBe("empty.spectramat(1): 'acoustic' has no value; ignored");
        definition.Acoustic.ShouldBeNull();
    }

    [Fact]
    public void Comments_and_blank_lines_around_the_line_do_not_matter()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            // A window.

            // acoustic = brick
              acoustic   =   glass   // the pane, not the frame

            """, "window.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldBeSameAs(AcousticPresets.Glass);
    }

    [Theory]
    [InlineData("shader = lit\nacoustic = metal\ntexture uDiffuse = Textures/white.png")]
    [InlineData("acoustic = metal\nshader = lit\ntexture uDiffuse = Textures/white.png")]
    [InlineData("texture uDiffuse = Textures/white.png\nshader = lit\nacoustic = metal")]
    public void The_shader_line_can_stand_before_or_after_it(string source)
    {
        MaterialDefinition definition = MaterialParser.Parse(source, "plate.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldBeSameAs(AcousticPresets.Metal);
        definition.ShaderName.ShouldBe("lit");
        definition.Textures.ShouldHaveSingleItem().Name.ShouldBe("uDiffuse");
    }

    [Fact]
    public void A_second_line_warns_and_replaces_the_first_as_a_second_shader_line_does()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            shader = lit
            acoustic = wood
            shader = unlit
            acoustic = Brick
            """, "twice.spectramat");

        definition.Warnings.Count.ShouldBe(2, Describe(definition));
        definition.Warnings.ShouldContain("twice.spectramat(3): 'shader' set again; 'unlit' replaces 'lit'");
        definition.Warnings.ShouldContain("twice.spectramat(4): 'acoustic' set again; 'brick' replaces 'wood'");

        definition.ShaderName.ShouldBe("unlit");
        definition.Acoustic.ShouldBeSameAs(AcousticPresets.Brick);
    }

    [Fact]
    public void A_second_line_that_names_nothing_leaves_the_first()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            acoustic = wood
            acoustic = wod
            acoustic =
            """, "typo.spectramat");

        definition.Warnings.Count.ShouldBe(2, Describe(definition));
        definition.Acoustic.ShouldBeSameAs(AcousticPresets.Wood);
    }

    [Fact]
    public void The_key_reaches_no_shader()
    {
        MaterialDefinition definition = MaterialParser.Parse("acoustic = concrete", "slab.spectramat");

        definition.Parameters.ShouldBeEmpty();
        definition.Textures.ShouldBeEmpty();
        definition.ShaderName.ShouldBeNull();
    }

    [Fact]
    public void A_typed_value_called_acoustic_is_a_shader_parameter_and_names_no_preset()
    {
        MaterialDefinition definition = MaterialParser.Parse("float acoustic = 3", "uniform.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.Acoustic.ShouldBeNull();
        definition.TryGetParameter("acoustic", out MaterialParameter parameter).ShouldBeTrue();
        parameter.AsFloat.ShouldBe(3f);
    }

    private static string Describe(MaterialDefinition definition)
        => definition.Warnings.Count == 0
            ? "(no warnings)"
            : string.Join(Environment.NewLine, definition.Warnings);
}
