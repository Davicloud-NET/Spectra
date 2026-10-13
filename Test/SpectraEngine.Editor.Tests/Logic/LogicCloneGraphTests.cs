using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell.Logic;
using System;
using System.Linq;
using static SpectraEngine.Editor.Tests.Logic.LogicFixture;

namespace SpectraEngine.Editor.Tests.Logic;

/// <summary>
/// A clone a script made while the level plays has the names of its source.
/// The view draws a wire of the clone where the level sends it: to the
/// entity that was copied with the sender, when one has the name.
/// </summary>
public sealed class LogicCloneGraphTests
{
    private static readonly Guid Button = Id(1);
    private static readonly Guid Door = Id(2);
    private static readonly Guid ButtonCopy = Id(3);
    private static readonly Guid DoorCopy = Id(4);

    [Fact]
    public void A_wire_of_a_clone_is_drawn_to_the_entity_of_the_same_clone_alone()
    {
        LogicGraph graph = Gates();

        LogicEdge edge = graph.Edges.Single(edge => edge.From.NodeId == ButtonCopy);
        edge.To.NodeId.ShouldBe(DoorCopy);
    }

    [Fact]
    public void A_wire_of_the_level_is_drawn_to_every_entity_of_the_name_and_the_clone_with_them()
    {
        LogicGraph graph = Gates();

        graph.Edges.Where(edge => edge.From.NodeId == Button).Select(edge => edge.To.NodeId)
            .ShouldBe([Door, DoorCopy], ignoreOrder: true);
    }

    [Fact]
    public void A_wire_of_a_clone_that_names_nothing_of_the_clone_is_drawn_to_the_level()
    {
        LogicGraph graph = Graph(
            Entity(1, "Button", "func_button", Wire("OnPressed", "Door", "Open")),
            Entity(2, "Door", "func_door"),
            Entity(3, "Button", "func_button", Wire("OnPressed", "Door", "Open")) with { CopyGroup = 1 });

        LogicEdge edge = graph.Edges.Single(edge => edge.From.NodeId == ButtonCopy);
        edge.To.NodeId.ShouldBe(Door);
    }

    // A button wired to a door, and a clone of the two.
    private static LogicGraph Gates() => Graph(
        Entity(1, "Button", "func_button", Wire("OnPressed", "Door", "Open")),
        Entity(2, "Door", "func_door"),
        Entity(3, "Button", "func_button", Wire("OnPressed", "Door", "Open")) with { CopyGroup = 1 },
        Entity(4, "Door", "func_door") with { CopyGroup = 1 });
}
