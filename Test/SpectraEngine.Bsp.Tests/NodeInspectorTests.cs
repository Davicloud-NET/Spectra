using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The rows a property panel shows for a node. Groups are derived from the
/// payloads the node carries.
/// </summary>
public sealed class NodeInspectorTests
{
    private static List<PropertyRow> Describe(SceneNode node)
    {
        var rows = new List<PropertyRow>();
        NodeInspector.Describe(node, rows);
        return rows;
    }

    private static IEnumerable<string> GroupsOf(List<PropertyRow> rows) =>
        rows.Select(r => r.Group).Distinct();

    [Fact]
    public void A_bare_node_has_only_the_groups_it_can_have()
    {
        var node = new SceneNode("Empty");

        List<PropertyRow> rows = Describe(node);

        GroupsOf(rows).ShouldBe([NodeInspector.NodeGroup, NodeInspector.TransformGroup],
            "a node with no payload must not grow a section for one");
    }

    [Fact]
    public void A_brush_node_grows_a_brush_group_and_a_light_node_a_light_group()
    {
        var brushNode = new SceneNode("Wall")
        {
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), default),
        };
        var lightNode = new SceneNode("Sun") { Light = new Light() };

        GroupsOf(Describe(brushNode)).ShouldContain(NodeInspector.BrushGroup);
        GroupsOf(Describe(brushNode)).ShouldNotContain(NodeInspector.LightGroup);
        GroupsOf(Describe(lightNode)).ShouldContain(NodeInspector.LightGroup);
        GroupsOf(Describe(lightNode)).ShouldNotContain(NodeInspector.BrushGroup);
    }

    [Fact]
    public void A_node_carrying_two_payloads_grows_both_groups()
    {
        var node = new SceneNode("Lamppost")
        {
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), default),
            Light = new Light(),
        };

        GroupsOf(Describe(node)).ShouldBe(
        [
            NodeInspector.NodeGroup,
            NodeInspector.TransformGroup,
            NodeInspector.BrushGroup,
            NodeInspector.LightGroup,

            // Last: the panel lays out in PropertyId order.
            NodeInspector.MaterialGroup,
        ]);
    }

    [Fact]
    public void Rows_of_a_group_are_contiguous_so_the_panel_can_group_by_run()
    {
        var node = new SceneNode("Lamppost")
        {
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), default),
            Light = new Light(),
        };

        List<PropertyRow> rows = Describe(node);

        var seen = new List<string>();
        string? current = null;
        foreach (PropertyRow row in rows)
        {
            if (row.Group == current) continue;
            seen.Contains(row.Group).ShouldBeFalse($"'{row.Group}' appears in more than one run");
            seen.Add(row.Group);
            current = row.Group;
        }
    }

    [Fact]
    public void The_two_declared_bits_are_shown_on_the_objects_that_own_them()
    {
        // Kind is on the node, operation on the brush.
        var node = new SceneNode("Doorway")
        {
            BrushKind = BrushKind.Part,
            Brush = Brush.CreateBox(new Vector3(-1f), new Vector3(1f), default)
                .WithOperation(BrushOperation.Subtractive),
        };

        List<PropertyRow> rows = Describe(node);

        rows.Single(r => r.Id == PropertyId.BrushKind).Text.ShouldBe("Part");
        rows.Single(r => r.Id == PropertyId.BrushOperation).Text.ShouldBe("Subtractive");
    }

    [Fact]
    public void Rotation_is_shown_as_degrees_a_person_can_type()
    {
        var node = new SceneNode("Turned")
        {
            LocalRotation = new EulerAngles(Yaw: 90f, Pitch: 0f, Roll: 0f).ToQuaternion(),
        };

        PropertyRow rotation = Describe(node).Single(r => r.Id == PropertyId.Rotation);

        rotation.Kind.ShouldBe(PropertyKind.Vector3);
        rotation.Vector.Y.ShouldBe(90f, 0.01f, "Y is yaw in the display vector");
        rotation.Vector.X.ShouldBe(0f, 0.01f);
    }

    [Fact]
    public void Brush_size_is_the_measurement_the_resize_gesture_works_in()
    {
        var node = new SceneNode("Slab")
        {
            Brush = Brush.CreateBox(new Vector3(-3f, -0.5f, -2f), new Vector3(3f, 0.5f, 2f), default),
        };

        PropertyRow size = Describe(node).Single(r => r.Id == PropertyId.BrushSize);

        size.Vector.X.ShouldBe(6f, 0.001f);
        size.Vector.Y.ShouldBe(1f, 0.001f);
        size.Vector.Z.ShouldBe(4f, 0.001f);
    }

    [Fact]
    public void An_id_is_shown_and_cannot_be_edited()
    {
        var node = new SceneNode("Thing");

        PropertyRow id = Describe(node).Single(r => r.Id == PropertyId.NodeId);

        id.IsEditable.ShouldBeFalse("a node's identity is what every command addresses it by");
        id.Text.ShouldBe(node.Id.ToString("D"));
    }

    [Fact]
    public void A_mesh_node_says_where_its_geometry_came_from()
    {
        var node = new SceneNode("Crate");
        node.MeshSource = new MeshSource("Models/crate.obj", 2);

        List<PropertyRow> rows = Describe(node);

        rows.Single(r => r.Id == PropertyId.MeshModel).Text.ShouldBe("Models/crate.obj");
        rows.Single(r => r.Id == PropertyId.MeshSubmesh).Text.ShouldBe("2");
    }

    [Fact]
    public void Describe_reuses_the_list_it_is_given()
    {
        // Called once per published snapshot, so it must not allocate a list.
        var node = new SceneNode("Thing");
        var rows = new List<PropertyRow> { PropertyRow.ReadOnly("stale", "stale", PropertyId.None, "stale") };

        NodeInspector.Describe(node, rows);

        rows.ShouldNotContain(r => r.Group == "stale");
    }

    [Fact]
    public void A_choice_row_carries_the_options_it_can_take()
    {
        var node = new SceneNode("Sun") { Light = new Light { Kind = LightKind.Point } };

        PropertyRow kind = Describe(node).Single(r => r.Id == PropertyId.LightKind);

        kind.Kind.ShouldBe(PropertyKind.Choice);
        kind.Text.ShouldBe("Point");

        // Checked against the enum so a new kind cannot be left out.
        kind.Choices!.Count.ShouldBe(Enum.GetValues<LightKind>().Length);

        foreach (LightKind value in Enum.GetValues<LightKind>())
            kind.Choices.ShouldContain(value.ToString());
    }

    [Fact]
    public void A_lights_shape_rows_are_the_ones_that_shape_reads()
    {
        List<PropertyRow> spot = Describe(
            new SceneNode("Spot") { Light = new Light { Kind = LightKind.Spot } });

        spot.ShouldContain(r => r.Id == PropertyId.LightOuterAngle);
        spot.ShouldNotContain(r => r.Id == PropertyId.LightWidth);

        List<PropertyRow> panel = Describe(
            new SceneNode("Panel") { Light = new Light { Kind = LightKind.Rect } });

        panel.ShouldContain(r => r.Id == PropertyId.LightWidth);
        panel.ShouldContain(r => r.Id == PropertyId.LightHeight);
        panel.ShouldNotContain(r => r.Id == PropertyId.LightRadius);

        List<PropertyRow> disc = Describe(
            new SceneNode("Disc") { Light = new Light { Kind = LightKind.Disc } });

        disc.ShouldContain(r => r.Id == PropertyId.LightRadius);
        disc.ShouldNotContain(r => r.Id == PropertyId.LightOuterAngle);

        // Range is validated for every kind, so it is always shown.
        List<PropertyRow> sun = Describe(
            new SceneNode("Sun") { Light = new Light { Kind = LightKind.Directional } });

        sun.ShouldContain(r => r.Id == PropertyId.LightRange);
        sun.ShouldNotContain(r => r.Id == PropertyId.LightWidth);
    }
}
