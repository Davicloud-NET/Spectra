using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The Material and Face sections of the property panel.</summary>
public sealed class FaceInspectorTests
{
    private const string Wall = "Materials/wall.spectramat";
    private const string Floor = "Materials/floor.spectramat";

    private static SceneNode BoxNode(Scene scene, string name = "Block")
    {
        SceneNode node = scene.Root.CreateChild(name);
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return node;
    }

    private static List<PropertyRow> Describe(SceneNode node, int pickedPlane = -1)
    {
        List<PropertyRow> rows = [];
        NodeInspector.Describe(node, rows, null, pickedPlane);
        return rows;
    }

    private static PropertyRow Row(List<PropertyRow> rows, PropertyId id, string key = "")
    {
        foreach (PropertyRow row in rows)
        {
            if (row.Id == id && string.Equals(row.Key ?? "", key, System.StringComparison.Ordinal))
                return row;
        }

        throw new Xunit.Sdk.XunitException($"No row for {id} keyed '{key}'.");
    }

    [Fact]
    public void A_brush_grows_a_material_row_naming_what_every_face_wears()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        node.Brush = node.Brush!.WithAllFacesMaterial(MaterialRegistry.Intern(Wall));

        PropertyRow row = Row(Describe(node), PropertyId.BrushMaterial);

        row.Group.ShouldBe(NodeInspector.MaterialGroup);
        row.Kind.ShouldBe(PropertyKind.Asset);
        row.Asset.ShouldBe(AssetKind.Material);
        row.Text.ShouldBe(Wall);
        row.Note.ShouldBe("");
    }

    [Fact]
    public void An_unnamed_brush_reports_an_empty_path_rather_than_a_word()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        PropertyRow row = Row(Describe(node), PropertyId.BrushMaterial);

        // Empty, not "(default)": the text is the path an edit posts.
        row.Text.ShouldBe("");
    }

    [Fact]
    public void Faces_wearing_different_materials_report_a_mixed_note()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);
        node.Brush = node.Brush!
            .WithAllFacesMaterial(MaterialRegistry.Intern(Wall))
            .WithFaceMaterial(2, MaterialRegistry.Intern(Floor));

        PropertyRow row = Row(Describe(node), PropertyId.BrushMaterial);

        row.Note.ShouldBe("mixed (2 materials)");
        row.Text.ShouldBe("");
    }

    [Fact]
    public void Without_a_picked_face_there_is_no_face_section()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        foreach (PropertyRow row in Describe(node))
            row.Group.ShouldNotBe(NodeInspector.FaceGroup);
    }

    [Fact]
    public void A_picked_face_grows_a_face_section_keyed_by_its_plane()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        List<PropertyRow> rows = Describe(node, pickedPlane: 2);
        string key = 2.ToString(CultureInfo.InvariantCulture);

        // Each row carries the plane index as its key, so an edit names a face.
        Row(rows, PropertyId.FaceMaterial, key).Asset.ShouldBe(AssetKind.Material);
        Row(rows, PropertyId.FaceAlignment, key).Text.ShouldBe("World");
        Row(rows, PropertyId.FaceUScale, key).Unit.ShouldBe("su/rep");
        Row(rows, PropertyId.FaceUOffset, key).Unit.ShouldBe("rep");
        Row(rows, PropertyId.FaceRotation, key).Unit.ShouldBe("deg");

        Row(rows, PropertyId.FaceIndex, key).Text.ShouldBe("+Y");
    }

    [Fact]
    public void A_plane_the_brush_does_not_have_grows_no_face_section()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        foreach (PropertyRow row in Describe(node, pickedPlane: 99))
            row.Group.ShouldNotBe(NodeInspector.FaceGroup);
    }

    [Fact]
    public void A_multi_selection_never_grows_a_face_section()
    {
        var scene = new Scene("Test");
        SceneNode a = BoxNode(scene, "A");
        SceneNode b = BoxNode(scene, "B");

        List<PropertyRow> rows = [];
        NodeInspector.Describe([a, b], rows, null, pickedPlane: 2);

        // A plane index means nothing across two brushes.
        foreach (PropertyRow row in rows)
            row.Group.ShouldNotBe(NodeInspector.FaceGroup);

        // The whole-brush row survives a multi-selection.
        Row(rows, PropertyId.BrushMaterial).Group.ShouldBe(NodeInspector.MaterialGroup);
    }

    [Fact]
    public void The_face_rows_report_the_frame_in_world_space()
    {
        var scene = new Scene("Test");
        SceneNode node = BoxNode(scene);

        // Rotated so the brush-local and world angles differ.
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f);
        Vector3 worldNormal = FaceAxes.WorldNormal(node.Brush!.LocalPlanes[2], node.WorldMatrix);

        FaceSurface local = node.Brush!.FaceSurfaces[2];
        FaceSurface turned = FaceAxes
            .WithRotation(local.Transformed(node.WorldMatrix), worldNormal, 30f);

        Matrix4x4.Invert(node.WorldMatrix, out Matrix4x4 inverse).ShouldBeTrue();
        node.Brush = node.Brush!.WithFaceSurface(2, turned.Transformed(inverse));

        List<PropertyRow> rows = Describe(node, pickedPlane: 2);
        string key = 2.ToString(CultureInfo.InvariantCulture);

        Row(rows, PropertyId.FaceRotation, key).Number.ShouldBe(30f, 1e-2f);
        Row(rows, PropertyId.FaceAlignment, key).Text.ShouldBe("Face");
    }

    [Fact]
    public void A_material_the_project_does_not_have_carries_a_missing_note()
    {
        var scene = new Scene("Test");
        var renderer = new FakeRenderer();
        var assets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        assets.AttachRenderer(renderer);
        scene.Assets = assets;

        try
        {
            const string Gone = "Materials/nobody_wrote_this.spectramat";

            SceneNode node = BoxNode(scene);
            node.Brush = node.Brush!.WithAllFacesMaterial(MaterialRegistry.Intern(Gone));

            // The note comes from the load cache, not a disk probe, so nothing
            // is reported until a load has been tried.
            Row(Describe(node), PropertyId.BrushMaterial).Note.ShouldBe("");

            assets.LoadMaterial(Gone);

            Row(Describe(node), PropertyId.BrushMaterial).Note.ShouldBe("missing");
        }
        finally
        {
            assets.ReleaseGraphicsResources();
        }
    }
}
