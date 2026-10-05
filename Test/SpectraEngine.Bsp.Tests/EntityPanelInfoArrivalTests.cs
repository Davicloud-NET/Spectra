using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// What a wiring panel is told beyond the selected entity's own wires: the
/// wires that arrive at it, and its live state while a level runs.
/// </summary>
public sealed class EntityPanelInfoArrivalTests
{
    private readonly Scene _scene = new("Arrivals");

    [Fact]
    public void Wires_from_other_entities_that_name_this_one_arrive()
    {
        SceneNode button = EntityRuntime.Place(_scene.Root, "Button", "recorder");
        SceneNode zone = EntityRuntime.Place(_scene.Root, "Zone", "recorder");
        SceneNode door = EntityRuntime.Place(_scene.Root, "Door", "recorder");
        EntityRuntime.Wire(button, "OnPressed", "Door", "Open");
        EntityRuntime.Wire(button, "OnPressed", "Lift", "Open");
        EntityRuntime.Wire(zone, "OnTrigger", "Do*", "Close");

        EntityPanelInfo info = Captured(door);

        info.Incoming.ShouldBe(
        [
            new EntityIncomingInfo(button.Id, "Button", "OnPressed", "Open"),
            new EntityIncomingInfo(zone.Id, "Zone", "OnTrigger", "Close"),
        ]);
        info.IncomingTruncated.ShouldBeFalse();
    }

    [Fact]
    public void A_wire_an_entity_sends_itself_arrives_by_name_and_by_token()
    {
        SceneNode lift = EntityRuntime.Place(_scene.Root, "Lift", "recorder");
        SceneNode other = EntityRuntime.Place(_scene.Root, "Other", "recorder");
        EntityRuntime.Wire(lift, "OnFullyOpen", "Lift", "Close");
        EntityRuntime.Wire(lift, "OnFullyClosed", TargetNameIndex.SelfToken, "Open");
        EntityRuntime.Wire(lift, "OnBlocked", TargetNameIndex.CallerToken, "Stop");

        // Someone else's !self is that someone, and the activator is unknown
        // until the level runs.
        EntityRuntime.Wire(other, "OnGo", TargetNameIndex.SelfToken, "Ping");
        EntityRuntime.Wire(other, "OnGo", TargetNameIndex.ActivatorToken, "Ping");

        Captured(lift).Incoming.Select(wire => wire.Input).ShouldBe(["Close", "Open", "Stop"]);
    }

    [Fact]
    public void An_entity_nothing_names_has_no_arrivals()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "Door", "recorder");
        EntityRuntime.Wire(door, "OnOpen", "Lamp", "TurnOn");

        Captured(door).Incoming.ShouldBeEmpty();
    }

    [Fact]
    public void More_arrivals_than_the_cap_are_cut_and_said_to_be()
    {
        SceneNode door = EntityRuntime.Place(_scene.Root, "Door", "recorder");
        SceneNode relay = EntityRuntime.Place(_scene.Root, "Relay", "recorder");
        for (int i = 0; i < EntityPanelInfo.MaxIncoming + 1; i++)
            EntityRuntime.Wire(relay, "OnTrigger", "Door", "Open");

        EntityPanelInfo info = Captured(door);

        info.Incoming.Count.ShouldBe(EntityPanelInfo.MaxIncoming);
        info.IncomingTruncated.ShouldBeTrue();
    }

    [Fact]
    public void State_is_empty_until_a_level_runs_and_then_holds_every_row()
    {
        SceneNode counter = EntityRuntime.Place(_scene.Root, "Counter", "counting");

        Captured(counter).State.ShouldBeEmpty();

        EntityCatalog catalog = EntityRuntime.Catalog([]);
        catalog.Add(new EntitySchema("counting"), () => new CountingEntity());
        var world = new EntityWorld(_scene, new CapturingLogger(), catalog);
        world.Activate();

        EntityPanelInfo.Capture(counter, null, _scene, null, world).ShouldNotBeNull().State.ShouldBe(
        [
            new KeyValuePair<string, string>("value", "7"),
            new KeyValuePair<string, string>("max", "9"),
        ]);

        world.Deactivate();

        EntityPanelInfo.Capture(counter, null, _scene, null, world).ShouldNotBeNull().State.ShouldBeEmpty();
    }

    private EntityPanelInfo Captured(SceneNode node) =>
        EntityPanelInfo.Capture(node, null, _scene).ShouldNotBeNull();

    private sealed class CountingEntity : Entity
    {
        public override void DescribeState(EntityStateWriter state)
        {
            state.Add("value", 7);
            state.Add("max", 9);
        }
    }
}
