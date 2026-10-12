using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// Scripts through a real bake and a real load: the cooked level has the
/// script nodes of the authored one, and its nodes answer what a script asks
/// as the authored ones do.
/// </summary>
public class CompiledMapScriptTests
{
    [Fact]
    public void Every_script_arrives_on_its_node_as_it_was_authored()
    {
        using CookedLevel level = CookedLevel.Bake(Scripted());

        level.Authored.ScriptNodes.Count.ShouldBe(4);
        level.Report.ScriptsLoaded.ShouldBe(4);

        // The scene's own list, in tree order: scripts start in that order.
        level.Scene.ScriptNodes.Select(node => node.Name)
            .ShouldBe(level.Authored.ScriptNodes.Select(node => node.Name));

        foreach (SceneNode authored in Nodes(level.Authored))
            Counterpart(level, authored).Script.ShouldBe(authored.Script, authored.Name);

        // A script and a module, each switched on and switched off.
        level.Scene.ScriptNodes.Select(node => node.Script).OfType<Script>()
            .Select(script => (script.IsEnabled, script.IsModule)).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void A_script_keeps_its_place_under_the_node_it_acts_on()
    {
        using CookedLevel level = CookedLevel.Bake(Scripted());

        SceneNode spin = level.Scene.ScriptNodes.Single(node => node.Name == "Spin");

        spin.Parent.ShouldNotBeNull().Name.ShouldBe("Crate");
        spin.Parent.Brush.ShouldNotBeNull("the crate is a part, and a part keeps its brush");
    }

    [Fact]
    public void Every_node_of_the_cooked_level_has_the_class_name_it_has_authored()
    {
        using CookedLevel level = CookedLevel.Bake(Scripted(withEntities: true, withLights: true));

        var seen = new HashSet<string>();
        foreach (SceneNode authored in Nodes(level.Authored))
        {
            string expected = NodeClassName.Of(authored);
            NodeClassName.Of(Counterpart(level, authored)).ShouldBe(expected, authored.Name);
            seen.Add(expected);
        }

        // Without these the loop above compared nothing that could differ.
        seen.ShouldContain(NodeClassName.Part);
        seen.ShouldContain(NodeClassName.Script);
        seen.ShouldContain(NodeClassName.ModuleScript);
        seen.ShouldContain(NodeClassName.Folder);
        seen.ShouldContain("func_door");
        seen.ShouldContain("SpotLight");
    }

    [Fact]
    public void A_baked_world_brush_has_no_brush_and_still_says_what_it_was()
    {
        using CookedLevel level = CookedLevel.Bake(Scripted());

        SceneNode wall = Named(level.Scene, "Wall");
        wall.Brush.ShouldBeNull();
        wall.BakedKind.ShouldBe(SceneNodeKind.BrushWorld);
        NodeClassName.Of(wall).ShouldBe(NodeClassName.Part);

        Named(level.Scene, "Doorway").BakedKind.ShouldBe(SceneNodeKind.BrushSubtractive);

        // A part came through with its brush, and a folder never had one.
        Named(level.Scene, "Crate").BakedKind.ShouldBeNull();
        Named(level.Scene, "Shelf").BakedKind.ShouldBeNull();
        Nodes(level.Authored).ShouldAllBe(node => node.BakedKind == null);
    }

    [Theory]
    [InlineData("Wall")]
    [InlineData("Doorway")]
    [InlineData("Shelf")]
    public void A_world_brush_and_what_is_above_it_move_in_neither_level_and_for_the_same_reason(string name)
    {
        using CookedLevel level = CookedLevel.Bake(Scripted());

        string authored = MoveRefusal(level.Authored, name).ShouldNotBeNull();
        string cooked = MoveRefusal(level.Scene, name).ShouldNotBeNull();

        cooked.ShouldBe(authored);
        authored.ShouldContain("world brush");
        Named(level.Scene, name).LocalPosition.ShouldBe(Named(level.Authored, name).LocalPosition);
    }

    [Theory]
    [InlineData("Crate")]
    [InlineData("Spin")]
    [InlineData("Rules")]
    public void A_part_and_a_script_node_move_in_both_levels(string name)
    {
        using CookedLevel level = CookedLevel.Bake(Scripted());

        MoveRefusal(level.Authored, name).ShouldBeNull();
        MoveRefusal(level.Scene, name).ShouldBeNull();
    }

    [Fact]
    public void A_level_bakes_the_same_world_with_and_without_its_scripts()
    {
        MapFixture fixture = MapFixture.Fresh();
        using CookedLevel plain = CookedLevel.Bake(Scripted(fixture, withScripts: false));
        using CookedLevel scripted = CookedLevel.Bake(Scripted(fixture));

        ScmapProbe.Read(plain.File).Scripts.ShouldBeEmpty();

        ScmapProbe before = ScmapProbe.Read(plain.File);
        ScmapProbe after = ScmapProbe.Read(scripted.File);
        after.TriangleCount.ShouldBe(before.TriangleCount);
        after.Hulls.Count.ShouldBe(before.Hulls.Count);
        after.Geometry.Count.ShouldBe(before.Geometry.Count);
    }

    // The room, with a shelf that holds a world brush, and four scripts: on
    // the crate, on the shelf's brush, and two at the root.
    private static SpectraEngine.Core.Scene.Scene Scripted(
        MapFixture? fixture = null, bool withScripts = true, bool withEntities = false, bool withLights = false)
    {
        SpectraEngine.Core.Scene.Scene scene =
            (fixture ?? MapFixture.Fresh()).BuildScene(withEntities: withEntities, withLights: withLights);

        SceneNode shelf = scene.Root.CreateChild("Shelf");
        SceneNode plank = shelf.CreateChild("Plank");
        plank.LocalPosition = new Vector3(4f, 1f, 0f);
        plank.Brush = Brush.CreateBox(new Vector3(-1f, -0.05f, -0.25f), new Vector3(1f, 0.05f, 0.25f));

        if (!withScripts) return scene;

        Named(scene, "Crate").CreateChild("Spin").Script = new Script("Scripts/spin.luau");
        plank.CreateChild("Dust").Script = new Script("Scripts/Lib/dust.luau") { IsEnabled = false, IsModule = true };
        scene.Root.CreateChild("Rules").Script = new Script("Scripts/rules.luau") { IsModule = true };
        scene.Root.CreateChild("Later").Script = new Script("Scripts/spin.luau") { IsEnabled = false };
        return scene;
    }

    // What the level says to a move of the node by a step, as a running level
    // is asked. Null when it moved.
    private static string? MoveRefusal(SpectraEngine.Core.Scene.Scene scene, string name)
    {
        var world = new EntityWorld(scene, NullLogger.Instance, new EntityCatalog());
        world.Activate();

        try
        {
            SceneNode node = Named(scene, name);
            Transform moved = node.LocalTransform with { Position = node.LocalPosition + Vector3.UnitY };
            return world.TrySetLocalTransform(node, in moved, out string? refusal) ? null : refusal;
        }
        finally
        {
            world.Deactivate();
        }
    }

    private static SceneNode Counterpart(CookedLevel level, SceneNode authored)
    {
        level.Scene.TryFindById(authored.Id, out SceneNode? node).ShouldBeTrue(authored.Name);
        return node.ShouldNotBeNull();
    }

    private static SceneNode Named(SpectraEngine.Core.Scene.Scene scene, string name) =>
        scene.Root.Traverse().Single(node => node.Name == name);

    private static IEnumerable<SceneNode> Nodes(SpectraEngine.Core.Scene.Scene scene) =>
        scene.Root.Traverse().Where(node => !ReferenceEquals(node, scene.Root));
}
