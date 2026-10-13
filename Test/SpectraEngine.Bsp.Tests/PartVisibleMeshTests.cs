using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The GPU mesh of a part a script hides and shows while a level plays. The
/// part keeps it until Stop, so a part that blinks builds its mesh once.
/// </summary>
public sealed class PartVisibleMeshTests
{
    private readonly Scene _scene = new("Blink") { StaticWorldMaterial = SpatialTestHelpers.NoopMaterial };
    private readonly FakeRenderer _renderer = new();

    [Fact]
    public void A_part_a_script_hides_keeps_its_mesh_and_is_not_drawn()
    {
        SceneNode part = Part();
        var writes = new ScriptWrites(Started());

        Show(writes, part, false);

        _renderer.LiveMeshes.Count.ShouldBe(1);
        _scene.PartBrushMeshCount.ShouldBe(1);
        _scene.DrawableNodes.ShouldNotContain(part);
        _scene.HiddenBrushNodes.ShouldBe([part]);
    }

    [Fact]
    public void A_part_that_blinks_builds_its_mesh_once()
    {
        SceneNode part = Part();
        var writes = new ScriptWrites(Started());

        for (int blink = 0; blink < 20; blink++)
        {
            Show(writes, part, false);
            Show(writes, part, true);
        }

        _renderer.CreatedMeshes.Count.ShouldBe(1);
        _scene.DrawableNodes.ShouldContain(part);
    }

    [Fact]
    public void A_part_built_hidden_gets_a_mesh_when_a_script_shows_it_and_keeps_it_while_it_blinks()
    {
        SceneNode zone = Part(isRendered: false);
        _renderer.CreatedMeshes.ShouldBeEmpty();
        var writes = new ScriptWrites(Started());

        for (int blink = 0; blink < 20; blink++)
        {
            Show(writes, zone, true);
            Show(writes, zone, false);
        }

        _renderer.CreatedMeshes.Count.ShouldBe(1);
        _renderer.LiveMeshes.Count.ShouldBe(1);
    }

    [Fact]
    public void Stop_lets_go_of_the_mesh_of_a_part_that_was_built_hidden()
    {
        SceneNode zone = Part(isRendered: false);
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);
        Show(writes, zone, true);
        Show(writes, zone, false);

        world.Deactivate();
        _scene.ProcessPartBrushMeshes(_renderer);

        zone.IsRendered.ShouldBeFalse();
        _renderer.LiveMeshes.ShouldBeEmpty("a hidden part must not keep a GPU mesh once the level has stopped");
        _scene.PartBrushMeshCount.ShouldBe(0);
    }

    [Fact]
    public void Stop_shows_a_part_a_script_hid_with_the_mesh_it_had()
    {
        SceneNode part = Part();
        EntityWorld world = Started();
        Show(new ScriptWrites(world), part, false);

        world.Deactivate();
        _scene.ProcessPartBrushMeshes(_renderer);

        part.IsRendered.ShouldBeTrue();
        _renderer.CreatedMeshes.Count.ShouldBe(1);
        _renderer.LiveMeshes.Count.ShouldBe(1);
        _scene.DrawableNodes.ShouldContain(part);
    }

    // After Stop the part is the editor's again, where a hidden part holds none.
    [Fact]
    public void A_part_hidden_after_the_level_stopped_holds_no_mesh()
    {
        SceneNode part = Part();
        EntityWorld world = Started();
        var writes = new ScriptWrites(world);
        Show(writes, part, false);
        Show(writes, part, true);
        world.Deactivate();

        part.IsRendered = false;
        _scene.ProcessPartBrushMeshes(_renderer);

        _renderer.LiveMeshes.ShouldBeEmpty();
    }

    [Fact]
    public void A_hidden_part_that_leaves_the_level_while_it_plays_lets_go_of_its_mesh()
    {
        SceneNode part = Part();
        var writes = new ScriptWrites(Started());
        Show(writes, part, false);

        _scene.Root.RemoveChild(part);
        _scene.ProcessPartBrushMeshes(_renderer);
        _renderer.LiveMeshes.ShouldBeEmpty();

        _scene.Root.AddChild(part);
        _scene.ProcessPartBrushMeshes(_renderer);
        _renderer.LiveMeshes.ShouldBeEmpty("it came back hidden, as a part that was built hidden");
    }

    // A mesh node holds its mesh whatever the switch says, so there is
    // nothing to keep for it.
    [Fact]
    public void A_mesh_a_script_hides_only_leaves_the_list_of_what_is_drawn()
    {
        SceneNode prop = SpatialTestHelpers.CreateMeshNode(_scene.Root, "Prop", Vector3.Zero);
        EntityWorld world = Started();

        Show(new ScriptWrites(world), prop, false);
        _scene.DrawableNodes.ShouldNotContain(prop);

        world.Deactivate();
        _scene.DrawableNodes.ShouldContain(prop);
        _scene.PartBrushMeshCount.ShouldBe(0);
    }

    // A part with its mesh built, or with none when it is built hidden.
    private SceneNode Part(bool isRendered = true)
    {
        SceneNode part = EntityMotion.BrushNode(_scene.Root, "Crate", BrushKind.Part, Vector3.Zero);
        part.IsRendered = isRendered;
        _scene.ProcessPartBrushMeshes(_renderer);
        return part;
    }

    private EntityWorld Started()
    {
        var world = new EntityWorld(_scene, new CapturingLogger(), EntityMotion.Catalog([]));
        world.Activate();
        return world;
    }

    // One write and the frame after it, which is when a mesh is built or let go.
    private void Show(ScriptWrites writes, SceneNode node, bool visible)
    {
        writes.TryWrite(node, SettableMemberId.Visible, MemberValue.Of(visible), out string? refusal).ShouldBeTrue(refusal);
        _scene.ProcessPartBrushMeshes(_renderer);
    }
}
