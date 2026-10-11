using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Viewport;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The cross that marks a hovered or selected light or point entity is on its
/// node. An icon that was moved gets one too.
/// </summary>
public sealed class IconPileFocusTests
{
    [Fact]
    public void The_icon_under_the_cursor_is_marked_on_the_icon_as_well_as_on_its_node()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        SceneIcons icons = rig.Place();
        var outline = new SelectionOutline { Icons = icons };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: lift[3]);

        // A cross is three lines. One on the node, one on the icon at the end of the row.
        CrossCenters(output, SelectionOutline.HoverColor).ShouldBe([Vector3.Zero, icons.Markers[3].At]);
    }

    [Fact]
    public void A_selected_icon_of_a_pile_is_marked_on_the_icon_as_well_as_on_its_node()
    {
        var rig = new IconPileRig();
        SceneNode[] lift = rig.AddLift(Vector3.Zero);
        rig.Scene.Selection.Add(lift[1]);
        SceneIcons icons = rig.Place();
        var outline = new SelectionOutline { Icons = icons };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: null);

        CrossCenters(output, SelectionOutline.SelectedColor).ShouldBe([Vector3.Zero, icons.Markers[1].At]);
    }

    [Fact]
    public void An_icon_that_was_not_moved_is_marked_on_its_node_alone()
    {
        var rig = new IconPileRig();
        SceneNode sound = rig.AddEntity(IconPileRig.Sound, new Vector3(1f, 2f, 0f));
        var outline = new SelectionOutline { Icons = rig.Place() };
        var output = new DebugDraw();

        outline.Draw(output, rig.Scene, rig.Camera, rig.ViewportSize, hovered: sound);

        CrossCenters(output, SelectionOutline.HoverColor).ShouldBe([new Vector3(1f, 2f, 0f)]);
    }

    // The middle of each cross drawn in this colour, three lines to a cross.
    private static List<Vector3> CrossCenters(DebugDraw output, Vector3 color)
    {
        List<Vector3> vertices = DebugLines.Colored(output, color);
        (vertices.Count % 6).ShouldBe(0);

        var centers = new List<Vector3>();
        for (int i = 0; i < vertices.Count; i += 6)
        {
            Vector3 center = (vertices[i] + vertices[i + 1]) * 0.5f;
            Vector3.Distance(center, (vertices[i + 2] + vertices[i + 3]) * 0.5f).ShouldBeLessThan(1e-5f);
            Vector3.Distance(center, (vertices[i + 4] + vertices[i + 5]) * 0.5f).ShouldBeLessThan(1e-5f);
            centers.Add(center);
        }

        return centers;
    }
}
