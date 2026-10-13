using System.Numerics;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a change to a level's tree owes the rest of the level: a node below
/// a destroyed one keeps its place, a made node counts once, a clone's wire
/// stays in the clone, and an entity hears of a brush that comes or goes.
/// </summary>
public sealed class TreeUpkeepTests
{
    private readonly TreeLevel _level = new();

    [Fact]
    public void A_node_below_a_destroyed_node_stays_where_it_stood_and_Stop_puts_it_back()
    {
        byte[] built = _level.Saved();
        TreeWrites tree = _level.Play();
        Vector3 stood = _level.Crate.WorldPosition;
        CFrame turned = _level.Crate.CFrame;

        _level.Destroy(tree, _level.Group);

        _level.Crate.Parent.ShouldBeNull();
        Vector3.Distance(_level.Crate.WorldPosition, stood).ShouldBeLessThan(1e-4f);
        _level.Crate.CFrame.FuzzyEq(turned, 1e-4f).ShouldBeTrue();

        _level.World.Deactivate();

        _level.Saved().ShouldBe(built);
    }

    [Fact]
    public void A_destroyed_node_counts_again_when_a_script_is_found_to_hold_it_and_is_forgotten_once()
    {
        TreeWrites tree = _level.Play();
        SceneNode made = _level.Make(tree, "Part");
        TreeLevel.Parent(tree, made, _level.Scene.Root);

        _level.Destroy(tree, made);
        tree.MadeCount.ShouldBe(0);

        tree.CountAgain(made).ShouldBeTrue();
        tree.CountAgain(made).ShouldBeFalse();
        tree.MadeCount.ShouldBe(1);

        tree.Forget(made);
        tree.Forget(made);
        tree.MadeCount.ShouldBe(0);
        tree.CountAgain(_level.Crate).ShouldBeFalse("the level was built with it");
    }

    [Fact]
    public void A_made_tree_that_was_forgotten_and_is_then_destroyed_is_taken_off_the_count_once()
    {
        TreeWrites tree = _level.Play();
        SceneNode kept = _level.Make(tree, "Folder");
        SceneNode loose = _level.Make(tree, "Folder");
        TreeLevel.Parent(tree, _level.Make(tree, "Part"), loose);

        tree.Forget(loose);
        _level.Destroy(tree, loose);

        tree.MadeCount.ShouldBe(1);
        tree.CountAgain(kept).ShouldBeFalse("it never stopped counting");
    }

    [Fact]
    public void A_name_an_entity_of_a_clone_asks_for_is_the_entity_that_was_copied_with_it()
    {
        SceneNode rig = _level.Scene.Root.CreateChild("Rig");
        SceneNode caller = EntityRuntime.Place(rig, "Caller", "lifecycle");
        SceneNode hearer = EntityRuntime.Place(rig, "Hearer", "lifecycle");
        TreeWrites tree = _level.Play();
        SceneNode copy = _level.Clone(tree, rig);
        TreeLevel.Parent(tree, copy, _level.Scene.Root);
        _level.Tick();

        Named("Hearer", copy.Children[0]).ShouldBe([copy.Children[1]]);
        Named("Hear*", copy.Children[0]).ShouldBe([copy.Children[1]]);
        Named("Sign", copy.Children[0]).ShouldBe([_level.Sign], "nothing of the clone has the name");
        Named("Hearer", caller).ShouldBe([hearer, copy.Children[1]], "a wire of the level reaches every one");
        Named("Hearer", null).ShouldBe([hearer, copy.Children[1]]);
    }

    [Fact]
    public void A_clone_of_a_clone_is_a_copy_of_its_own()
    {
        SceneNode rig = _level.Scene.Root.CreateChild("Rig");
        EntityRuntime.Place(rig, "Caller", "lifecycle");
        EntityRuntime.Place(rig, "Hearer", "lifecycle");
        TreeWrites tree = _level.Play();
        SceneNode first = _level.Clone(tree, rig);
        TreeLevel.Parent(tree, first, _level.Scene.Root);
        SceneNode second = _level.Clone(tree, first);
        TreeLevel.Parent(tree, second, _level.Scene.Root);
        _level.Tick();

        Named("Hearer", first.Children[0]).ShouldBe([first.Children[1]]);
        Named("Hearer", second.Children[0]).ShouldBe([second.Children[1]]);
    }

    [Fact]
    public void An_entity_is_told_when_a_brush_comes_under_it_or_leaves_it_and_not_of_a_node_with_none()
    {
        var log = new List<string>();
        var level = new TreeLevel(CountingEntity.AddTo(EntityMotion.Catalog(log), log));
        SceneNode owner = EntityRuntime.Place(level.Scene.Root, "Owner", CountingEntity.Class);
        TreeWrites tree = level.Play();
        log.Clear();

        TreeLevel.Parent(tree, level.Crate, owner);
        TreeLevel.Parent(tree, level.Lamp, owner);
        log.ShouldBe(["Owner owns 1"]);

        SceneNode made = level.Make(tree, "Part");
        TreeLevel.Parent(tree, made, level.Crate);
        log.ShouldBe(["Owner owns 1", "Owner owns 2"]);

        level.Destroy(tree, level.Crate);
        log.ShouldBe(["Owner owns 1", "Owner owns 2", "Owner owns 0"]);
    }

    private List<SceneNode> Named(string name, SceneNode? asker)
    {
        var found = new List<Entity>();
        Entity? self = asker is null ? null : EntityRuntime.Live(_level.World, asker);
        _level.World.Index.ShouldNotBeNull().Resolve(name, self, null, null, found);
        return [.. found.Select(entity => entity.Node)];
    }

    // Says how many brushes it owns each time it is told they changed.
    private sealed class CountingEntity : Entity
    {
        public const string Class = "counter_of_brushes";

        private readonly List<string> _log;

        private CountingEntity(List<string> log) => _log = log;

        public static EntityCatalog AddTo(EntityCatalog catalog, List<string> log)
        {
            catalog.Add(new EntitySchema(Class), () => new CountingEntity(log));
            return catalog;
        }

        protected internal override void OnOwnedBrushesChanged() =>
            _log.Add($"{TargetName} owns {CollectOwnedBrushes().Count}");
    }
}
