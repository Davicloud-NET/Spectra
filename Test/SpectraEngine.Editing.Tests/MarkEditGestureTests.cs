using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Editing.Commands;
using SpectraEngine.Editing.Hosting;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// A tag or an attribute edited while a property gesture is open, as a drag
/// across a number or the colour picker opens one. An attribute's colour is
/// picked inside such a gesture. A tag has none of its own.
/// </summary>
public sealed class MarkEditGestureTests
{
    [Fact]
    public void A_tag_edit_stays_out_of_a_gesture_that_is_open()
    {
        var rig = new EntityEditRig();
        SceneNode brick = rig.AddPart("Brick");
        rig.Scene.Selection.SetRange([brick]);

        rig.Host.BeginPropertyGesture("Position").ShouldBeTrue();
        rig.Host.ApplyTag(new TagEdit("Lava", IsOn: true)).ShouldBe(0);
        brick.Tags.Count.ShouldBe(0);

        // Had it joined, a cancelled drag would take the tag with it.
        rig.Host.EndPropertyGesture(commit: false);
        rig.Host.ApplyTag(new TagEdit("Lava", IsOn: true)).ShouldBe(1);
        brick.Tags.ShouldBe(["Lava"]);
    }

    [Fact]
    public void An_attributes_colour_joins_the_gesture_and_a_cancel_puts_it_back()
    {
        var rig = new EntityEditRig();
        SceneNode brick = rig.AddPart("Brick");
        brick.Attributes = AttributeSet.Empty.With("Glow", MemberValue.Of(new Color3(1f, 1f, 1f)));
        AttributeSet built = brick.Attributes;
        rig.Scene.Selection.SetRange([brick]);

        rig.Host.BeginPropertyGesture("Color").ShouldBeTrue();
        rig.Host.ApplyAttribute(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(1f, 0f, 0f)))).ShouldBe(1);
        rig.Host.ApplyAttribute(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(0f, 0f, 1f)))).ShouldBe(1);
        rig.Host.EndPropertyGesture(commit: false);

        brick.Attributes.ShouldBe(built);
    }

    [Fact]
    public void An_attributes_colour_dragged_in_a_gesture_is_one_step_back()
    {
        var rig = new EntityEditRig();
        SceneNode brick = rig.AddPart("Brick");
        brick.Attributes = AttributeSet.Empty.With("Glow", MemberValue.Of(new Color3(1f, 1f, 1f)));
        AttributeSet built = brick.Attributes;
        rig.Scene.Selection.SetRange([brick]);

        rig.Host.BeginPropertyGesture("Color").ShouldBeTrue();
        rig.Host.ApplyAttribute(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(1f, 0f, 0f))));
        rig.Host.ApplyAttribute(AttributeEdit.Set("Glow", MemberValue.Of(new Color3(0f, 0f, 1f))));
        rig.Host.EndPropertyGesture(commit: true);
        rig.Host.Apply(EditorHostCommand.Undo);

        brick.Attributes.ShouldBe(built);
    }
}
