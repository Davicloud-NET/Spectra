using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Images;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The material cook: source text packed verbatim, with every texture and
/// shader it names resolved against the project.
/// </summary>
public class MaterialRuleTests
{
    private const string MaterialPath = "Materials/wall.spectramat";
    private const string TexturePath = "Textures/wall_brick.png";
    private const string CookedTexturePath = "Textures/wall_brick.simage";

    private const string Body =
        "shader = lit\ntexture uDiffuse = Textures/wall_brick.png, linearmipmap, repeat\n";

    [Fact]
    public void A_material_is_packed_verbatim_and_carries_the_material_entry_kind()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 1));
        byte[] source = project.WriteAsset(MaterialPath, Body);

        CookResult result = Cook(project);

        CookedAsset asset = result.Assets.Single(a => a.SourcePath == MaterialPath);
        asset.Rule.ShouldBe(RuleKind.Material);

        asset.Outputs.Single().Path.ShouldBe(MaterialPath);

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.TryOpen(MaterialPath, out ContentBlob? blob).ShouldBeTrue();
        using (blob)
        {
            // Verbatim, so keys the parser does not know survive the cook.
            blob.Span.ToArray().ShouldBe(source);
        }

        PackContents contents = PackContents.Read(result.OutputPath!);
        int index = Enumerable.Range(0, contents.Entries.Count)
            .Single(i => contents.NameOf(i) == MaterialPath);

        contents.Entries[index].EntryKind.ShouldBe(PackEntryKind.Material);
    }

    [Fact]
    public void A_texture_reference_resolves_through_the_simage_redirection()
    {
        // The material names the PNG; only the .simage exists.
        using var project = new TempProject();
        project.WriteAsset(CookedTexturePath, TempProject.Bytes(64, seed: 2));
        project.WriteAsset(MaterialPath, Body);

        Cook(project).Succeeded.ShouldBeTrue();

        ImageContentPath.CookedPathFor(TexturePath).ShouldBe(CookedTexturePath);
    }

    [Fact]
    public void Adding_the_cooked_texture_a_material_probed_for_re_cooks_that_material()
    {
        // The first cook probes for the .simage, misses, and uses the PNG.
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 3));
        project.WriteAsset(MaterialPath, Body);

        CookResult first = Cook(project, cache: true, label: "cold");
        first.Succeeded.ShouldBeTrue();

        CookResult unchanged = Cook(project, cache: true, label: "warm");
        unchanged.Assets.Single(a => a.SourcePath == MaterialPath).FromCache.ShouldBeTrue();

        project.WriteAsset("Textures/other.simage", TempProject.Bytes(32, seed: 4));

        // An unrelated .simage must not invalidate it.
        Cook(project, cache: true, label: "unrelated")
            .Assets.Single(a => a.SourcePath == MaterialPath).FromCache.ShouldBeTrue();

        project.WriteAsset(CookedTexturePath, TempProject.Bytes(48, seed: 5));

        Cook(project, cache: true, label: "arrived")
            .Assets.Single(a => a.SourcePath == MaterialPath).FromCache.ShouldBeFalse();
    }

    [Fact]
    public void A_material_naming_a_shader_nothing_provides_is_refused_and_names_it()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 6));
        project.WriteAsset(
            MaterialPath, "shader = glowy\ntexture uDiffuse = Textures/wall_brick.png\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeFalse();

        var missing = result.Diagnostics.Single(d => d.Id.ToString() == "SC5003");
        missing.Message.ShouldContain("glowy");
        missing.File.ShouldBe(MaterialPath);
    }

    [Fact]
    public void The_built_in_shader_and_a_project_shader_both_resolve()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 7));

        // Case-insensitive, as AssetManager matches it.
        project.WriteAsset("Materials/a.spectramat", "shader = Lit\n");

        // A built-in, embedded in the engine assembly.
        project.WriteAsset("Materials/b.spectramat", "shader = GBufferFill\n");

        // A project shader. Real source, because the shader rule compiles it.
        project.WriteAsset($"{BaseShaders.ContentFolder}/Glowy.spectrashade", BaseShaders.Lit);
        project.WriteAsset("Materials/c.spectramat", "shader = Glowy\n");

        // No shader key means the built-in.
        project.WriteAsset("Materials/d.spectramat", "float uRoughness = 0.5\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC5003");
    }

    [Fact]
    public void An_unknown_key_warns_and_the_material_still_reaches_the_pack()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 8));
        project.WriteAsset(MaterialPath, Body + "frobnicate uThing = 3\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.Single(d => d.Id.ToString() == "SC5002")
            .Message.ShouldContain("frobnicate");

        result.Assets.Single(a => a.SourcePath == MaterialPath).Outputs.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_rule_claims_material_files_and_nothing_else()
    {
        MaterialRule.Handles(MaterialPath).ShouldBeTrue();
        MaterialRule.Handles("Materials/WALL.SPECTRAMAT").ShouldBeTrue();
        MaterialRule.Handles(TexturePath).ShouldBeFalse();
        MaterialRule.Handles("Shaders/Lit.spectrashade").ShouldBeFalse();
        MaterialRule.Handles("Materials/notes.txt").ShouldBeFalse();

        MaterialParser.FileExtension.ShouldBe(".spectramat");
    }

    [Fact]
    public void The_engines_own_materials_cook_clean()
    {
        // The repo's real Assets/, copied beside the test binary.
        string assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        Directory.Exists(assets).ShouldBeTrue($"the engine's content should be beside the test binary: {assets}");

        using var project = new TempProject();
        CopyTree(assets, project.Layout.AssetsPath);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Assets.Count(a => a.Rule == RuleKind.Material).ShouldBeGreaterThan(0);
    }

    private static CookResult Cook(TempProject project, bool cache = false, string label = "out") =>
        new CookSession(
                project.Layout,
                new CookSettings { UseCache = cache, OutputPath = Path.Combine(project.Root, label) })
            .Run();

    private static string Describe(CookResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString()));

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (string file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);

        foreach (string directory in Directory.GetDirectories(from))
            CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
