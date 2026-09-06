using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Hosting;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Painting a material onto a brush, from the render thread's side of the
/// boundary.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gesture cannot be driven headlessly and the assignment can.</b> A
/// drag is Avalonia, a compositor, a pointer and an OLE session; what decides
/// whether the result is right is a verb taking a path, a viewport point and a
/// scope. So the verb is what has tests: which faces changed, that it is one
/// history entry, that the selection is left alone, and that each of the three
/// outcomes says the thing a person has to act on.
/// </para>
/// <para>
/// <b>The refusals are most of the file, for the reason the model-insert tests
/// give.</b> A drop has no keyboard equivalent, so one that silently does
/// nothing is indistinguishable from a drag the shell never received.
/// </para>
/// </remarks>
public sealed class MaterialAssignTests
{
    // A material the repo's own content root really has, so the resolved path
    // is measured against a real file rather than against a fixture.
    private const string Brick = "Materials/wall.spectramat";

    private static SceneEditorHost NewHost(Scene scene, Renderer? renderer = null)
    {
        renderer ??= new CompilingRenderer();
        renderer.SetFramebufferSize(new Vector2D<int>(1280, 720));

        return new SceneEditorHost(
            NullLoggerFactory.Instance,
            scene,
            renderer,
            new InputManager(NullLogger<InputManager>.Instance));
    }

    private static (SceneEditorHost Host, AssetManager Assets, Renderer Renderer) NewHostWithAssets(Scene scene)
    {
        var renderer = new FakeRenderer();
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);
        scene.Assets = assets;

