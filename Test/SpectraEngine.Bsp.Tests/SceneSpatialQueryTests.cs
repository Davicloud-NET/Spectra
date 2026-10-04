using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using static SpectraEngine.Bsp.Tests.SpatialTestHelpers;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// <see cref="Scene.Raycast"/> against analytic hits and <see cref="Scene.QueryFrustum"/>
/// against brute force.
/// </summary>
public sealed class SceneSpatialQueryTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void Raycast_hits_a_mesh_cube_at_the_exact_analytic_distance()
    {
        var scene = new Scene("Test");
        SceneNode cube = CreateMeshNode(scene.Root, "cube", new Vector3(10f, 0f, 0f));

        var ray = new Ray3(Vector3.Zero, Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(cube);
        hit.Distance.ShouldBe(9.5f, Tolerance);
        hit.Point.X.ShouldBe(9.5f, Tolerance);
        hit.Point.Y.ShouldBe(0f, Tolerance);
        hit.Point.Z.ShouldBe(0f, Tolerance);
        hit.Normal.X.ShouldBe(-1f, Tolerance);
        hit.Normal.Y.ShouldBe(0f, Tolerance);
        hit.Normal.Z.ShouldBe(0f, Tolerance);
    }

    [Fact]
    public void Raycast_hits_a_brush_box_at_the_exact_analytic_distance()
    {
        var scene = new Scene("Test");
        SceneNode wall = CreateBrushNode(scene.Root, "wall", new Vector3(10f, 0f, 0f));

        var ray = new Ray3(Vector3.Zero, Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(wall);
        hit.Distance.ShouldBe(9.5f, Tolerance);
        hit.Normal.X.ShouldBe(-1f, Tolerance);
        hit.Normal.Y.ShouldBe(0f, Tolerance);
        hit.Normal.Z.ShouldBe(0f, Tolerance);
    }

    [Fact]
    public void Raycast_respects_max_distance()
    {
        var scene = new Scene("Test");
        CreateMeshNode(scene.Root, "cube", new Vector3(10f, 0f, 0f));

        var ray = new Ray3(Vector3.Zero, Vector3.UnitX);
        scene.Raycast(ray, out _, maxDistance: 5f).ShouldBeFalse();
        scene.Raycast(ray, out _, maxDistance: 20f).ShouldBeTrue();
    }

    [Fact]
    public void Raycast_misses_cleanly()
    {
        var scene = new Scene("Test");
        CreateMeshNode(scene.Root, "cube", new Vector3(10f, 0f, 0f));
        CreateBrushNode(scene.Root, "wall", new Vector3(0f, 10f, 0f));

        scene.Raycast(new Ray3(Vector3.Zero, -Vector3.UnitX), out _).ShouldBeFalse();
        scene.Raycast(new Ray3(Vector3.Zero, Vector3.UnitZ), out _).ShouldBeFalse();
    }

    [Fact]
    public void Raycast_picks_the_nearest_of_two_aligned_nodes_despite_fat_box_ordering()
    {
        var scene = new Scene("Test");
        // Far node inserted first. Its fat box (min x = 9.9) is entered before
        // the near node's real hit at x = 10, so pruning on box-entry distance
        // would pick the wrong node.
        SceneNode far = CreateMeshNode(scene.Root, "far", new Vector3(10.6f, 0f, 0f));
        SceneNode near = CreateMeshNode(scene.Root, "near", new Vector3(10.5f, 0f, 0f));

        var ray = new Ray3(Vector3.Zero, Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(near);
        far.ShouldNotBeSameAs(hit.Node);
        hit.Distance.ShouldBe(10f, Tolerance);
    }

    [Fact]
    public void Raycast_reports_the_rotated_brush_normal_in_world_space()
    {
        // 2x2x2 brush rotated 45 degrees about Y. Its local +X face becomes
        // n = (cos45, 0, -sin45), n.p = 1; a -X ray from (5, 0, -0.3) enters
        // it at t = 5.3 - sqrt(2).
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("rotated");
        node.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4f);
        node.Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f));

        var ray = new Ray3(new Vector3(5f, 0f, -0.3f), -Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        float sqrt2Over2 = MathF.Sqrt(2f) / 2f;
        hit.Node.ShouldBeSameAs(node);
        hit.Distance.ShouldBe(5.3f - MathF.Sqrt(2f), Tolerance);
        hit.Normal.X.ShouldBe(sqrt2Over2, Tolerance);
        hit.Normal.Y.ShouldBe(0f, Tolerance);
        hit.Normal.Z.ShouldBe(-sqrt2Over2, Tolerance);
    }

    [Fact]
    public void Raycast_handles_scaled_mesh_nodes()
    {
        // A unit cube scaled by 2 spans [-1, 1], so a -X ray from x = 5 hits at t = 4.
        var scene = new Scene("Test");
        SceneNode node = CreateMeshNode(scene.Root, "scaled", Vector3.Zero);
        node.LocalScale = new Vector3(2f);

        var ray = new Ray3(new Vector3(5f, 0f, 0f), -Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(node);
        hit.Distance.ShouldBe(4f, Tolerance);
        hit.Normal.X.ShouldBe(1f, Tolerance);
        hit.Normal.Y.ShouldBe(0f, Tolerance);
        hit.Normal.Z.ShouldBe(0f, Tolerance);
    }

    [Fact]
    public void Raycast_handles_scaled_brush_nodes()
    {
        // The graph allows scale on brush nodes; only the static-world snapshot
        // rejects it. A 0.5 half-extent box scaled by 2 spans [-1, 1], so the
        // hit is at t = 4. A rigid-inverse shortcut would give t = 4.5 and a
        // normal of length 2.
        var scene = new Scene("Test");
        SceneNode node = CreateBrushNode(scene.Root, "scaled", Vector3.Zero);
        node.LocalScale = new Vector3(2f);

        var ray = new Ray3(new Vector3(5f, 0f, 0f), -Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(node);
        hit.Distance.ShouldBe(4f, Tolerance);
        hit.Point.X.ShouldBe(1f, Tolerance);
        hit.Normal.Length().ShouldBe(1f, Tolerance);
        hit.Normal.X.ShouldBe(1f, Tolerance);
        hit.Normal.Y.ShouldBe(0f, Tolerance);
        hit.Normal.Z.ShouldBe(0f, Tolerance);
    }

    [Fact]
    public void Raycast_falls_back_to_the_unit_box_for_meshes_without_cpu_geometry()
    {
        var scene = new Scene("Test");
        SceneNode node = scene.Root.CreateChild("gpu-only");
        node.LocalPosition = new Vector3(3f, 0f, 0f);
        node.MeshRenderer = new MeshRenderer(new FakeMesh([], []), NoopMaterial);

        var ray = new Ray3(Vector3.Zero, Vector3.UnitX);
        scene.Raycast(ray, out SceneRaycastHit hit).ShouldBeTrue();

        hit.Node.ShouldBeSameAs(node);
        hit.Distance.ShouldBe(2.5f, Tolerance);
        hit.Normal.X.ShouldBe(-1f, Tolerance);
    }

    [Fact]
    public void Raycast_on_an_empty_scene_returns_false()
    {
        var scene = new Scene("Test");
        scene.Raycast(new Ray3(Vector3.Zero, Vector3.UnitX), out _).ShouldBeFalse();
    }

    [Fact]
    public void QueryFrustum_matches_brute_force_over_a_grid_for_several_camera_poses()
    {
        var scene = new Scene("Test");
        var allNodes = new List<SceneNode>();
        int i = 0;
        for (int x = -2; x <= 2; x++)
        for (int y = -2; y <= 2; y++)
        for (int z = -2; z <= 2; z++)
        {
            var position = new Vector3(x * 4f, y * 4f, z * 4f);
            allNodes.Add(i++ % 2 == 0
                ? CreateMeshNode(scene.Root, $"m{i}", position)
                : CreateBrushNode(scene.Root, $"b{i}", position));
        }

        var results = new List<SceneNode>();
        bool sawPartialView = false;
        foreach (Camera camera in CameraPoses())
        {
            Frustum frustum = camera.GetFrustum();

            var expected = allNodes.Where(n => frustum.Intersects(WorldBoundsOf(n))).ToHashSet();
            expected.ShouldNotBeEmpty($"pose at {camera.Position} sees nothing — bad test setup");
            if (expected.Count < allNodes.Count)
                sawPartialView = true;

            results.Clear();
            scene.QueryFrustum(frustum, results);

            results.Count.ShouldBe(expected.Count,
                $"pose at {camera.Position}: BVH count differs from brute force");
            results.ToHashSet().SetEquals(expected).ShouldBeTrue(
                $"pose at {camera.Position}: BVH set differs from brute force");
        }

        sawPartialView.ShouldBeTrue("every pose saw the whole grid — the oracle comparison proved nothing");
    }

    [Fact]
    public void QueryFrustum_appends_without_clearing_the_callers_list()
    {
        var scene = new Scene("Test");
        CreateMeshNode(scene.Root, "cube", Vector3.Zero);
        Camera camera = new() { Position = new Vector3(0f, 0f, 10f) };

        var results = new List<SceneNode>();
        scene.QueryFrustum(camera.GetFrustum(), results);
        scene.QueryFrustum(camera.GetFrustum(), results);

        results.Count.ShouldBe(2);
    }

    [Fact]
    public void QueryFrustum_stays_correct_while_nodes_move()
    {
        var scene = new Scene("Test");
        SceneNode mover = CreateMeshNode(scene.Root, "mover", new Vector3(0f, 0f, -5f));
        Camera camera = new() { Position = new Vector3(0f, 0f, 10f) }; // looking down -Z
        Frustum frustum = camera.GetFrustum();

        var results = new List<SceneNode>();
        scene.QueryFrustum(frustum, results);
        results.ShouldBe(new[] { mover });

        // Far behind the camera. A stale fat box must not keep it visible.
        mover.LocalPosition = new Vector3(0f, 0f, 500f);
        results.Clear();
        scene.QueryFrustum(frustum, results);
        results.ShouldBeEmpty();

        mover.LocalPosition = new Vector3(2f, 1f, -20f);
        results.Clear();
        scene.QueryFrustum(frustum, results);
        results.ShouldBe(new[] { mover });
    }

    private static IEnumerable<Camera> CameraPoses()
    {
        yield return new Camera { Position = new Vector3(0f, 0f, 30f), Yaw = -MathF.PI / 2f };
        yield return new Camera { Position = new Vector3(30f, 6f, 0f), Yaw = MathF.PI };
        var lookAt = new Camera { Position = new Vector3(25f, 25f, 25f) };
        lookAt.LookAt(Vector3.Zero);
        yield return lookAt;
        yield return new Camera { Position = new Vector3(3f, 2f, 12f), Yaw = -MathF.PI / 2f, Pitch = -0.4f };
        yield return new Camera
        {
            Position = new Vector3(0f, 0f, 60f),
            Yaw = -MathF.PI / 2f,
            FieldOfView = MathF.PI / 12f,
        };
    }
}
