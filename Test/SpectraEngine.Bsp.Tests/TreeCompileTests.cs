using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a script adds to a level and takes from it costs no compile of the
/// static world, and neither does Stop when it puts the tree back.
/// </summary>
public sealed class TreeCompileTests
{
    private readonly TreeLevel _level = new();
    private readonly FakeRenderer _renderer = new();

    [Fact]
    public void A_part_that_is_spawned_moved_cloned_and_destroyed_leaves_the_static_world_as_it_is()
    {
        _level.Scene.RebuildStaticWorld(_renderer);
        int compiles = _level.Scene.StaticWorldCompileCount;
        TreeWrites tree = _level.Play();

        for (int tick = 0; tick < 30; tick++)
        {
            SceneNode part = _level.Make(tree, "Part");
            part.LocalPosition = new Vector3(tick, 1f, 0f);
            TreeLevel.Parent(tree, part, tick % 2 == 0 ? _level.Scene.Root : _level.Wall);
            TreeLevel.Parent(tree, _level.Clone(tree, _level.Group), _level.Scene.Root);
            if (tick % 3 == 0)
                _level.Destroy(tree, part);

            _level.Tick();
            _level.Scene.ProcessStaticWorldCompilation(_renderer, new CapturingLogger());
        }

        _level.Scene.StaticWorldDirty.ShouldBeFalse();
        _level.Scene.StaticWorldCompileCount.ShouldBe(compiles);
    }

    [Fact]
    public void Stop_puts_the_tree_back_and_no_world_brush_leaves_the_scene_for_it()
    {
        _level.Scene.RebuildStaticWorld(_renderer);
        int compiles = _level.Scene.StaticWorldCompileCount;
        var left = new List<SceneNode>();
        TreeWrites tree = _level.Play();
        TreeLevel.Parent(tree, _level.Make(tree, "Part"), _level.Scene.Root);
        TreeLevel.Parent(tree, _level.Group, null);
        TreeLevel.Parent(tree, _level.Lamp, _level.Wall);
        _level.Destroy(tree, _level.Sign);
        _level.Scene.NodeRemoved += left.Add;

        _level.World.Deactivate();
        _level.Scene.ProcessStaticWorldCompilation(_renderer, new CapturingLogger());

        left.ShouldNotContain(_level.Wall);
        _level.Scene.Root.Children.ShouldBe([_level.Wall, _level.Group, _level.Lamp, _level.Sign]);
        _level.Scene.StaticWorldDirty.ShouldBeFalse();
        _level.Scene.StaticWorldCompileCount.ShouldBe(compiles);
    }
}
