using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Rules;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// What a bake makes of the file a script node names: how the path is
/// spelled, a path that is also another kind of asset, and a file that is no
/// <c>.luau</c> file.
/// </summary>
public class MapScriptPathTests
{
    private const string Level = "Maps/Room.smap";

    [Fact]
    public void Two_spellings_of_one_script_file_bake_as_their_nodes_spell_them()
    {
        using var project = new TempProject();
        MapFixture.WriteBundle(project, "Room.smap", TwoSpellings());

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project);

        said.ShouldBeEmpty();
        ScmapProbe map = ScmapProbe.Read(file.ShouldNotBeNull());
        map.Scripts.Select(script => script.Path)
            .ShouldBe(["Scripts/Door.luau", "Scripts/door.luau", "Scripts/Door.luau"]);

        // One row for each spelling, and none for a spelling seen before.
        map.Assets.Count(row => row.Kind == PackEntryKind.Script).ShouldBe(2);
    }

    [Fact]
    public void A_cooked_level_names_each_script_as_the_authored_level_does()
    {
        using CookedLevel level = CookedLevel.Bake(TwoSpellings());

        // The path is what a script prints in front of a line.
        level.Scene.ScriptNodes.Select(node => node.Script?.Path)
            .ShouldBe(level.Authored.ScriptNodes.Select(node => node.Script?.Path));
        level.Scene.ScriptNodes.Select(node => node.Script?.Path).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void A_file_named_as_a_script_and_as_a_model_stops_the_bake_with_a_code()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();

        Scene scene = fixture.BuildScene();
        scene.Root.CreateChild("Prop");
        scene.Root.CreateChild("Spin").Script = new Script("Scripts/Thing.luau");

        // In another case: a pack finds one file for both.
        MapDocument document = MapSceneBinder.FromScene(scene);
        document.Nodes.Single(node => node.Name == "Prop").Mesh = new MapMeshSource { Model = "scripts/thing.luau" };
        Save(project, document);

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project);

        file.ShouldBeNull();
        CookDiagnostic refused = said.ShouldHaveSingleItem();
        refused.Id.ToString().ShouldBe("SC7012");
        refused.IsError.ShouldBeTrue();
        refused.Message.ShouldContain(Level);
        refused.Message.ShouldContain("'scripts/thing.luau'");
        refused.Message.ShouldContain("as a model and as a script");
        CookGate.Verdict(CookDiagnosticCodes.MapAssetOfTwoKinds).ShouldBe(CookGateVerdict.Fatal);
    }

    [Theory]
    [InlineData("Scripts/door.lua")]
    [InlineData("Scripts/door.txt")]
    [InlineData("Scripts/door")]
    public void A_script_node_that_names_a_file_that_is_not_luau_stops_the_bake(string path)
    {
        using var project = new TempProject();
        Scene scene = MapFixture.Fresh().BuildScene();
        scene.Root.CreateChild("Door").Script = new Script(path);
        MapFixture.WriteBundle(project, "Room.smap", scene);

        (byte[]? file, List<CookDiagnostic> said) = TryBake(project);

        file.ShouldBeNull();
        CookDiagnostic refused = said.ShouldHaveSingleItem();
        refused.Id.ToString().ShouldBe("SC7013");
        refused.Message.ShouldContain("'Door'");
        refused.Message.ShouldContain($"'{path}'");
        refused.Message.ShouldContain(".luau");
        CookGate.Verdict(CookDiagnosticCodes.MapScriptNotLuau).ShouldBe(CookGateVerdict.Fatal);
    }

    [Fact]
    public void A_project_whose_level_names_a_script_the_cook_would_not_check_makes_no_pack()
    {
        using var project = new TempProject();
        MapFixture fixture = MapFixture.Fresh();
        fixture.WriteMaterials(project);

        // The raw copy rule would pack this file as it is.
        project.WriteAsset("Scripts/door.lua", "this is ( no script\n");
        Scene scene = fixture.BuildScene();
        scene.Root.CreateChild("Door").Script = new Script("Scripts/door.lua");
        MapFixture.WriteBundle(project, "Room.smap", scene);

        var check = new FakeScriptCheck();
        CookResult cooked = new CookSession(
            project.Layout, new CookSettings { UseCache = false, ScriptCheck = check }).Run();

        cooked.Succeeded.ShouldBeFalse();
        cooked.OutputPath.ShouldBeNull();
        cooked.Diagnostics.Single(d => d.IsError).Id.ToString().ShouldBe("SC7013");
        check.Asked.ShouldBeEmpty();
    }

    // Three script nodes over one file, the second in another case.
    private static Scene TwoSpellings()
    {
        Scene scene = MapFixture.Fresh().BuildScene();
        scene.Root.CreateChild("Upper").Script = new Script("Scripts/Door.luau");
        scene.Root.CreateChild("Lower").Script = new Script("Scripts/door.luau");
        scene.Root.CreateChild("Again").Script = new Script("Scripts/Door.luau");
        return scene;
    }

    private static void Save(TempProject project, MapDocument document)
    {
        string bundle = Path.Combine(project.Layout.MapsPath, Path.GetFileName(Level));
        Directory.CreateDirectory(bundle);
        MapBundle.Save(bundle, document);
    }

    private static (byte[]? File, List<CookDiagnostic> Diagnostics) TryBake(TempProject project)
    {
        // Content root is the project root: a bundle lives beside Assets/.
        var context = new RuleContext(project.Root, Level, CookProfile.Ship);
        new MapRule().Cook(context);

        return (
            context.Emissions.Count == 0 ? null : context.Emissions[0].Payload,
            [.. context.Diagnostics]);
    }
}
