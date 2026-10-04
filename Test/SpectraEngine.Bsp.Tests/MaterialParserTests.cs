using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Graphics;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The <c>.spectramat</c> parser on its own. It never throws: anything it does
/// not understand lands in <see cref="MaterialDefinition.Warnings"/>.
/// </summary>
public sealed class MaterialParserTests
{
    [Fact]
    public void Parses_every_field_kind()
    {
        const string source = """
            // A comment line, and a blank one below.

            shader = lit

            texture uDiffuse = Textures/wall_brick.png, nearest, clamp
            texture uMask    = Textures/gradient_mask.png   // options are optional
            float   uRoughness = 0.25
            color   uBaseColor = #8040FF
            color   uTint      = 0.1 0.2 0.3 0.4
            vec2    uTiling    = 4 8
            vec3    uEmissive  = 0.5, 0.25, 0.125
            vec4    uParams    = 1 0 0 1
            """;

        MaterialDefinition definition = MaterialParser.Parse(source, "test.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        definition.ShaderName.ShouldBe("lit");

        // Units follow declaration order.
        definition.Textures.Count.ShouldBe(2);
        definition.TryGetTextureSlot("uDiffuse", out MaterialTextureSlot diffuse).ShouldBeTrue();
        diffuse.TexturePath.ShouldBe("Textures/wall_brick.png");
        diffuse.Unit.ShouldBe(0);
        diffuse.Filter.ShouldBe(TextureFilter.Nearest);
        diffuse.Wrap.ShouldBe(TextureWrap.Clamp);
        diffuse.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

        definition.TryGetTextureSlot("uMask", out MaterialTextureSlot mask).ShouldBeTrue();
        mask.TexturePath.ShouldBe("Textures/gradient_mask.png");
        mask.Unit.ShouldBe(1);
        // Same defaults as AssetManager.LoadTexture.
        mask.Filter.ShouldBe(TextureFilter.LinearMipmap);
        mask.Wrap.ShouldBe(TextureWrap.Repeat);
        mask.ColorSpace.ShouldBe(TextureColorSpace.Srgb);

        definition.Parameters.Count.ShouldBe(6);
        ShouldBe(definition, "uRoughness", MaterialParameterKind.Float, new Vector4(0.25f, 0f, 0f, 0f));
        // 'color' is authored in sRGB and stored linear.
        ShouldBe(definition, "uBaseColor", MaterialParameterKind.Vector3,
            ColorSpace.SrgbToLinear(new Vector4(128 / 255f, 64 / 255f, 1f, 0f)));
        // Alpha is not converted.
        ShouldBe(definition, "uTint", MaterialParameterKind.Vector4,
            ColorSpace.SrgbToLinear(new Vector4(0.1f, 0.2f, 0.3f, 0.4f)));
        definition.TryGetParameter("uTint", out MaterialParameter tint).ShouldBeTrue();
        tint.Value.W.ShouldBe(0.4f);

        // vecN are plain numbers and pass through unconverted.
        ShouldBe(definition, "uTiling", MaterialParameterKind.Vector2, new Vector4(4f, 8f, 0f, 0f));
        ShouldBe(definition, "uEmissive", MaterialParameterKind.Vector3, new Vector4(0.5f, 0.25f, 0.125f, 0f));
        ShouldBe(definition, "uParams", MaterialParameterKind.Vector4, new Vector4(1f, 0f, 0f, 1f));
    }

    [Fact]
    public void Empty_and_comment_only_files_parse_to_an_empty_definition()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            // nothing but comments

            """, "empty.spectramat");

        definition.Warnings.ShouldBeEmpty(Describe(definition));
        // Null means the built-in lit shader.
        definition.ShaderName.ShouldBeNull();
        definition.Textures.ShouldBeEmpty();
        definition.Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_key_warns_and_the_rest_of_the_file_still_loads()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            doubleSided = true
            reflections = 4
            color uBaseColor = 1 1 1
            """, "future.spectramat");

