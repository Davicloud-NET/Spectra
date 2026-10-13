using Microsoft.Extensions.Logging.Abstractions;
using SpectraEngine.Core.Hosting;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The scene tree's <c>tag:</c> filter over a real scene: it finds the nodes
/// built with a tag, and follows the tags as they are edited.
/// </summary>
public sealed class SceneTreeTagFilterTests
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

        public SceneNode Add(string name, params string[] tags) =>
            Scene.Root.AddChild(new SceneNode(name) { Tags = TagSet.Of(tags) });

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

    [Fact]
    public void A_tag_filter_finds_the_nodes_with_that_tag_in_any_case()
    {
        var rig = new Rig();
        SceneNode pool = rig.Add("Pool", "Lava");
        SceneNode brick = rig.Add("Brick", "Hot stuff", "Lava");
        SceneNode ice = rig.Add("Ice", "Cold");
        SceneNode plain = rig.Add("Lava lamp");
        rig.Publish();

        rig.Tree.ApplyFilter("tag:lava");

        rig.MatchOf(pool).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(ice).ShouldBe(SceneTreeMatch.None);
        rig.MatchOf(plain).ShouldBe(SceneTreeMatch.None, "a name is not a tag");
        rig.Tree.MatchCount.ShouldBe(2);
        rig.Tree.FilterIsUnknown.ShouldBeFalse();

        rig.Tree.ApplyFilter("TAG: Lava ");
        rig.Tree.MatchCount.ShouldBe(2);
    }

    [Fact]
    public void A_part_of_a_tag_finds_it_so_the_list_narrows_while_the_word_is_typed()
    {
        var rig = new Rig();
        SceneNode brick = rig.Add("Brick", "Hot stuff");
        SceneNode pool = rig.Add("Pool", "Lava");
        rig.Publish();

        rig.Tree.ApplyFilter("tag:stu");
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(pool).ShouldBe(SceneTreeMatch.None);

        rig.Tree.ApplyFilter("tag:hot stuff");
        rig.Tree.MatchCount.ShouldBe(1);
    }

    [Fact]
    public void The_prefix_alone_finds_every_node_that_has_a_tag()
    {
        var rig = new Rig();
        SceneNode tagged = rig.Add("Pool", "Lava");
        SceneNode plain = rig.Add("Plain");
        rig.Publish();

        rig.Tree.ApplyFilter("tag:");

        rig.MatchOf(tagged).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(plain).ShouldBe(SceneTreeMatch.None);
        rig.Tree.MatchCount.ShouldBe(1);
    }

    // The filter reads a node's name and its tags, and never the name of an
    // attribute. So the period, the hyphen and the slash a name may have
    // are nothing it meets.
    [Fact]
    public void An_attributes_name_is_nothing_the_filter_reads_whatever_characters_it_has()
    {
        var rig = new Rig();
        SceneNode brick = rig.Add("Brick", "Lava");
        brick.Attributes = AttributeSet.Empty
            .With("ui/hud.title-2", MemberValue.Of(1d))
            .With("a.b", MemberValue.Of(2d));
        rig.Publish();

        foreach (string filter in (string[])["ui/hud.title-2", "a.b", "hud", "tag:a.b", "tag:ui/hud", "tag:title-2"])
        {
            rig.Tree.ApplyFilter(filter);
            rig.Tree.MatchCount.ShouldBe(0, filter);
        }

        rig.Tree.ApplyFilter("tag:lava");
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.Match);
    }

    [Fact]
    public void A_tag_nothing_has_matches_nothing_and_is_no_unknown_kind()
    {
        var rig = new Rig();
        rig.Add("Pool", "Lava");
        rig.Publish();

        rig.Tree.ApplyFilter("tag:ice");

        rig.Tree.MatchCount.ShouldBe(0);
        rig.Tree.FilterIsUnknown.ShouldBeFalse();
    }

    [Fact]
    public void The_filter_follows_a_tag_that_is_put_on_and_taken_off()
    {
        var rig = new Rig();
        SceneNode brick = rig.Add("Brick");
        rig.Publish();
        rig.Tree.ApplyFilter("tag:lava");
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.None);

        brick.Tags = TagSet.Of("Lava");
        rig.Publish();
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.Match);

        brick.Tags = TagSet.Empty;
        rig.Publish();
        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.None);
    }

    [Fact]
    public void A_tagged_node_deep_in_the_tree_keeps_the_nodes_above_it_in_view()
    {
        var rig = new Rig();
        SceneNode group = rig.Add("Group");
        SceneNode brick = group.AddChild(new SceneNode("Brick") { Tags = TagSet.Of("Lava") });
        rig.Publish();

        rig.Tree.ApplyFilter("tag:lava");

        rig.MatchOf(brick).ShouldBe(SceneTreeMatch.Match);
        rig.MatchOf(group).ShouldBe(SceneTreeMatch.Ancestor);
    }

    [Fact]
    public void A_filter_by_kind_still_reads_as_one()
    {
        var rig = new Rig();
        SceneNode lamp = rig.Add("Lamp", "Lava");
        lamp.Light = new Light { Kind = LightKind.Point };
        rig.Publish();

        rig.Tree.ApplyFilter("t:light");
        rig.MatchOf(lamp).ShouldBe(SceneTreeMatch.Match);

        rig.Tree.ApplyFilter("t:nothing");
        rig.Tree.FilterIsUnknown.ShouldBeTrue();
    }

    [Theory]
    [InlineData("tag:lava", "lava")]
    [InlineData("Tag:  Hot stuff ", "Hot stuff")]
    [InlineData("TAG:", "")]
    public void A_filter_that_starts_with_the_prefix_asks_for_a_tag(string filter, string wanted)
    {
        SceneTreeTagFilter.TryRead(filter, out string read).ShouldBeTrue();

        read.ShouldBe(wanted);
    }

    [Theory]
    [InlineData("t:light")]
    [InlineData("lava")]
    [InlineData("a tag: lava")]
    [InlineData("")]
    public void Any_other_filter_asks_for_no_tag(string filter)
    {
        SceneTreeTagFilter.TryRead(filter, out _).ShouldBeFalse();
    }
}
