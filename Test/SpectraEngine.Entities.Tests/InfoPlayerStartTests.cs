using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Entities.Tests;

public sealed class InfoPlayerStartTests
{
    [Fact]
    public void A_start_placed_in_a_level_is_one_the_engine_can_find()
    {
        var scene = new Scene("Entities");
        EntityRuntime.Place(scene.Root, "relay", "logic_relay");
        SceneNode node = EntityRuntime.Place(scene.Root, "start", "info_player_start");

        var world = new EntityWorld(scene, new CapturingLogger(), EntityRuntime.Catalog([]));
        world.Activate();

        Entity start = world.Entities.Single(entity => entity is IPlayerStart);
        start.ShouldBeOfType<InfoPlayerStart>();
        start.Node.ShouldBeSameAs(node);
    }

    [Fact]
    public void The_schema_is_a_point_with_nothing_to_set_or_send()
    {
        EntitySchema schema = InfoPlayerStart.SpectraSchema;

        schema.ClassName.ShouldBe("info_player_start");
        schema.DisplayName.ShouldBe("Player start");
        schema.Group.ShouldBe("Player");
        schema.Placement.ShouldBe(EntityPlacement.Point);
        schema.Keyvalues.ShouldBeEmpty();
        schema.Inputs.ShouldBeEmpty();
        schema.Outputs.ShouldBeEmpty();
    }
}
