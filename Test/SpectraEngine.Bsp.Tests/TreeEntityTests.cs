using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// An entity lives while its node is in the level. One that comes in with a
/// clone starts as the entities of the level did, and one whose node leaves
/// is removed, each at the end of the tick.
/// </summary>
public sealed class TreeEntityTests
{
    private readonly TreeLevel _level = new();

    [Fact]
    public void An_entity_under_a_clone_starts_at_the_end_of_the_tick_its_clone_came_into_the_level()
    {
        TreeWrites tree = _level.Play();
        _level.Log.Clear();

        SceneNode copy = _level.Clone(tree, _level.Sign);
        _level.Tick();
        _level.Log.ShouldBeEmpty();

        TreeLevel.Parent(tree, copy, _level.Scene.Root);
        _level.Log.ShouldBeEmpty();
        _level.World.PendingSpawnCount.ShouldBe(1);
        _level.Tick();

        _level.Log.ShouldBe(["spawn:Sign", "activate:Sign"]);
        _level.World.Entities.Count.ShouldBe(2);
        EntityRuntime.Live(_level.World, copy).Node.ShouldBeSameAs(copy);
    }

    [Fact]
    public void Entities_that_come_in_together_are_all_named_before_the_first_one_spawns()
    {
        var log = new List<string>();
        var level = new TreeLevel(FindingEntity.AddTo(EntityMotion.Catalog(log), log));
        SceneNode rig = level.Scene.Root.CreateChild("Rig");
        EntityRuntime.Place(rig, "Finder", FindingEntity.Class);
        EntityRuntime.Place(rig, "Late", "lifecycle");
        TreeWrites tree = level.Play();
        SceneNode copy = level.Clone(tree, rig);
        copy.Children[1].Name = "Later";
        log.Clear();

        TreeLevel.Parent(tree, copy, level.Scene.Root);
        level.Tick();

        log.ShouldBe(["found Later at spawn", "spawn:Later", "activate:Later"]);
    }

    [Fact]
    public void An_entity_whose_node_leaves_the_level_is_removed_at_the_end_of_the_tick()
    {
        TreeWrites tree = _level.Play();
        _level.Log.Clear();

        TreeLevel.Parent(tree, _level.Sign, null);
        _level.Log.ShouldBeEmpty();
        _level.Tick();

        _level.Log.ShouldBe(["remove:Sign"]);
        _level.World.Entities.ShouldBeEmpty();

        TreeLevel.Parent(tree, _level.Sign, _level.Group);
        _level.Tick();

        _level.Log.ShouldBe(["remove:Sign", "spawn:Sign", "activate:Sign"]);
    }

    [Fact]
    public void An_entity_below_a_destroyed_node_is_removed_with_it()
    {
        SceneNode sign = EntityRuntime.Place(_level.Crate, "Label", "lifecycle");
        TreeWrites tree = _level.Play();
        _level.Log.Clear();

        _level.Destroy(tree, _level.Group);
        _level.Tick();

        _level.Log.ShouldBe(["remove:Label"]);
        _level.World.Index.ShouldNotBeNull().TryGetByNodeId(sign.Id, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_node_that_leaves_and_comes_back_in_one_tick_gets_a_new_entity()
    {
        TreeWrites tree = _level.Play();
        Entity first = EntityRuntime.Live(_level.World, _level.Sign);
        _level.Log.Clear();

        TreeLevel.Parent(tree, _level.Sign, null);
        TreeLevel.Parent(tree, _level.Sign, _level.Scene.Root);
        _level.Tick();

        _level.Log.ShouldBe(["remove:Sign", "spawn:Sign", "activate:Sign"]);
        EntityRuntime.Live(_level.World, _level.Sign).ShouldNotBeSameAs(first);
        _level.World.Entities.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_clone_that_comes_and_goes_in_one_tick_gets_no_entity()
    {
        TreeWrites tree = _level.Play();
        SceneNode copy = _level.Clone(tree, _level.Sign);
        _level.Log.Clear();

        TreeLevel.Parent(tree, copy, _level.Scene.Root);
        _level.Destroy(tree, copy);
        _level.Tick();

        _level.Log.ShouldBeEmpty();
        _level.World.Entities.ShouldHaveSingleItem();
    }

    [Fact]
    public void An_input_queued_for_an_entity_whose_node_left_is_not_sent()
    {
        var log = new List<string>();
        var level = new TreeLevel(EntityMotion.Catalog(log));
        SceneNode sink = EntityRuntime.Place(level.Scene.Root, "sink", "recorder");
        TreeWrites tree = level.Play();
        level.World.QueueInput(EntityRuntime.Live(level.World, sink), "Hello");

        level.Destroy(tree, sink);
        level.Tick();
        level.Tick();

        log.ShouldNotContain(line => line.StartsWith("sink:Hello", StringComparison.Ordinal));
    }

    // Looks for a sibling by name when it spawns, which finds one only when
    // every entity that came with it is in the index by then.
    private sealed class FindingEntity : Entity
    {
        public const string Class = "finder";

        private readonly List<string> _log;

        private FindingEntity(List<string> log) => _log = log;

        public static EntityCatalog AddTo(EntityCatalog catalog, List<string> log)
        {
            catalog.Add(new EntitySchema(Class), () => new FindingEntity(log));
            return catalog;
        }

        protected internal override void OnSpawn()
        {
            var found = new List<Entity>();
            World.Index?.Resolve("Later", this, null, null, found);
            if (found.Count > 0)
                _log.Add("found Later at spawn");
        }
    }
}
