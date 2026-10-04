using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Gizmos;
using SpectraEngine.Editing.Hosting;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>Inserting a model into the scene through the editor host.</summary>
public sealed class ModelInsertTests
{
    // A real model in the repo's content root, so the real importer runs.
    private const string Crate = "Models/crate.obj";

    private static SceneEditorHost NewHost(Scene scene, Renderer? renderer = null)
    {
        renderer ??= new CompilingRenderer();

        // The host reads the viewport size in its constructor; zero breaks picking.
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance,
            scene,
            renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    private static (SceneEditorHost Host, AssetManager Assets) NewHostWithAssets(Scene scene)
    {
        var renderer = new FakeRenderer();
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);
        scene.Assets = assets;

        return (NewHost(scene, renderer), assets);
    }

    // Plate top is at y = 1 and the centre ray hits it.
    private static void AimAtAPlate(Scene scene)
    {
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = Brush.CreateBox(new Vector3(-8f, -1f, -8f), new Vector3(8f, 1f, 8f));

        scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));
    }

    [Fact]
    public void A_dropped_model_arrives_with_geometry_and_names_where_it_came_from()
    {
        var scene = new Scene("Editor");
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        ModelInsertReport report = host.InsertModel(Crate);

        report.Placed.ShouldBeTrue();
        report.IsComplete.ShouldBeTrue(report.Describe());
        report.Unresolved.ShouldBeNull();
        report.Refused.ShouldBeNull();

        scene.Root.Children.Count.ShouldBe(1);
        SceneNode node = scene.Root.Children[0];
        node.Id.ShouldBe(report.NodeId);

        // Named for the file, not the importer's root node.
        node.Name.ShouldBe("crate");

        // MeshSource is what lets the node survive a save and reload.
        CollectSources(node).ShouldNotBeEmpty("a dropped model must record which file it came from");
        foreach (MeshSource source in CollectSources(node))
            source.ModelPath.ShouldBe(Crate);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_dropped_model_is_one_history_entry_and_is_selected()
    {
        var scene = new Scene("Editor");
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        host.InsertModel(Crate);

        SceneNode node = scene.Root.Children[0];
        scene.Selection.Items.ShouldBe([node]);
        host.UndoDepth.ShouldBe(1, "a whole model subtree is one thing the user did");

        Guid id = node.Id;
        host.Apply(EditorHostCommand.Undo);
        scene.Root.Children.ShouldBeEmpty();

        host.Apply(EditorHostCommand.Redo);
        scene.Root.Children.Count.ShouldBe(1);
        scene.Root.Children[0].Id.ShouldBe(id);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_dropped_model_rests_on_the_surface_it_was_aimed_at()
    {
        // crate.obj's pivot is at its base, so this passes with zero clearance.
        // The centred-pivot test below is the one that checks the lift.
        var scene = new Scene("Editor");
        AimAtAPlate(scene);
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        host.InsertModel(Crate);

        SceneNode node = scene.Root.Children[^1];
        MeasureAlongY(node, out float min, out float max);

        max.ShouldBeGreaterThan(min, "the crate has to have some height for this to mean anything");
        min.ShouldBe(1f, 0.001f, "a dropped model rests flush on the surface under the cursor");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_model_whose_pivot_is_at_its_CENTRE_is_lifted_rather_than_half_buried()
    {
        // Every model in the repo has its pivot at its base, so only a fixture
        // centred on its origin can tell a measured clearance from zero.
        var scene = new Scene("Editor");
        AimAtAPlate(scene);

        string root = CenteredCubeContentRoot(out string modelPath);
        try
        {
            var renderer = new FakeRenderer();
            var assets = new AssetManager(
                NullLogger<AssetManager>.Instance, root, hotReloadEnabled: false);
            assets.AttachRenderer(renderer);
            scene.Assets = assets;

            SceneEditorHost host = NewHost(scene, renderer);
            ModelInsertReport report = host.InsertModel(modelPath);
            report.IsComplete.ShouldBeTrue(report.Describe());

            SceneNode node = scene.Root.Children[^1];
            MeasureAlongY(node, out float min, out float max);

            (max - min).ShouldBe(2f, 0.01f, "the fixture is two units tall");
            min.ShouldBe(1f, 0.001f, "the model's lowest point sits on the surface");
            node.LocalPosition.Y.ShouldBe(2f, 0.01f, "its pivot is therefore a unit above the surface");

            assets.ReleaseGraphicsResources();
        }
        finally
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_drop_aims_at_the_point_it_was_dropped_on_rather_than_the_view_centre()
    {
        var scene = new Scene("Editor");
        AimAtAPlate(scene);
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        host.InsertModel(Crate, new Vector2(320f, 300f));
        Vector3 left = scene.Root.Children[^1].LocalPosition;

        host.Apply(EditorHostCommand.Undo);

        host.InsertModel(Crate, new Vector2(960f, 300f));
        Vector3 right = scene.Root.Children[^1].LocalPosition;

        right.X.ShouldBeGreaterThan(left.X, "a drop on the right of the viewport lands to the right");

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_model_file_that_carries_its_own_root_translation_still_lands_where_it_was_dropped()
    {
        var scene = new Scene("Editor");
        AimAtAPlate(scene);
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        host.InsertModel(Crate);

        SceneNode node = scene.Root.Children[^1];
        MeasureAlongY(node, out float min, out _);
        min.ShouldBe(1f, 0.001f);

        // No offset inherited from the file.
        node.LocalPosition.Y.ShouldBe(node.WorldPosition.Y, 0.0001f);

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void A_scene_with_no_asset_manager_still_places_a_node_and_says_why_it_is_empty()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        ModelInsertReport report = host.InsertModel(Crate);

        report.Placed.ShouldBeTrue("a node the user can see and delete beats silence");
        report.IsComplete.ShouldBeFalse();
        report.Unresolved.ShouldNotBeNullOrWhiteSpace();
        report.Refused.ShouldBeNull("nothing refused this; it simply could not be resolved");

        scene.Root.Children.Count.ShouldBe(1);
        SceneNode node = scene.Root.Children[0];
        node.MeshRenderer.ShouldBeNull();
        node.Name.ShouldBe("crate", "the node still says what was asked for");
        scene.Selection.Items.ShouldBe([node]);
        host.UndoDepth.ShouldBe(1, "an empty placeholder is still one thing to undo");
    }

    [Fact]
    public void A_model_the_project_does_not_have_places_a_node_naming_the_file()
    {
        var scene = new Scene("Editor");
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        ModelInsertReport report = host.InsertModel("Models/there_is_no_such_prop.obj");

        report.Placed.ShouldBeTrue();
        report.Unresolved.ShouldNotBeNullOrWhiteSpace();
        report.Describe().ShouldContain("there_is_no_such_prop.obj");
        scene.Root.Children[0].MeshRenderer.ShouldBeNull();

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void An_asset_path_that_escapes_the_content_root_is_reported_rather_than_resolved()
    {
        // ContentRoot.NormalizeRelativePath throws on rooted paths and on '..'.
        var scene = new Scene("Editor");
        (SceneEditorHost host, AssetManager assets) = NewHostWithAssets(scene);

        ModelInsertReport escape = host.InsertModel("../../secrets.obj");
        escape.Unresolved.ShouldNotBeNullOrWhiteSpace();

        host.Apply(EditorHostCommand.Undo);

        ModelInsertReport rooted = host.InsertModel(@"C:\elsewhere\prop.obj");
        rooted.Unresolved.ShouldNotBeNullOrWhiteSpace();

        assets.ReleaseGraphicsResources();
    }

    [Fact]
    public void An_empty_path_is_refused_outright_rather_than_placing_anything()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        ModelInsertReport report = host.InsertModel("   ");

        report.Placed.ShouldBeFalse();
        report.Refused.ShouldNotBeNullOrWhiteSpace();
        report.Unresolved.ShouldBeNull("nothing was placed, so nothing is missing geometry");
        scene.Root.Children.ShouldBeEmpty();
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_drop_is_refused_while_play_mode_owns_the_scene()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);
        host.Suspend();

        ModelInsertReport report = host.InsertModel(Crate);

        report.Placed.ShouldBeFalse();
        report.Refused.ShouldNotBeNullOrWhiteSpace();
        report.Describe().ShouldContain("not placed");
        scene.Root.Children.ShouldBeEmpty();
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_refusal_and_an_unresolved_model_are_reported_as_different_things()
    {
        // Refused: nothing happened. Unresolved: a node is in the scene and the
        // history.
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        ModelInsertReport unresolved = host.InsertModel(Crate);
        host.Suspend();
        ModelInsertReport refused = host.InsertModel(Crate);

        unresolved.Placed.ShouldBeTrue();
        refused.Placed.ShouldBeFalse();
        unresolved.Describe().ShouldNotBe(refused.Describe());
    }

    // Temp content root with one cube from -1 to +1. A real OBJ file so the
    // real importer runs.
    private static string CenteredCubeContentRoot(out string modelPath)
    {
        string root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "spectra-drop-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Models"));

        modelPath = "Models/centered_cube.obj";
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(root, "Models", "centered_cube.obj"),
            """
            v -1 -1 -1
            v  1 -1 -1
            v  1  1 -1
            v -1  1 -1
            v -1 -1  1
            v  1 -1  1
            v  1  1  1
            v -1  1  1
            f 1 3 2
            f 1 4 3
            f 5 6 7
            f 5 7 8
            f 1 2 6
            f 1 6 5
            f 2 3 7
            f 2 7 6
            f 3 4 8
            f 3 8 7
            f 4 1 5
            f 4 5 8

            """);

        return root;
    }

    private static List<MeshSource> CollectSources(SceneNode root)
    {
        var found = new List<MeshSource>();
        Walk(root);
        return found;

        void Walk(SceneNode node)
        {
            if (node.MeshSource is { } source)
                found.Add(source);

            for (int i = 0; i < node.Children.Count; i++)
                Walk(node.Children[i]);
        }
    }

    // World-space extent of the subtree along Y.
    private static void MeasureAlongY(SceneNode root, out float min, out float max)
    {
        var nodes = new List<SceneNode>();
        Walk(root);

        GizmoSelectionBounds.TryMeasure(
            nodes, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ,
            out Vector3 low, out Vector3 high).ShouldBeTrue();

        min = low.Y;
        max = high.Y;

        void Walk(SceneNode node)
        {
            nodes.Add(node);
            for (int i = 0; i < node.Children.Count; i++)
                Walk(node.Children[i]);
        }
    }
}