        definition.Warnings.Count.ShouldBe(2, Describe(definition));
        definition.Warnings.ShouldContain(w => w.Contains("doubleSided") && w.Contains("unknown key"));
        definition.Warnings.ShouldAllBe(w => w.StartsWith("future.spectramat("));
        definition.TryGetParameter("uBaseColor", out _).ShouldBeTrue();
    }

    [Fact]
    public void Unknown_parameter_kind_and_unknown_texture_option_warn_without_losing_the_directive_line()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            mat4    uWeird   = 1 2 3 4
            texture uDiffuse = Textures/dev_grid.png, trilinear
            """, "odd.spectramat");

        definition.Warnings.ShouldContain(w => w.Contains("unknown parameter kind 'mat4'"));
        definition.Warnings.ShouldContain(w => w.Contains("unknown option 'trilinear'"));

        definition.TryGetTextureSlot("uDiffuse", out MaterialTextureSlot slot).ShouldBeTrue();
        slot.Filter.ShouldBe(TextureFilter.LinearMipmap);
        slot.ColorSpace.ShouldBe(TextureColorSpace.Srgb);
        definition.Parameters.ShouldBeEmpty();
    }

    [Fact]
    public void Malformed_values_warn_and_are_skipped()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            float uA = notanumber
            vec3  uB = 1 2
            color uC = #12345
            color uD = 1 2
            texture uE =
            uF
            = 3
            vec2 too many names = 1 2
            float uOk = 2.5
            """, "bad.spectramat");

        definition.Warnings.Count.ShouldBe(8, Describe(definition));
        definition.Warnings.ShouldContain(w => w.Contains("uA") && w.Contains("not a list of up to 4 numbers"));
        definition.Warnings.ShouldContain(w => w.Contains("vec3 uB") && w.Contains("needs 3"));
        definition.Warnings.ShouldContain(w => w.Contains("uC") && w.Contains("#RRGGBB"));
        definition.Warnings.ShouldContain(w => w.Contains("color uD") && w.Contains("3 or 4"));
        definition.Warnings.ShouldContain(w => w.Contains("uE") && w.Contains("no file path"));
        definition.Warnings.ShouldContain(w => w.Contains("expected 'name = value'"));
        definition.Warnings.ShouldContain(w => w.Contains("no name before '='"));
        definition.Warnings.ShouldContain(w => w.Contains("more than a kind and a name"));

        definition.Parameters.ShouldHaveSingleItem().Name.ShouldBe("uOk");
        definition.Textures.ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_declarations_warn_and_the_last_one_wins()
    {
        MaterialDefinition definition = MaterialParser.Parse("""
            shader = lit
            texture uDiffuse = Textures/a.png
            texture uOther   = Textures/b.png
            float   uScale   = 1
            texture uDiffuse = Textures/c.png, nearest
            float   uScale   = 2
            shader = unlit
            """, "dupes.spectramat");

        definition.Warnings.Count.ShouldBe(3, Describe(definition));
        definition.ShaderName.ShouldBe("unlit");

        definition.Textures.Count.ShouldBe(2);
        definition.TryGetTextureSlot("uDiffuse", out MaterialTextureSlot diffuse).ShouldBeTrue();
        diffuse.TexturePath.ShouldBe("Textures/c.png");
        diffuse.Filter.ShouldBe(TextureFilter.Nearest);
        // Keeps its unit; renumbering would shift every later sampler.
        diffuse.Unit.ShouldBe(0);
        definition.TryGetTextureSlot("uOther", out MaterialTextureSlot other).ShouldBeTrue();
        other.Unit.ShouldBe(1);

        definition.TryGetParameter("uScale", out MaterialParameter scale).ShouldBeTrue();
        scale.AsFloat.ShouldBe(2f);
    }

    [Fact]
    public void Numbers_parse_with_the_invariant_culture()
    {
        // de-DE uses a decimal comma.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            MaterialDefinition definition = MaterialParser.Parse("vec3 uC = 0.5, 1.25, 2", "culture.spectramat");

            definition.Warnings.ShouldBeEmpty(Describe(definition));
            definition.TryGetParameter("uC", out MaterialParameter c).ShouldBeTrue();
            c.AsVector3.ShouldBe(new Vector3(0.5f, 1.25f, 2f));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Every_shipped_material_file_parses_cleanly()
    {
        string folder = Path.Combine(ContentRoot.Path, "Materials");
        string[] files = Directory.GetFiles(folder, "*" + MaterialParser.FileExtension);

        files.ShouldNotBeEmpty($"no material files found in {folder}");
        foreach (string file in files)
        {
            MaterialDefinition definition = MaterialParser.ParseFile(file);
            definition.Warnings.ShouldBeEmpty($"{file}: {Describe(definition)}");
            definition.Textures.ShouldNotBeEmpty(file);

            foreach (MaterialTextureSlot slot in definition.Textures)
            {
                string texture = ContentRoot.ResolveAbsolute(ContentRoot.Path, slot.TexturePath);
                File.Exists(texture).ShouldBeTrue($"{file} references a missing texture: {slot.TexturePath}");
            }
        }
    }

    private static void ShouldBe(
        MaterialDefinition definition, string name, MaterialParameterKind kind, Vector4 value)
    {
        definition.TryGetParameter(name, out MaterialParameter parameter)
            .ShouldBeTrue($"'{name}' was not parsed");
        parameter.Kind.ShouldBe(kind, name);
        // Exact: these literals round-trip through float.Parse.
        parameter.Value.ShouldBe(value, name);
    }

    private static string Describe(MaterialDefinition definition)
        => definition.Warnings.Count == 0
            ? "(no warnings)"
            : string.Join(Environment.NewLine, definition.Warnings);
}
