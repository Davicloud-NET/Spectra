using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Undo;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// Editing a brush's material and one face's texture frame from the property
/// panel.
/// </summary>
/// <remarks>
/// <b>A face is named by the plane index in the edit's KEY.</b> That is the
/// mechanism an entity keyvalue already uses for the same reason: one
/// <c>PropertyId</c> covers a family of rows that only a string can tell apart.
/// An edit arriving without one names no face at all, and the editor refuses it
/// rather than guessing at "the first face", which would retexture a wall
/// nobody clicked.
/// </remarks>
public sealed class FacePropertyEditTests
{
    private const string Wall = "Materials/wall.spectramat";
    private const string Floor = "Materials/floor.spectramat";

    private static SceneNode BoxNode(Scene scene)
    {
        SceneNode node = scene.Root.CreateChild("Block");
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return node;
    }

    private static string Key(int plane) => plane.ToString(CultureInfo.InvariantCulture);

    private static int Apply(SceneNode node, PropertyEdit edit) =>
        PropertyEditor.Apply(new UndoStack(node.Owner!), [node], edit);

    // --- The whole brush -----------------------------------------------------

    [Fact]
    public void A_brush_material_edit_paints_every_face_in_one_entry()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        var undo = new UndoStack(scene);

        int changed = PropertyEditor.Apply(
            undo, [node], new PropertyEdit { Id = PropertyId.BrushMaterial, Text = Wall });

        changed.ShouldBe(1);
        undo.UndoCount.ShouldBe(1);

        for (int i = 0; i < node.Brush!.FaceSurfaces.Count; i++)
        {
            MaterialRegistry.TryGetPath(node.Brush.FaceSurfaces[i].Material, out string path).ShouldBeTrue();
            path.ShouldBe(Wall);
        }
    }

    [Fact]
    public void A_material_the_brush_already_wears_records_nothing()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        node.Brush = node.Brush!.WithAllFacesMaterial(MaterialRegistry.Intern(Wall));
        Brush before = node.Brush;

        Apply(node, new PropertyEdit { Id = PropertyId.BrushMaterial, Text = Wall })
            .ShouldBe(0);

        // Reference identity is the change detector for the carve and the
        // part-mesh cache: a fresh instance here recompiles the world to draw
        // the picture it already had.
        node.Brush.ShouldBeSameAs(before);
    }

    [Fact]
    public void An_empty_material_path_means_the_engine_default()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        node.Brush = node.Brush!.WithAllFacesMaterial(MaterialRegistry.Intern(Wall));

        Apply(node, new PropertyEdit { Id = PropertyId.BrushMaterial, Text = "" })
            .ShouldBe(1);

        node.Brush!.FaceSurfaces[0].Material.IsDefault.ShouldBeTrue();
    }

    [Fact]
    public void A_rooted_material_path_is_refused()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        Brush before = node.Brush!;

        // The registry interns whatever it is handed, so a path outside the
        // content root becomes a reference nothing can resolve, written into a
        // map and carried to whoever opens it next.
        Apply(node, new PropertyEdit
        {
            Id = PropertyId.BrushMaterial,
            Text = @"C:\elsewhere\wall.spectramat",
        }).ShouldBe(0);

        node.Brush.ShouldBeSameAs(before);
    }

    // --- One face ------------------------------------------------------------

    [Fact]
    public void A_face_material_edit_paints_only_that_plane()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        node.Brush = node.Brush!.WithAllFacesMaterial(MaterialRegistry.Intern(Wall));

        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceMaterial,
            Key = Key(3),
            Text = Floor,
        }).ShouldBe(1);

        MaterialRegistry.TryGetPath(node.Brush!.FaceSurfaces[3].Material, out string painted).ShouldBeTrue();
        painted.ShouldBe(Floor);

        MaterialRegistry.TryGetPath(node.Brush.FaceSurfaces[2].Material, out string neighbour).ShouldBeTrue();
        neighbour.ShouldBe(Wall);
    }

    [Fact]
    public void A_face_edit_with_no_key_names_no_face_and_records_nothing()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        Brush before = node.Brush!;

        Apply(node, new PropertyEdit { Id = PropertyId.FaceMaterial, Text = Wall })
            .ShouldBe(0);

        node.Brush.ShouldBeSameAs(before);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("-1")]
    [InlineData("not a number")]
    public void A_face_key_outside_the_brush_records_nothing(string key)
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        Brush before = node.Brush!;

        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceMaterial,
            Key = key,
            Text = Wall,
        }).ShouldBe(0);

        node.Brush.ShouldBeSameAs(before);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-2f)]
    [InlineData(float.NaN)]
    public void A_face_scale_that_cannot_be_used_is_refused_before_anything_is_written(float value)
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        Brush before = node.Brush!;

        // FaceSurface's own constructor throws on a zero scale, and a command
        // that threw from inside Do would leave the transaction open and the
        // scene half-edited. Refused here, where nothing has been built yet.
        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceUScale,
            Key = Key(0),
            Number = value,
        }).ShouldBe(0);

        node.Brush.ShouldBeSameAs(before);
    }

    [Fact]
    public void Scale_offset_and_rotation_edits_round_trip_through_the_rows()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        Apply(node, new PropertyEdit { Id = PropertyId.FaceUScale, Key = Key(2), Number = 2.5f });
        Apply(node, new PropertyEdit { Id = PropertyId.FaceVOffset, Key = Key(2), Number = -0.75f });
        Apply(node, new PropertyEdit { Id = PropertyId.FaceRotation, Key = Key(2), Number = 45f });

        List<PropertyRow> rows = [];
        NodeInspector.Describe(node, rows, null, pickedPlane: 2);

        float scale = 0f, offset = 0f, rotation = 0f;
        foreach (PropertyRow row in rows)
        {
            if (row.Id == PropertyId.FaceUScale) scale = row.Number;
            if (row.Id == PropertyId.FaceVOffset) offset = row.Number;
            if (row.Id == PropertyId.FaceRotation) rotation = row.Number;
        }

        scale.ShouldBe(2.5f, 1e-4f);
        offset.ShouldBe(-0.75f, 1e-4f);
        rotation.ShouldBe(45f, 1e-2f);
    }

    [Fact]
    public void An_alignment_edit_switches_the_face_between_the_two_frames()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        node.Brush!.FaceSurfaces[2].IsWorldAligned.ShouldBeTrue();

        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceAlignment,
            Key = Key(2),
            Text = "Face",
        }).ShouldBe(1);

        node.Brush!.FaceSurfaces[2].IsWorldAligned.ShouldBeFalse();

        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceAlignment,
            Key = Key(2),
            Text = "World",
        }).ShouldBe(1);

        node.Brush!.FaceSurfaces[2].IsWorldAligned.ShouldBeTrue();
    }

    [Fact]
    public void A_face_edit_on_a_placed_brush_is_written_in_the_brushs_own_space()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        // Turned and moved: the panel edits in world space and the file stores
        // local axes, so a write that forgot the inverse would put the texture
        // somewhere else the moment the brush was rotated.
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.9f);
        node.LocalPosition = new Vector3(30f, 0f, -12f);

        Apply(node, new PropertyEdit
        {
            Id = PropertyId.FaceRotation,
            Key = Key(2),
            Number = 20f,
        }).ShouldBe(1);

        List<PropertyRow> rows = [];
        NodeInspector.Describe(node, rows, null, pickedPlane: 2);

        foreach (PropertyRow row in rows)
        {
            if (row.Id == PropertyId.FaceRotation)
                row.Number.ShouldBe(20f, 1e-2f);
        }
    }
}
