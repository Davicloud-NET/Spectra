using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>Which face of a brush a ray hit, and the picked face as selection state.</summary>
// A face is named by its plane index. Polygons are compacted, face surfaces are not.
public sealed class FacePickTests
{
    // CreateBox plane order is +X, -X, +Y, -Y, +Z, -Z. Maps record it.
    private static Scene SceneWithBox(out SceneNode node)
    {
        var scene = new Scene("Test");
        node = scene.Root.CreateChild("Box");
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        return scene;
    }

    [Theory]
    [InlineData(2f, 0f, 0f, 0)]
    [InlineData(-2f, 0f, 0f, 1)]
    [InlineData(0f, 2f, 0f, 2)]
    [InlineData(0f, -2f, 0f, 3)]
    [InlineData(0f, 0f, 2f, 4)]
    [InlineData(0f, 0f, -2f, 5)]
    public void Each_face_of_a_box_reports_its_own_plane(float x, float y, float z, int expected)
    {
        Scene scene = SceneWithBox(out _);

        var from = new Vector3(x, y, z);
        var ray = new Ray3(from, Vector3.Normalize(-from));

        scene.Raycast(in ray, out SceneRaycastHit hit).ShouldBeTrue();
        hit.PlaneIndex.ShouldBe(expected);
    }

    [Fact]
    public void A_mesh_hit_reports_no_plane()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("Prop");
        node.MeshRenderer = new MeshRenderer(
            SpatialTestHelpers.CreateCubeMesh(1f), SpatialTestHelpers.NoopMaterial);

        var ray = new Ray3(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);

        scene.Raycast(in ray, out SceneRaycastHit hit).ShouldBeTrue();

        // -1, not 0: 0 is a real plane.
        hit.PlaneIndex.ShouldBe(-1);
    }

    [Fact]
    public void A_rotated_brush_reports_the_plane_in_its_own_space()
    {
        Scene scene = SceneWithBox(out SceneNode node);

        // A quarter turn about +Y takes local +X to world -Z, so the face
        // looking at a camera on +Z is the local -X one: plane 1.
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.Pi * 0.5f);

        var ray = new Ray3(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);

        scene.Raycast(in ray, out SceneRaycastHit hit).ShouldBeTrue();
        hit.PlaneIndex.ShouldBe(1);

        // Control: the unrotated box answers 4 for the same ray.
        node.LocalRotation = Quaternion.Identity;
        scene.Raycast(in ray, out hit).ShouldBeTrue();
        hit.PlaneIndex.ShouldBe(4);
    }

    [Fact]
    public void A_face_can_be_picked_only_on_the_sole_selected_brush()
    {
        Scene scene = SceneWithBox(out SceneNode box);
        SceneNode other = scene.Root.CreateChild("Other");
        other.Brush = Brush.CreateBox(new Vector3(4f), new Vector3(6f));

        // Nothing selected yet.
        scene.Selection.SelectFace(box, 2).ShouldBeFalse();

        scene.Selection.Select(box);
        scene.Selection.SelectFace(box, 2).ShouldBeTrue();
        scene.Selection.FaceNode.ShouldBeSameAs(box);
        scene.Selection.FacePlane.ShouldBe(2);

        // A face index means nothing across two brushes.
        scene.Selection.Add(other);
        scene.Selection.FacePlane.ShouldBe(-1);
        scene.Selection.SelectFace(box, 2).ShouldBeFalse();
    }

    [Fact]
    public void A_plane_outside_the_brush_is_refused()
    {
        Scene scene = SceneWithBox(out SceneNode box);
        scene.Selection.Select(box);

        scene.Selection.SelectFace(box, 6).ShouldBeFalse();
        scene.Selection.SelectFace(box, -1).ShouldBeFalse();
        scene.Selection.FacePlane.ShouldBe(-1);
    }

    [Fact]
    public void Any_selection_change_clears_the_picked_face()
    {
        Scene scene = SceneWithBox(out SceneNode box);
        SceneNode other = scene.Root.CreateChild("Other");

        scene.Selection.Select(box);
        scene.Selection.SelectFace(box, 4).ShouldBeTrue();

        scene.Selection.Select(other);
        scene.Selection.FaceNode.ShouldBeNull();
        scene.Selection.FacePlane.ShouldBe(-1);
    }

    [Fact]
    public void Clearing_and_removing_both_clear_the_picked_face()
    {
        Scene scene = SceneWithBox(out SceneNode box);

        scene.Selection.Select(box);
        scene.Selection.SelectFace(box, 0).ShouldBeTrue();
        scene.Selection.Clear();
        scene.Selection.FacePlane.ShouldBe(-1);

        scene.Selection.Select(box);
        scene.Selection.SelectFace(box, 0).ShouldBeTrue();
        scene.Selection.Deselect(box);
        scene.Selection.FacePlane.ShouldBe(-1);
    }

    [Fact]
    public void A_plane_maps_to_the_polygon_it_produced()
    {
        Brush box = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        for (int i = 0; i < box.LocalPlanes.Count; i++)
        {
            box.TryGetPlaneFace(i, out Polygon face).ShouldBeTrue();

            Vector3.Dot(face.Surface.Normal, box.LocalPlanes[i].Normal).ShouldBe(1f, 1e-4f);
        }

        box.TryGetPlaneFace(6, out _).ShouldBeFalse();
        box.TryGetPlaneFace(-1, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_plane_every_other_plane_clips_away_reports_no_face()
    {
        // A box plus a plane far outside it, which produces no polygon.
        Plane[] planes =
        [
            new(new Vector3(1f, 0f, 0f), -1f),
            new(new Vector3(-1f, 0f, 0f), -1f),
            new(new Vector3(0f, 1f, 0f), -1f),
            new(new Vector3(0f, -1f, 0f), -1f),
            new(new Vector3(0f, 0f, 1f), -1f),
            new(new Vector3(0f, 0f, -1f), -1f),
            new(Vector3.Normalize(new Vector3(1f, 1f, 1f)), -50f),
        ];

        var brush = new Brush(planes, Matrix4x4.Identity);

        brush.FaceSurfaces.Count.ShouldBe(7);
        brush.TryGetPlaneFace(6, out _).ShouldBeFalse();
        brush.TryGetPlaneFace(0, out _).ShouldBeTrue();
    }

    [Fact]
    public void Painting_every_face_returns_the_same_instance_when_nothing_changes()
    {
        Brush box = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        MaterialRef wall = MaterialRegistry.Intern("Materials/wall.spectramat");

        Brush painted = box.WithAllFacesMaterial(wall);
        painted.ShouldNotBeSameAs(box);

        // The carve and the part-mesh cache detect change by brush reference.
        painted.WithAllFacesMaterial(wall).ShouldBeSameAs(painted);
    }

    [Fact]
    public void Painting_every_face_keeps_the_operation()
    {
        Brush cut = Brush.CreateBox(new Vector3(-1f), new Vector3(1f))
            .WithOperation(BrushOperation.Subtractive);

        Brush painted = cut.WithAllFacesMaterial(
            MaterialRegistry.Intern("Materials/wall.spectramat"));

        painted.Operation.ShouldBe(BrushOperation.Subtractive);
    }
}
