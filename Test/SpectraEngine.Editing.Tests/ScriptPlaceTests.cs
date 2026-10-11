using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Gizmos;
using System.Numerics;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A script with no children has no place: the move, rotate and size tools
/// give it no handles, and a typed position does not reach it.
/// </summary>
public sealed class ScriptPlaceTests
{
    private const float RoundTrip = 1e-3f;

    private static SceneNode AddScript(GizmoHarness harness, Vector3 position, bool selected = true)
    {
        SceneNode node = harness.AddNode(position, "Script");
        node.Script = new Script("Scripts/spin.luau");
        if (selected)
            harness.Scene.Selection.Add(node);

        return node;
    }

    [Theory]
    [InlineData(GizmoMode.Translate)]
    [InlineData(GizmoMode.Rotate)]
    [InlineData(GizmoMode.Scale)]
    public void A_selected_script_with_no_children_gets_no_handles(GizmoMode mode)
    {
        var harness = GizmoHarness.ThreeQuarterView();
        AddScript(harness, Vector3.Zero);
        GizmoTool tool = harness.Use(mode);

        harness.Hover(Vector3.Zero).ShouldBe(GizmoUpdateResult.None);

        tool.IsVisible.ShouldBeFalse();
        tool.PickAt(harness.Frame(harness.WorldToScreen(Vector3.Zero))).ShouldBe(GizmoPick.Miss);
        harness.LiveGeometry().AxisLength.ShouldBe(0f);
    }

    [Fact]
    public void A_press_where_its_arrow_would_be_moves_nothing_and_records_nothing()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode script = AddScript(harness, Vector3.Zero);

        // Where the x arrow of a node at the origin is.
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;
        harness.Grab(Vector3.UnitX * (length * 0.8f)).ShouldBe(GizmoUpdateResult.None);
        harness.DragBy(Vector3.UnitX * 2f);
        harness.Release();

        script.LocalPosition.ShouldBe(Vector3.Zero);
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_script_with_children_is_a_parent_of_things_and_keeps_its_handles()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode script = AddScript(harness, Vector3.Zero);
        script.CreateChild("Lamp");
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * 0.8f)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.DragBy(Vector3.UnitX * 2f);
        harness.Release();

        script.LocalPosition.X.ShouldBe(2f, RoundTrip);
    }

    [Fact]
    public void Selected_beside_a_part_the_handles_are_the_parts_and_the_script_stays_put()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode part = harness.AddSelectedBrushNode(Vector3.Zero);
        SceneNode script = AddScript(harness, new Vector3(40f, 0f, 0f));

        // The pivot is the part's, not half way to a script that stands nowhere.
        harness.Hover(Vector3.Zero);
        harness.Gizmo.Pivot.X.ShouldBe(0f, RoundTrip);

        float length = harness.GeometryAt(Vector3.Zero).AxisLength;
        harness.Grab(Vector3.UnitX * (length * 0.8f)).ShouldBe(GizmoUpdateResult.DragBegan);
        harness.Gizmo.DragTargetCount.ShouldBe(1);
        harness.DragBy(Vector3.UnitX * 2f);
        harness.Release();

        part.LocalPosition.X.ShouldBe(2f, RoundTrip);
        script.LocalPosition.X.ShouldBe(40f);
    }

    [Fact]
    public void A_script_under_a_selected_part_still_rides_along_with_it()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode part = harness.AddSelectedBrushNode(Vector3.Zero);
        SceneNode script = part.CreateChild("Script");
        script.Script = new Script("Scripts/spin.luau");
        float length = harness.GeometryAt(Vector3.Zero).AxisLength;

        harness.Grab(Vector3.UnitX * (length * 0.8f));
        harness.DragBy(Vector3.UnitX * 2f);
        harness.Release();

        script.WorldPosition.X.ShouldBe(2f, RoundTrip);
        script.LocalPosition.ShouldBe(Vector3.Zero);
    }

    [Theory]
    [InlineData(PropertyId.Position)]
    [InlineData(PropertyId.Rotation)]
    [InlineData(PropertyId.Scale)]
    public void A_typed_transform_does_not_reach_a_script_with_no_children(PropertyId id)
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode script = AddScript(harness, Vector3.Zero);
        var edit = new PropertyEdit { Id = id, Vector = new Vector3(3f, 4f, 5f) };

        PropertyEditor.Apply(harness.Undo, [script], edit).ShouldBe(0);

        script.LocalTransform.ShouldBe(Transform.Identity);
        harness.Undo.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void A_position_typed_for_a_part_and_a_script_moves_the_part_alone()
    {
        var harness = GizmoHarness.ThreeQuarterView();
        SceneNode part = harness.AddSelectedBrushNode(Vector3.Zero);
        SceneNode script = AddScript(harness, Vector3.Zero);
        var edit = new PropertyEdit { Id = PropertyId.Position, Vector = new Vector3(3f, 0f, 0f) };

        PropertyEditor.Apply(harness.Undo, [part, script], edit).ShouldBe(1);

        part.LocalPosition.X.ShouldBe(3f);
        script.LocalPosition.ShouldBe(Vector3.Zero);
    }
}
