using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The one line of live state a wiring view shows on a card: the entity's
/// headline, its first row when it gives none, and nothing for an entity
/// with no state.
/// </summary>
public sealed class LogicStateCaptureTests
{
    private readonly Scene _scene = new("States");
    private readonly EntityHeadline _headline = new();
    private readonly List<KeyValuePair<string, string>> _rows = [];

    [Fact]
    public void A_class_that_gives_a_headline_is_read_by_it()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "headlined");
        EntityWorld world = Started();

        LogicEntityState state = Capture(world, door.Id).ShouldHaveSingleItem();

        state.ShouldBe(new LogicEntityState(door.Id, "opening", "3 of 9 ticks"));
    }

    [Fact]
    public void A_class_with_rows_and_no_headline_is_read_by_its_first_row()
    {
        SceneNode counter = EntityRuntime.Place(_scene.Root, "counter", "rows_only");
        EntityWorld world = Started();

        Capture(world, counter.Id).ShouldHaveSingleItem()
            .ShouldBe(new LogicEntityState(counter.Id, "value", "7"));
    }

    [Fact]
    public void An_entity_with_no_state_and_an_id_nothing_has_are_left_out()
    {
        SceneNode relay = EntityRuntime.Place(_scene.Root, "relay", "relay");
        SceneNode door = EntityRuntime.Place(_scene.Root, "door", "headlined");
        EntityWorld world = Started();

        Capture(world, relay.Id, Guid.NewGuid(), door.Id)
            .Select(state => state.NodeId)
            .ShouldBe([door.Id]);
    }

    [Fact]
    public void A_headline_writer_drops_rows_and_a_row_writer_drops_the_headline()
    {
        var entity = new HeadlinedEntity();

        entity.DescribeState(new EntityStateWriter(_headline));
        _headline.IsSet.ShouldBeTrue();

        entity.DescribeState(new EntityStateWriter(_rows));
        _rows.ShouldBe([new KeyValuePair<string, string>("ticks travelled", "3")]);

        new EntityStateWriter(_headline).WantsHeadlineOnly.ShouldBeTrue();
        new EntityStateWriter(_rows).WantsHeadlineOnly.ShouldBeFalse();
    }

    private LogicEntityState[] Capture(EntityWorld world, params Guid[] ids) =>
        LogicStateCapture.Capture(world, ids, _headline, _rows);

    private EntityWorld Started()
    {
        EntityCatalog catalog = EntityRuntime.Catalog([]);
        catalog.Add(new EntitySchema("headlined"), () => new HeadlinedEntity());
        catalog.Add(new EntitySchema("rows_only"), () => new RowsOnlyEntity());

        var world = new EntityWorld(_scene, new CapturingLogger(), catalog);
        world.Activate();
        return world;
    }

    private sealed class HeadlinedEntity : Entity
    {
        public override void DescribeState(EntityStateWriter state)
        {
            state.Headline("opening", "3 of 9 ticks");
            state.Add("ticks travelled", 3);
        }
    }

    private sealed class RowsOnlyEntity : Entity
    {
        public override void DescribeState(EntityStateWriter state)
        {
            state.Add("value", 7);
            state.Add("max", 9);
        }
    }
}
