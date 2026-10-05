using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Packs;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Audio.Acoustics;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// A material's <c>acoustic</c> line through the cook. The pack holds the
/// material's text, so the line needs no cooked field of its own.
/// </summary>
public class MaterialAcousticCookTests
{
    private const string MaterialPath = "Materials/door.spectramat";
    private const string TexturePath = "Textures/door.png";

    private const string Body = "shader = lit\ntexture uDiffuse = Textures/door.png\n";

    [Fact]
    public void An_acoustic_line_is_cooked_and_read_back_from_the_pack_as_its_preset()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 21));
        project.WriteAsset(MaterialPath, Body + "acoustic = wood\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        result.Diagnostics.ShouldNotContain(d => d.Id.ToString() == "SC5002", Describe(result));

        ReadBack(project, result).ShouldBeSameAs(AcousticPresets.Wood);

        PackVerifyResult verified = PackVerifier.Verify(result.OutputPath!);
        verified.Succeeded.ShouldBeTrue();
        verified.WarningCount.ShouldBe(0);
    }

    [Fact]
    public void A_cooked_material_without_the_line_reads_back_as_generic()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 22));
        project.WriteAsset(MaterialPath, Body);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        ReadBack(project, result).ShouldBeSameAs(AcousticPresets.Generic);
    }

    [Fact]
    public void An_unknown_preset_warns_as_a_malformed_material_in_the_cook_and_in_the_verifier()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 23));
        project.WriteAsset(MaterialPath, Body + "acoustic = wod\n");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue(Describe(result));
        CookDiagnostic cooked = result.Diagnostics.Single(d => d.Id.ToString() == "SC5002");
        cooked.IsError.ShouldBeFalse();
        cooked.Message.ShouldContain("Materials/door.spectramat(3)");
        cooked.Message.ShouldContain("'wod'");

        // The line is still in the pack, and nothing is heard of it there.
        ReadBack(project, result).ShouldBeSameAs(AcousticPresets.Generic);

        PackVerifyResult verified = PackVerifier.Verify(result.OutputPath!);
        verified.Succeeded.ShouldBeTrue();
        verified.Diagnostics.Single(d => d.Id.ToString() == "SC5002").Message.ShouldContain("'wod'");
    }

    [Fact]
    public void A_strict_cook_refuses_an_unknown_preset()
    {
        using var project = new TempProject();
        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 24));
        project.WriteAsset(MaterialPath, Body + "acoustic = wod\n");

        CookResult result = Cook(project, strict: true);

        result.Succeeded.ShouldBeFalse();
        result.Diagnostics.Single(d => d.Id.ToString() == "SC5002").IsError.ShouldBeTrue();
    }

    // Through the pack alone, as a shipped game or a server would read it.
    private static AcousticPreset ReadBack(TempProject project, CookResult result)
    {
        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        var content = new ContentSourceStack();
        content.Mount(pack);

        return new MaterialAcoustics(NullLogger.Instance, content).Resolve(MaterialRegistry.Intern(MaterialPath));
    }

    private static CookResult Cook(TempProject project, bool strict = false) =>
        new CookSession(
                project.Layout,
                new CookSettings { UseCache = false, Strict = strict, OutputPath = Path.Combine(project.Root, "out") })
            .Run();

    private static string Describe(CookResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(static d => d.ToString()));
}
