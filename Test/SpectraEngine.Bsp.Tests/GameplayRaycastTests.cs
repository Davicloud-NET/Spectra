using System;
using System.Numerics;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>The gameplay ray: carved world plus part brushes, matching what is drawn.</summary>
public sealed class GameplayRaycastTests
{
    [Fact]
    public void A_shot_through_a_carved_doorway_passes_through()
    {
        World world = Room();

        var ray = new Ray3(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.False(world.Scene.RaycastGameplay(in ray, out GameplayRayHit hit, 8f),
            $"the ray should have passed through the doorway, but stopped at {hit.Point} " +
            $"(static world: {hit.StaticWorld})");
    }

    [Fact]
    public void The_authored_geometry_raycast_still_reports_the_doorway_as_solid()
    {
        // Not a bug: Scene.Raycast answers about authored brushes, which is
        // what an editor pick needs.
        World world = Room();
        var ray = new Ray3(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.True(world.Scene.Raycast(in ray, out SceneRaycastHit hit, 8f));
        Assert.Equal("wall", hit.Node.Name);
    }

    [Fact]
    public void A_shot_beside_the_doorway_stops_at_the_wall()
    {
        World world = Room();

        // Sideways past the opening's edge.
        var ray = new Ray3(new Vector3(2f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.True(world.Scene.RaycastGameplay(in ray, out GameplayRayHit hit, 8f));
        Assert.True(hit.StaticWorld, "the wall is world geometry");
        Assert.Null(hit.Node);
        Assert.Equal(-4f, hit.Point.Z, 1);
        Assert.True(hit.Normal.Z > 0.9f, $"the wall's near face should face +z, got {hit.Normal}");
    }

    [Fact]
    public void A_world_hit_reports_the_material_of_the_face_it_struck()
    {
        // The BSP reports the plane crossed, not the polygon, so a regression
        // here falls back to the default material without failing.
        World world = Room();
        var ray = new Ray3(new Vector3(2f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.True(world.Scene.RaycastGameplay(in ray, out GameplayRayHit hit, 8f));
        Assert.Equal(world.WallMaterial, hit.Material);
    }

    [Fact]
    public void A_part_brush_is_hit_and_reports_itself()
    {
        // Parts are not in the compile, so only the live lane can hit them.
        World world = Room();
        world.AddPart("crate", new Vector3(0f, 1f, -2f), new Vector3(0.5f, 0.5f, 0.5f));

        var ray = new Ray3(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.True(world.Scene.RaycastGameplay(in ray, out GameplayRayHit hit, 8f));
        Assert.False(hit.StaticWorld);
        Assert.Equal("crate", hit.Node?.Name);
    }

    [Fact]
    public void The_nearer_of_the_two_lanes_wins()
    {
        World near = Room();
        near.AddPart("crate", new Vector3(2f, 1f, -2f), new Vector3(0.5f, 0.5f, 0.5f));
        var ray = new Ray3(new Vector3(2f, 1f, 0f), new Vector3(0f, 0f, -1f));

        Assert.True(near.Scene.RaycastGameplay(in ray, out GameplayRayHit nearHit, 8f));
        Assert.Equal("crate", nearHit.Node?.Name);

        World far = Room();
        far.AddPart("crate", new Vector3(2f, 1f, -6f), new Vector3(0.5f, 0.5f, 0.5f));

        Assert.True(far.Scene.RaycastGameplay(in ray, out GameplayRayHit farHit, 8f));
        Assert.True(farHit.StaticWorld, "the wall is nearer than a part behind it");
    }

    [Fact]
    public void A_node_the_filter_ignores_is_not_hit()
    {
        World world = Room();
        SceneNode crate = world.AddPart("crate", new Vector3(0f, 1f, -2f), new Vector3(0.5f, 0.5f, 0.5f));

        var ray = new Ray3(new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, -1f));
        var filter = new SceneQueryFilter { Ignore = [crate] };

        Assert.False(world.Scene.RaycastGameplay(in ray, out _, in filter, 8f),
            "with the crate ignored the ray should go through the doorway again");
    }

    [Fact]
    public void A_ray_that_reaches_nothing_reports_no_hit()
    {
        World world = Room();
        var ray = new Ray3(new Vector3(0f, 40f, 0f), Vector3.UnitY);

        Assert.False(world.Scene.RaycastGameplay(in ray, out _, 8f));
    }

    // A room whose north wall has a doorway cut by a subtractive brush.
    private sealed class World
    {
        public Scene Scene { get; } = new("GameplayRayTest");

        public MaterialRef WallMaterial { get; } = MaterialRegistry.Intern("Materials/test_wall.spectramat");

        public void Compile() => Scene.RebuildStaticWorld(new FakeRenderer());

        public void AddBox(string name, Vector3 center, Vector3 half, MaterialRef material = default)
        {
            SceneNode node = Scene.Root.CreateChild(name);
            node.LocalPosition = center;
            node.Brush = Brush.CreateBox(-half, half, material);
        }

        public void AddCut(string name, Vector3 center, Vector3 half)
        {
            SceneNode node = Scene.Root.CreateChild(name);
            node.LocalPosition = center;
            node.Brush = Brush.CreateBox(-half, half).WithOperation(BrushOperation.Subtractive);
        }

        public SceneNode AddPart(string name, Vector3 center, Vector3 half)
        {
            SceneNode node = Scene.Root.CreateChild(name);
            node.LocalPosition = center;
            node.BrushKind = BrushKind.Part;
            node.Brush = Brush.CreateBox(-half, half);
            return node;
        }
    }

    private static World Room()
    {
        var world = new World();

        // Wall at z = -4, half a unit thick, with a 2 x 2.4 opening cut flush:
        // the cut's z planes coincide with the wall's.
        world.AddBox("wall", new Vector3(0f, 1.5f, -4.25f), new Vector3(6f, 1.5f, 0.25f), world.WallMaterial);
        world.AddCut("door", new Vector3(0f, 1.2f, -4.25f), new Vector3(1f, 1.2f, 0.25f));
        // Floor is 0.01 below the doorway's bottom plane as a workaround: a
        // coplanar contact seals the opening (see CoplanarCutSealingTests).
        world.AddBox("floor", new Vector3(0f, -0.51f, 0f), new Vector3(6f, 0.5f, 6f));

        world.Compile();
        return world;
    }
}
