using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Assets.Sources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The cook end to end: a project folder in, one mountable pack out. The pack
/// is checked by mounting it through the engine's reader.
/// </summary>
public class CookSessionTests
{
    [Fact]
    public void A_cooked_project_mounts_as_one_pack_of_raw_entries()
    {
        using var project = new TempProject();
        byte[] texture = project.WriteAsset("Data/strings.bin", TempProject.Bytes(300, seed: 1));
        byte[] material = project.WriteAsset("Materials/wall.spectramat", "shader = Lit\n");
        byte[] model = project.WriteAsset("Models/crate.obj", TempProject.Bytes(64, seed: 2));

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue();
        result.ErrorCount.ShouldBe(0);
        result.EntryCount.ShouldBe(3);
        result.OutputPath.ShouldNotBeNull();
        File.Exists(result.OutputPath).ShouldBeTrue();

        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));
        pack.EntryCount.ShouldBe(3);
        pack.TombstoneCount.ShouldBe(0);

        ReadEntry(pack, "Data/strings.bin").ShouldBe(texture);
        ReadEntry(pack, "Materials/wall.spectramat").ShouldBe(material);
        ReadEntry(pack, "Models/crate.obj").ShouldBe(model);
    }

    [Fact]
    public void A_cooked_asset_resolves_by_the_same_path_a_loose_file_did()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/strings.bin", TempProject.Bytes(16));

        CookResult result = Cook(project);
        var pack = project.Track(new PackSource(NullLogger.Instance, result.OutputPath!));

        // The id hashes the normalised path, so every spelling is one asset.
        pack.Exists("Data/strings.bin").ShouldBeTrue();
        pack.Exists(@"Data\strings.bin").ShouldBeTrue();
        pack.Exists("/Data/strings.bin").ShouldBeTrue();
        pack.Exists("Data/absent.bin").ShouldBeFalse();
    }

    [Fact]
    public void Two_cooks_of_one_project_are_byte_identical()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/a.bin", TempProject.Bytes(50, seed: 3));
        project.WriteAsset("Data/b.bin", TempProject.Bytes(51, seed: 4));
        project.WriteAsset("Deep/Nested/Folder/c.txt", "hello\n");

        // An image too: BC7 encoding is the step most likely to vary.
        project.WriteAsset("Textures/wall_brick.png", TempProject.Png(16, 16, seed: 5));

        byte[] first = File.ReadAllBytes(Cook(project).OutputPath!);
        byte[] second = File.ReadAllBytes(Cook(project).OutputPath!);

        second.ShouldBe(first);
    }

    [Fact]
    public void Every_asset_records_the_input_it_read()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/a.bin", TempProject.Bytes(8));

        CookResult result = Cook(project);

        CookedAsset asset = result.Assets.Single();
        asset.Rule.ShouldBe(RuleKind.RawCopy);
        asset.RuleVersion.ShouldBe(1);
        asset.Dependencies.Single().Path.ShouldBe("Data/a.bin");
        asset.Dependencies.Single().Kind.ShouldBe(RuleDependencyKind.Read);
        asset.Outputs.Single().Path.ShouldBe("Data/a.bin");
        asset.Outputs.Single().Length.ShouldBe(8);
    }

    [Fact]
    public void A_loose_cook_writes_the_tree_instead_of_a_pack()
    {
        using var project = new TempProject();
        byte[] payload = project.WriteAsset("Data/strings.bin", TempProject.Bytes(24));

        CookResult result = Cook(project, new CookSettings { Loose = true });

        result.Succeeded.ShouldBeTrue();
        result.OutputPath.ShouldBe(project.CookedPath);

        string written = Path.Combine(project.CookedPath, "Data", "strings.bin");
        File.Exists(written).ShouldBeTrue();
        File.ReadAllBytes(written).ShouldBe(payload);
        Directory.GetFiles(project.CookedPath, "*.spack").ShouldBeEmpty();
    }

    [Fact]
    public void A_project_with_no_assets_folder_is_a_cook_error()
    {
        using var project = new TempProject();
        Directory.Delete(project.Layout.AssetsPath, recursive: true);

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeFalse();
        result.ErrorCount.ShouldBe(1);
        result.Diagnostics.Single().Id.ToString().ShouldBe("SC1001");

        result.OutputPath.ShouldBeNull();
    }

    [Fact]
    public void A_map_bundle_is_baked_into_the_pack_beside_the_content()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/a.bin", TempProject.Bytes(4));
        WriteMapBundle(project, "Lobby.smap");

        CookResult result = Cook(project);

        result.Succeeded.ShouldBeTrue();
        result.Diagnostics.ShouldBeEmpty();

        CookedAsset map = result.Assets.Single(asset => asset.SourcePath == "Maps/Lobby.smap");
        map.Rule.ShouldBe(RuleKind.Map);
        map.Outputs.Single().Path.ShouldBe("Maps/Lobby.scmap");
    }

    [Fact]
    public void The_manifest_lists_every_asset_its_inputs_and_its_output_hash()
    {
        using var project = new TempProject();
        project.WriteAsset("Data/a.bin", TempProject.Bytes(12));

        string manifestPath = Path.Combine(project.Root, "cook-manifest.json");
        CookResult result = Cook(project, new CookSettings { ManifestPath = manifestPath });

        result.Succeeded.ShouldBeTrue();
        string manifest = File.ReadAllText(manifestPath);

        manifest.ShouldContain("\"scookManifest\": 1");

        // Each asset record is compact, on one line.
        manifest.ShouldContain("\"path\":\"Data/a.bin\"");
        manifest.ShouldContain("\"rule\":\"rawcopy\"");
        manifest.ShouldContain(
            result.Assets.Single().Outputs.Single().ContentHash.ToString("X32"));

        // LF on every OS.
        manifest.ShouldNotContain("\r\n");
        manifest.EndsWith('\n').ShouldBeTrue();
    }

    [Fact]
    public void The_manifest_carries_the_paths_a_rule_looked_for_and_did_not_find()
    {
        var asset = new CookedAsset(
            "Materials/wall.spectramat",
            RuleKind.Material,
            3,
            [
                new RuleDependency("Materials/wall.spectramat", RuleDependencyKind.Read, (UInt128)7),
                new RuleDependency("Textures/wall_normal.png", RuleDependencyKind.ProbeMissing, UInt128.Zero),
            ],
            [new CookedOutput("Materials/wall.spectramat", (UInt128)11, (UInt128)13, 42)]);

        string manifest = Encoding.UTF8.GetString(
            CookManifest.Write("TestGame", CookProfile.Ship, [asset]));

        manifest.ShouldContain("\"missing\":[\"Textures/wall_normal.png\"]");
        manifest.ShouldContain("\"rule\":\"material\"");
        manifest.ShouldContain("\"ruleVersion\":3");
    }

    private static CookResult Cook(TempProject project, CookSettings? settings = null) =>
        new CookSession(project.Layout, settings ?? new CookSettings()).Run();

    private static byte[] ReadEntry(PackSource pack, string path)
    {
        pack.TryOpen(path, out ContentBlob? blob).ShouldBeTrue();
        using (blob!)
            return blob.Span.ToArray();
    }

    private static void WriteMapBundle(TempProject project, string bundleName)
    {
        string bundle = Path.Combine(project.Layout.MapsPath, bundleName);
        Directory.CreateDirectory(bundle);
        File.WriteAllText(
            Path.Combine(bundle, "map.json"),
            "{\n  \"spectramap\": 1,\n  \"minimumReadableVersion\": 1,\n  \"nodes\": []\n}\n");
    }
}
