using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The scene tree's <c>t:script</c> filter over a real scene: it finds every
/// node that carries a script, whatever else the node is.
/// </summary>
public sealed class SceneTreeScriptFilterTests
{
    // A scene whose changes are logged, and the tree built from what the log
    // hands over.
    private sealed class Rig
    {
        private readonly SceneChangeLog _log = new();
        private long _frame;

        public Rig()
        {
            _log.Observe(Scene);
            _log.Drain();
        }

        public Scene Scene { get; } = new("Tree");

        public SceneTreeModel Tree { get; } = new(new EngineHost(NullLogger.Instance), NullLogger.Instance);

        public SceneNode Add(SceneNode node) => Scene.Root.AddChild(node);

        public void Publish()
        {
            (IReadOnlyList<SceneChange> changes, bool overflowed) = _log.Drain();
            overflowed.ShouldBeFalse();
            Tree.ApplyChanges(new FrameSnapshot { FrameNumber = ++_frame, Changes = changes });
        }

        public SceneTreeMatch MatchOf(SceneNode node)
        {
            Tree.TryGetNode(node.Id, out SceneTreeNode row).ShouldBeTrue(node.Name);
            return row.Match;
        }
    }

    private static SceneNode Part(string name) => new(name)
    {
        BrushKind = BrushKind.Part,
        Brush = Brush.CreateBox(-Vector3.One, Vector3.One),
    };

    [Fact]
    public void A_part_that_carries_a_script_is_found_as_a_script_and_as_a_part()
    {
        var rig = new Rig();
        SceneNode door = rig.Add(Part("Door"));
        door.Script = new Script("Scripts/door.luau");
        SceneNode crate = rig.Add(Part("Crate"));
        SceneNode opener = rig.Add(new SceneNode("Opener") { Script = new Script("Scripts/opener.luau") });
        rig.Publish();

        rig.Tree.ApplyFilter("t:script");
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(opener).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(crate).ShouldBe(SceneTreeMatch.None);
        rig.Tree.MatchCount.ShouldBe(2);

        rig.Tree.ApplyFilter("t:part");
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(crate).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(opener).ShouldBe(SceneTreeMatch.None);
    }

    [Fact]
    public void A_light_and_a_door_that_carry_scripts_are_found_too()
    {
        var rig = new Rig();
        SceneNode lamp = rig.Add(new SceneNode("Lamp")
        {
            Light = new Light { Kind = LightKind.Point },
            Script = new Script("Scripts/flicker.luau"),
        });
        SceneNode door = rig.Add(Part("Door"));
        door.Entity = new EntityData("func_door");
        door.Script = new Script("Scripts/door.luau");
        rig.Publish();

        rig.Tree.ApplyFilter("t:script");

        rig.MatchOf(lamp).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.Match);
    }

    [Fact]
    public void A_part_is_found_once_it_gets_a_script_and_not_after_it_loses_it()
    {
        var rig = new Rig();
        SceneNode door = rig.Add(Part("Door"));
        rig.Publish();
        rig.Tree.ApplyFilter("t:script");
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.None);

        door.Script = new Script("Scripts/door.luau");
        rig.Publish();
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.Match);

        door.Script = null;
        rig.Publish();
        rig.MatchOf(door).ShouldBe(SceneTreeMatch.None);
    }
}
