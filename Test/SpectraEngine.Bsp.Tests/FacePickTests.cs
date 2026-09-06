using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Which face of a brush a ray hit, and what the selection does with that
/// answer.
/// </summary>
/// <remarks>
/// <b>A plane index is the only handle a face has.</b> A brush's polygons are
/// compacted (a plane every other plane clips away produces none) while its face
/// surfaces are not, so the index that means "this face" everywhere else in the
/// engine is the PLANE's. A hit carrying the polygon's index instead would edit
/// the wrong surface on any brush with a clipped plane, silently, and the
/// symptom would be one wall in a level texturing itself when its neighbour was
/// painted.
/// </remarks>
public sealed class FacePickTests
{
    // CreateBox lays its planes out +X, -X, +Y, -Y, +Z, -Z. The order is not an
    // implementation detail here: the panel names a face by it and a map records
    // it, so a change to it is a change to saved data.
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

        // -1 rather than 0, because 0 is a real plane: a mesh reporting it would
        // let a paint land on "the first face" of a node that has no faces.
        hit.PlaneIndex.ShouldBe(-1);
    }

    [Fact]
    public void A_rotated_brush_reports_the_plane_in_its_own_space()
    {
        Scene scene = SceneWithBox(out SceneNode node);

        // A quarter turn about +Y takes the brush's local +X to world -Z, so
        // the face now looking at a camera on +Z is the local -X one: plane 1.
        // The index is the BRUSH's and does not follow the world, which is what
        // makes it a stable name for a face in a saved map.
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, float.Pi * 0.5f);

        var ray = new Ray3(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);

        scene.Raycast(in ray, out SceneRaycastHit hit).ShouldBeTrue();
        hit.PlaneIndex.ShouldBe(1);

        // And the unrotated box answers 4 for the same ray, which is what makes
        // this a test of the transform rather than of the ray.
        node.LocalRotation = Quaternion.Identity;
        scene.Raycast(in ray, out hit).ShouldBeTrue();
        hit.PlaneIndex.ShouldBe(4);
    }

    // --- The picked face, as selection state ---------------------------------

    [Fact]
    public void A_face_can_be_picked_only_on_the_sole_selected_brush()
    {
        Scene scene = SceneWithBox(out SceneNode box);
        SceneNode other = scene.Root.CreateChild("Other");
        other.Brush = Brush.CreateBox(new Vector3(4f), new Vector3(6f));

        // Nothing selected: a face has no brush to belong to.
        scene.Selection.SelectFace(box, 2).ShouldBeFalse();

        scene.Selection.Select(box);
        scene.Selection.SelectFace(box, 2).ShouldBeTrue();
        scene.Selection.FaceNode.ShouldBeSameAs(box);
        scene.Selection.FacePlane.ShouldBe(2);

        // A face index means nothing across two brushes: face 4 of one is not
        // face 4 of another, so the panel offers it for a single selection only.
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

        // Every path out of a selection funnels through one raise, so a face
        // cannot outlive the selection it was picked on: the panel would keep
        // showing a Face section for a brush that is no longer selected.
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

    // --- The polygon behind the plane ----------------------------------------

    [Fact]
    public void A_plane_maps_to_the_polygon_it_produced()
    {
        Brush box = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        for (int i = 0; i < box.LocalPlanes.Count; i++)
        {
            box.TryGetPlaneFace(i, out Polygon face).ShouldBeTrue();

            // The polygon's own plane is the one asked for, which is the whole
            // claim: the compacted list and the plane list are indexed
            // differently and the map is what joins them.
            Vector3.Dot(face.Surface.Normal, box.LocalPlanes[i].Normal).ShouldBe(1f, 1e-4f);
        }

        box.TryGetPlaneFace(6, out _).ShouldBeFalse();
        box.TryGetPlaneFace(-1, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_plane_every_other_plane_clips_away_reports_no_face()
    {
        // A box plus a plane far outside it: legitimate in the definition, and
        // it produces no polygon at all. False is the answer rather than an
        // exception, because the face still has a surface and a material.
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

    // --- Painting whole brushes ----------------------------------------------

    [Fact]
    public void Painting_every_face_returns_the_same_instance_when_nothing_changes()
    {
        Brush box = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));
        MaterialRef wall = MaterialRegistry.Intern("Materials/wall.spectramat");

        Brush painted = box.WithAllFacesMaterial(wall);
        painted.ShouldNotBeSameAs(box);

        // Reference identity is the change detector for the carve and the
        // part-mesh cache, so a no-op edit must not produce a new brush.
        painted.WithAllFacesMaterial(wall).ShouldBeSameAs(painted);
    }

    [Fact]
    public void Painting_every_face_keeps_the_operation()
    {
        Brush cut = Brush.CreateBox(new Vector3(-1f), new Vector3(1f))
            .WithOperation(BrushOperation.Subtractive);

        Brush painted = cut.WithAllFacesMaterial(
            MaterialRegistry.Intern("Materials/wall.spectramat"));

        // Dropping it would turn a doorway into a wall the next time somebody
        // retextured it, which is the failure WithFaceSurface already names.
        painted.Operation.ShouldBe(BrushOperation.Subtractive);
    }
}
