using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The two bounds on what scripts add to a level's tree: how many nodes they
/// made are still there, and how deep a tree goes. Each refusal names its bound.
/// </summary>
public sealed class TreeLimitTests
{
    private const string TooMany =
        "scripts would have made more than 16384 nodes that are still there, and a level holds no more. " +
        "Destroy the ones that are done with";

    private readonly TreeLevel _level = new();

    [Fact]
    public void Scripts_make_so_many_nodes_and_no_more_until_one_is_destroyed()
    {
        TreeWrites tree = _level.Play();
        SceneNode last = _level.Make(tree, "Folder");
        for (int i = 1; i < TreeLimits.MaxMadeNodes; i++)
            last = _level.Make(tree, "Folder");

        tree.TryMake("Folder", out SceneNode? over, out string? refusal).ShouldBeFalse();

        over.ShouldBeNull();
        refusal.ShouldBe($"Instance.new made nothing: {TooMany}");
        tree.MadeCount.ShouldBe(TreeLimits.MaxMadeNodes);

        _level.Destroy(tree, last);

        _level.Make(tree, "Folder");
        tree.MadeCount.ShouldBe(TreeLimits.MaxMadeNodes);
    }

    [Fact]
    public void A_clone_that_would_pass_the_bound_is_refused_whole()
    {
        for (int i = 0; i < 40; i++)
            _level.Crate.CreateChild($"Bolt{i}");
        TreeWrites tree = _level.Play();
        // The group is 43 nodes: itself, two parts and the bolts.
        for (int i = 0; i < TreeLimits.MaxMadeNodes - 42; i++)
            _level.Make(tree, "Folder");

        tree.TryClone(_level.Group, _level.Copies, out SceneNode? copy, out string? refusal).ShouldBeFalse();

        copy.ShouldBeNull();
        _level.Copies.ShouldBeEmpty();
        refusal.ShouldBe($"Group was not cloned: with its copy, {TooMany}");
        tree.MadeCount.ShouldBe(TreeLimits.MaxMadeNodes - 42);

        // The crate with its bolts is 41 nodes, and those fit.
        _level.Clone(tree, _level.Crate);
        tree.MadeCount.ShouldBe(TreeLimits.MaxMadeNodes - 1);
    }

    [Fact]
    public void A_tree_goes_so_deep_and_no_deeper()
    {
        TreeWrites tree = _level.Play();
        SceneNode deepest = _level.Scene.Root;
        for (int level = 2; level <= TreeLimits.MaxDepth; level++)
        {
            SceneNode next = _level.Make(tree, "Folder");
            TreeLevel.Parent(tree, next, deepest);
            deepest = next;
        }

        TreeLimits.LevelOf(deepest).ShouldBe(TreeLimits.MaxDepth);
        SceneNode over = _level.Make(tree, "Folder");

        TreeLevel.Refused(tree, over, deepest).ShouldBe(
            "Folder cannot get Folder as its parent: that would make the tree more than 256 nodes deep, " +
            "which is as deep as a tree may be");
        TreeLevel.Parent(tree, over, deepest.Parent);
    }

    [Fact]
    public void A_chain_built_outside_the_level_is_bound_the_same_and_so_is_what_it_is_put_under()
    {
        TreeWrites tree = _level.Play();
        SceneNode top = _level.Make(tree, "Folder");
        SceneNode deepest = top;
        for (int level = 2; level <= TreeLimits.MaxDepth; level++)
        {
            SceneNode next = _level.Make(tree, "Folder");
            TreeLevel.Parent(tree, next, deepest);
            deepest = next;
        }

        TreeLevel.Refused(tree, _level.Make(tree, "Folder"), deepest).ShouldContain("more than 256 nodes deep");
        TreeLevel.Refused(tree, top, _level.Scene.Root).ShouldContain("more than 256 nodes deep");

        // The crate is three levels down, so 253 more fit under it and 254 do not.
        SceneNode third = top.Children[0].Children[0];
        TreeLevel.Refused(tree, third, _level.Crate).ShouldContain("more than 256 nodes deep");
        TreeLevel.Parent(tree, third.Children[0], _level.Crate);

        tree.IsInLevel(deepest).ShouldBeTrue();
        TreeLimits.LevelOf(deepest).ShouldBe(TreeLimits.MaxDepth);
    }

    [Fact]
    public void A_height_is_counted_no_deeper_than_it_is_asked_about()
    {
        var top = new SceneNode("Top");
        SceneNode at = top;
        for (int i = 0; i < 5_000; i++)
            at = at.CreateChild("Link");

        TreeLimits.HeightUpTo(top, 5).ShouldBe(6);
        TreeLimits.HeightUpTo(at, 5).ShouldBe(1);
        TreeLimits.HeightUpTo(at.Parent.ShouldNotBeNull(), 5).ShouldBe(2);
        TreeLimits.HeightUpTo(top, 0).ShouldBe(1);
    }

    [Fact]
    public void A_made_tree_no_script_can_reach_is_forgotten_and_one_in_the_level_is_not()
    {
        TreeWrites tree = _level.Play();
        SceneNode loose = _level.Make(tree, "Folder");
        TreeLevel.Parent(tree, _level.Make(tree, "Part"), loose);
        SceneNode kept = _level.Make(tree, "Part");
        TreeLevel.Parent(tree, kept, _level.Group);

        tree.Forget(kept);
        tree.MadeCount.ShouldBe(3);

        tree.Forget(loose);
        tree.MadeCount.ShouldBe(1);
    }
}