        return (NewHost(scene, renderer), assets, renderer);
    }

    // A plate whose top face is at y = 1, with the camera above it looking
    // down: the centre ray hits the +Y face, which is plane 2 of CreateBox.
    private static SceneNode AimAtAPlate(Scene scene)
    {
        SceneNode plate = scene.Root.CreateChild("Plate");
        plate.Brush = Brush.CreateBox(new Vector3(-8f, -1f, -8f), new Vector3(8f, 1f, 8f));

        scene.Camera.Position = new Vector3(0.5f, 8f, 4f);
        scene.Camera.LookAt(new Vector3(0.5f, 1f, 0.5f));

        return plate;
    }

    private static int FacesWearing(Brush brush, string path)
    {
        int count = 0;
        for (int i = 0; i < brush.FaceSurfaces.Count; i++)
        {
            if (MaterialRegistry.TryGetPath(brush.FaceSurfaces[i].Material, out string p) &&
                string.Equals(p, path, System.StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // --- What lands ----------------------------------------------------------

    [Fact]
    public void A_drop_on_a_face_paints_that_face_only()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);

        MaterialAssignReport report = host.AssignMaterial(Brick, null, MaterialDropScope.Face);

        report.Applied.ShouldBeTrue(report.Describe());
        report.FacesChanged.ShouldBe(1);
        report.NodeName.ShouldBe("Plate");

        // One of six, and the whole point of the scope: painting a wall of a
        // room must not paint its floor and ceiling too.
        FacesWearing(plate.Brush!, Brick).ShouldBe(1);
    }

    [Fact]
    public void A_drop_with_brush_scope_paints_every_face()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);

        MaterialAssignReport report = host.AssignMaterial(Brick, null, MaterialDropScope.Brush);

        report.Applied.ShouldBeTrue(report.Describe());
        report.FacesChanged.ShouldBe(6);
        FacesWearing(plate.Brush!, Brick).ShouldBe(6);

        // The whole block, in words, rather than a count somebody has to know
        // the shape of a box to read.
        report.Describe().ShouldContain("the whole block");
    }

    [Fact]
    public void A_drop_is_one_undo_entry_and_leaves_the_selection_alone()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneNode other = scene.Root.CreateChild("Other");
        SceneEditorHost host = NewHost(scene);

        scene.Selection.Select(other);
        Brush before = plate.Brush!;

        host.AssignMaterial(Brick, null, MaterialDropScope.Brush);

        host.UndoDepth.ShouldBe(1);

        // A paint sweep across five blocks must not end with the last one
        // selected and the user's own selection gone.
        scene.Selection.Items.Count.ShouldBe(1);
        scene.Selection.Items[0].ShouldBeSameAs(other);

        host.Apply(EditorHostCommand.Undo);
        plate.Brush.ShouldBeSameAs(before);
    }

    [Fact]
    public void A_material_the_brush_already_wears_records_nothing()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);

        host.AssignMaterial(Brick, null, MaterialDropScope.Brush);
        Brush painted = plate.Brush!;

        MaterialAssignReport again = host.AssignMaterial(Brick, null, MaterialDropScope.Brush);

        // Reference identity is what invalidates the carve downstream, so a
        // second identical assignment must not produce a new brush: it would
        // recompile the world to draw the picture it already had.
        again.Applied.ShouldBeTrue();
        again.FacesChanged.ShouldBe(0);
        plate.Brush.ShouldBeSameAs(painted);
        host.UndoDepth.ShouldBe(1);

        again.Describe().ShouldContain("already wears");
    }

    [Fact]
    public void An_empty_path_puts_the_face_back_to_the_engine_default()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);

        host.AssignMaterial(Brick, null, MaterialDropScope.Brush);
        MaterialAssignReport report = host.AssignMaterial("", null, MaterialDropScope.Brush);

        report.Applied.ShouldBeTrue(report.Describe());
        report.FacesChanged.ShouldBe(6);
        plate.Brush!.FaceSurfaces[0].Material.IsDefault.ShouldBeTrue();

        // "None" is a real answer in the picker, so it needs a sentence of its
        // own rather than one reading "  applied to the whole block".
        report.Describe().ShouldContain("default material");
    }

    // --- The refusals --------------------------------------------------------

    [Fact]
    public void A_drop_on_nothing_is_refused_in_words()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        MaterialAssignReport report = host.AssignMaterial(Brick, null, MaterialDropScope.Face);

        report.Applied.ShouldBeFalse();
        report.Refused.ShouldNotBeNullOrWhiteSpace();
        report.Describe().ShouldContain("nothing is under the pointer");
    }

    [Fact]
    public void A_drop_on_a_mesh_is_refused_by_saying_what_takes_a_material()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        SceneNode prop = scene.Root.CreateChild("Prop");
        prop.MeshRenderer = new MeshRenderer(
            BoxMesh.Centred(new Vector3(1f)), new Material(null));

        scene.Camera.Position = new Vector3(0f, 0f, 5f);
        scene.Camera.LookAt(Vector3.Zero);

        MaterialAssignReport report = host.AssignMaterial(Brick, null, MaterialDropScope.Face);

        report.Applied.ShouldBeFalse();
        report.Describe().ShouldContain("mesh");
    }

    [Fact]
    public void A_rooted_path_is_refused_rather_than_interned()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);
        Brush before = plate.Brush!;

        // The registry interns whatever it is handed, so a path outside the
        // content root would become a material reference nothing can resolve,
        // written into a map and carried to whoever opens it next.
        MaterialAssignReport report = host.AssignMaterial(
            @"C:\elsewhere\brick.spectramat", null, MaterialDropScope.Face);

        report.Applied.ShouldBeFalse();
        plate.Brush.ShouldBeSameAs(before);
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_drop_while_play_mode_owns_the_scene_is_refused_and_records_nothing()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        SceneEditorHost host = NewHost(scene);
        Brush before = plate.Brush!;

        host.Suspend();

        MaterialAssignReport report = host.AssignMaterial(Brick, null, MaterialDropScope.Face);

        report.Applied.ShouldBeFalse();
        report.Describe().ShouldContain("play mode");
        plate.Brush.ShouldBeSameAs(before);
        host.UndoDepth.ShouldBe(0);
    }

    [Fact]
    public void A_material_with_no_file_is_applied_and_reported_as_unresolved()
    {
        var scene = new Scene("Editor");
        SceneNode plate = AimAtAPlate(scene);
        (SceneEditorHost host, AssetManager assets, Renderer renderer) = NewHostWithAssets(scene);

        try
        {
            MaterialAssignReport report = host.AssignMaterial(
                "Materials/nobody_wrote_this.spectramat", null, MaterialDropScope.Face);

            // Applied AND wrong, which is the third voice: the faces really did
            // change and the answer is to write the file rather than to undo.
            report.Applied.ShouldBeTrue();
            report.FacesChanged.ShouldBe(1);
            report.Unresolved.ShouldNotBeNullOrWhiteSpace();
            report.Describe().ShouldContain("missing");

            plate.Brush!.FaceSurfaces[2].Material.IsDefault.ShouldBeFalse();
        }
        finally
        {
            assets.ReleaseGraphicsResources();
        }
    }

    // --- The selection route -------------------------------------------------

    [Fact]
    public void Assign_to_selection_paints_every_selected_brush_in_one_entry()
    {
        var scene = new Scene("Editor");
        SceneNode a = scene.Root.CreateChild("A");
        a.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        SceneNode b = scene.Root.CreateChild("B");
        b.Brush = Brush.CreateBox(new Vector3(3f), new Vector3(5f));

        SceneEditorHost host = NewHost(scene);
        scene.Selection.Select(a);
        scene.Selection.Add(b);

        MaterialAssignReport report = host.AssignMaterialToSelection(Brick);

        report.Applied.ShouldBeTrue(report.Describe());
        report.FacesChanged.ShouldBe(12);
        report.NodeName.ShouldBe("2 blocks");
        host.UndoDepth.ShouldBe(1);

        FacesWearing(a.Brush!, Brick).ShouldBe(6);
        FacesWearing(b.Brush!, Brick).ShouldBe(6);
    }

    [Fact]
    public void Assign_to_selection_with_no_block_selected_says_to_select_one()
    {
        var scene = new Scene("Editor");
        scene.Root.CreateChild("Empty");
        SceneEditorHost host = NewHost(scene);

        scene.Selection.Select(scene.Root.Children[0]);

        MaterialAssignReport report = host.AssignMaterialToSelection(Brick);

        report.Applied.ShouldBeFalse();
        report.Describe().ShouldContain("Select one");
        host.UndoDepth.ShouldBe(0);
    }

    // --- The drag flag -------------------------------------------------------

    [Fact]
    public void The_material_drag_flag_is_state_the_outline_reads_and_a_reset_clears_it()
    {
        var scene = new Scene("Editor");
        SceneEditorHost host = NewHost(scene);

        host.MaterialDrag.ShouldBeNull();

        host.SetMaterialDrag(MaterialDropScope.Brush);
        host.MaterialDrag.ShouldBe(MaterialDropScope.Brush);

        host.SetMaterialDrag(null);
        host.MaterialDrag.ShouldBeNull();
    }
}
