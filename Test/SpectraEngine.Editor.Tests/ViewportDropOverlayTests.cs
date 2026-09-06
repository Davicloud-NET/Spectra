using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.ComponentModel;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The first thing this shell draws over the render: when it appears, what it
/// says, and which session it refuses to appear in at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>A drag cannot be driven headlessly and every decision inside one can.</b>
/// The gesture is a pointer, a compositor and an OLE loop; what is capable of
/// being WRONG is the visibility rule, the agreement between the overlay's
/// verdict and the drop's, and the guard that keeps a per-pointer-move event
/// from re-evaluating five bindings. All three are functions of their inputs
/// and all three are here, because the alternative is reasoning that nothing
/// ever checks.
/// </para>
/// <para>
/// <b>What is NOT provable here, stated so nobody reads a green run as more
/// than it is:</b> that the overlay is legible over a lit render, that the chip
/// sits where it was meant to, and that the frame is visible at all. Those need
/// a person with a mouse.
/// </para>
/// </remarks>
public sealed class ViewportDropOverlayTests
{
    private static ContentDragPayload Model() =>
        new(ContentKind.Model, "Models/crate.obj", "crate.obj");

    private static ContentDragPayload Texture() =>
        new(ContentKind.Texture, "Textures/wall_brick.png", "wall_brick.png");

    // --- When it is drawn ----------------------------------------------------

    [Fact]
    public void No_drag_over_the_viewport_draws_nothing()
    {
        ViewportDropPrompt.For(null, hasSession: true, viewportAcceptsDrops: true)
            .IsVisible.ShouldBeFalse();
    }

    [Fact]
    public void A_model_over_a_composited_viewport_says_what_would_land()
    {
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Model(), hasSession: true, viewportAcceptsDrops: true);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeTrue();

        // The CONTENT-RELATIVE path, which is the identity every other layer
        // keys on. Showing a bare file name would make two crates in two folders
        // read as one thing at the moment somebody is deciding whether to let
        // go.
        prompt.Subject.ShouldBe("Models/crate.obj");
        prompt.Reason.ShouldBeEmpty();
    }

    [Fact]
    public void A_texture_is_refused_IN_THE_VIEWPORT_rather_than_with_a_cursor()
    {
        // The half of the H11 gesture that was missing. A refusal cursor says
        // the shell did not understand the drag, when in fact it understood it
        // perfectly and has something to say about it.
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Texture(), hasSession: true, viewportAcceptsDrops: true);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeFalse();
        prompt.Reason.ShouldContain("wall_brick.png");

        // Empty, because the reason already names the file: two mentions of one
        // thing in one chip read as two different things.
        prompt.Subject.ShouldBeEmpty();
    }

    [Fact]
    public void A_native_session_draws_NO_overlay_even_though_the_drop_is_refused()
    {
        // THE AIRSPACE RULE, AS DATA. A native child is a window the OS
        // composites above everything Avalonia draws into the main window, so
        // the identical markup over one is painted and never seen - and an
        // overlay nobody can see is worse than none, because the code then
        // claims to have reported something. The refusal still reaches the user
        // through the status bar and the output log, which is what H11 built.
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Model(), hasSession: true, viewportAcceptsDrops: false);

        prompt.ShouldBe(ViewportDropPrompt.None);

        // Same inputs, and the policy DOES have something to say. The two
        // answers are deliberately different, and this pins that they are.
        AssetDropPolicy.Refuse(Model(), hasSession: true, viewportAcceptsDrops: false)
            .ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void With_no_session_there_is_no_viewport_to_draw_over()
    {
        ViewportDropPrompt.For(Model(), hasSession: false, viewportAcceptsDrops: true)
            .ShouldBe(ViewportDropPrompt.None);
    }

    [Fact]
    public void Nothing_the_overlay_binds_to_is_ever_null()
    {
        // These bind straight to TextBlock.Text, where a null is a binding that
        // silently leaves the previous value on screen - so an overlay coming
        // back for a second drag would show the first drag's file.
        ViewportDropPrompt none = ViewportDropPrompt.None;

        none.Headline.ShouldBeEmpty();
        none.Subject.ShouldBeEmpty();
        none.Reason.ShouldBeEmpty();
    }

    // --- Agreement with the drop it describes --------------------------------

    [Fact]
    public void The_overlay_accepts_exactly_what_the_drop_would_place()
    {
        // The one thing this overlay must never do is promise a placement the
        // drop then refuses, because the moment that is discovered is the moment
        // somebody let go of the mouse. Stated as an equality against the policy
        // rather than as a list of kinds, so a kind added to AssetDropPolicy
        // cannot make the two disagree.
        foreach (ContentKind kind in new[]
        {
            ContentKind.Model, ContentKind.Texture, ContentKind.Material,
            ContentKind.Shader, ContentKind.Other,
        })
        {
            var payload = new ContentDragPayload(kind, $"Assets/thing.{kind}", $"thing.{kind}");

            ViewportDropPrompt prompt =
                ViewportDropPrompt.For(payload, hasSession: true, viewportAcceptsDrops: true);

            prompt.IsVisible.ShouldBeTrue();
            prompt.Accepts.ShouldBe(AssetDropPolicy.CanPlace(kind));
        }
    }

    [Fact]
    public void A_refusal_is_the_policys_own_sentence_and_not_a_second_one()
    {
        // Verbatim, never paraphrased. Two wordings for one refusal is two
        // things to keep in step, and the one on screen would be the one nobody
        // updated.
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Texture(), hasSession: true, viewportAcceptsDrops: true);

        prompt.Reason.ShouldBe(
            AssetDropPolicy.Refuse(Texture(), hasSession: true, viewportAcceptsDrops: true));
    }

    // --- The per-pointer-move guard ------------------------------------------

    [Fact]
    public void One_gesture_produces_one_prompt_however_far_the_pointer_travels()
    {
        // DragOver fires per pointer move and carries the payload the gesture
        // started with, so a drag across a 1280x720 pane asks this question
        // several hundred times with the same answer. Equality is what makes
        // that free.
        ViewportDropPrompt first =
            ViewportDropPrompt.For(Model(), hasSession: true, viewportAcceptsDrops: true);
        ViewportDropPrompt second =
            ViewportDropPrompt.For(Model(), hasSession: true, viewportAcceptsDrops: true);

        first.ShouldBe(second);
    }

    [Fact]
    public void An_unchanged_prompt_notifies_nothing()
    {
        var shell = new ShellModel();
        var raised = new List<string?>();

        shell.DropPrompt = ViewportDropPrompt.For(
            Model(), hasSession: true, viewportAcceptsDrops: true);

        ((INotifyPropertyChanged)shell).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // Two hundred pointer moves' worth of the same answer.
        for (int i = 0; i < 200; i++)
        {
            shell.DropPrompt = ViewportDropPrompt.For(
                Model(), hasSession: true, viewportAcceptsDrops: true);
        }

        raised.ShouldBeEmpty();
    }

    [Fact]
    public void A_changed_prompt_republishes_every_half_of_itself()
    {
        // The four bindable properties are views of one value, so a change to
        // the value has to raise all of them: a headline that moved while the
        // reason under it did not is a chip describing two different drags.
        var shell = new ShellModel();
        var raised = new List<string?>();
        ((INotifyPropertyChanged)shell).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        shell.DropPrompt = ViewportDropPrompt.For(
            Model(), hasSession: true, viewportAcceptsDrops: true);

        raised.ShouldContain(nameof(ShellModel.DropVisible));
        raised.ShouldContain(nameof(ShellModel.DropAccepts));
        raised.ShouldContain(nameof(ShellModel.DropHeadline));
        raised.ShouldContain(nameof(ShellModel.DropSubject));
        raised.ShouldContain(nameof(ShellModel.DropReason));

        shell.DropVisible.ShouldBeTrue();
        shell.DropAccepts.ShouldBeTrue();
        shell.DropSubject.ShouldBe("Models/crate.obj");
    }

    [Fact]
    public void Crossing_from_a_model_to_a_texture_swaps_the_arm()
    {
        // A drag never changes payload mid-gesture, but two gestures in a row
        // do, and the prompt is state rather than history: the second one must
        // not inherit the first one's arm.
        var shell = new ShellModel
        {
            DropPrompt = ViewportDropPrompt.For(
                Model(), hasSession: true, viewportAcceptsDrops: true),
        };

        shell.DropPrompt = ViewportDropPrompt.For(
            Texture(), hasSession: true, viewportAcceptsDrops: true);

        shell.DropAccepts.ShouldBeFalse();
        shell.DropSubject.ShouldBeEmpty();
        shell.DropReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void The_overlay_goes_away_when_the_drag_does()
    {
        // The failure this pins is a frame and a label left painted over the
        // picture with no gesture behind them, which is a viewport that looks
        // broken and has no verb anywhere to clear it.
        var shell = new ShellModel
        {
            DropPrompt = ViewportDropPrompt.For(
                Model(), hasSession: true, viewportAcceptsDrops: true),
        };

        shell.DropVisible.ShouldBeTrue();

        shell.DropPrompt = ViewportDropPrompt.For(
            null, hasSession: true, viewportAcceptsDrops: true);

        shell.DropVisible.ShouldBeFalse();
        shell.DropHeadline.ShouldBeEmpty();
        shell.DropSubject.ShouldBeEmpty();
        shell.DropReason.ShouldBeEmpty();
    }
}

/// <summary>
/// The material arm of the same overlay: what it says a drop would paint, and
/// which key changes that.
/// </summary>
/// <remarks>
/// <b>The modifier is advertised WHILE the drag is in flight, because there is
/// nowhere else to advertise it.</b> A drag has no menu beside it and no
/// shortcut printed anywhere, so the only moment "hold Ctrl for the whole block"
/// can be read is while somebody is holding the mouse down over the face it
/// describes; a modifier whose effect only shows after the drop is a modifier
/// nobody uses twice.
/// </remarks>
public sealed class MaterialDropPromptTests
{
    private static ContentDragPayload Material() =>
        new(ContentKind.Material, "Materials/wall_brick.spectramat", "wall_brick.spectramat");

    [Fact]
    public void A_material_over_the_viewport_says_it_would_paint_this_face()
    {
        ViewportDropPrompt prompt = ViewportDropPrompt.For(
            Material(), hasSession: true, viewportAcceptsDrops: true, MaterialDropScope.Face);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeTrue();
        prompt.Headline.ShouldBe("Drop to paint");
        prompt.Subject.ShouldBe("Materials/wall_brick.spectramat");
        prompt.Hint.ShouldContain("this face");
        prompt.Hint.ShouldContain("Ctrl");
    }

    [Fact]
    public void Holding_the_modifier_says_the_whole_block_and_how_to_go_back()
    {
        ViewportDropPrompt prompt = ViewportDropPrompt.For(
            Material(), hasSession: true, viewportAcceptsDrops: true, MaterialDropScope.Brush);

        prompt.Hint.ShouldContain("the whole block");

        // Both directions, because a person holding Ctrl needs to know how to
        // stop as much as one not holding it needs to know how to start.
        prompt.Hint.ShouldContain("release");
    }

    [Fact]
    public void A_model_still_says_place_and_carries_no_hint()
    {
        var model = new ContentDragPayload(ContentKind.Model, "Models/crate.obj", "crate.obj");

        ViewportDropPrompt prompt = ViewportDropPrompt.For(
            model, hasSession: true, viewportAcceptsDrops: true);

        prompt.Headline.ShouldBe("Drop to place");
        prompt.Hint.ShouldBe("");
        prompt.IconKey.ShouldBe("IconMesh");
    }

    [Fact]
    public void The_two_arms_wear_different_glyphs()
    {
        ViewportDropPrompt painting = ViewportDropPrompt.For(
            Material(), hasSession: true, viewportAcceptsDrops: true);

        var texture = new ContentDragPayload(ContentKind.Texture, "Textures/brick.png", "brick.png");
        ViewportDropPrompt refusing = ViewportDropPrompt.For(
            texture, hasSession: true, viewportAcceptsDrops: true);

        // The arms share a hue by design (amber means state or warning here, and
        // the accent means selection and nothing else), so the icon is half of
        // how they are told apart.
        painting.IconKey.ShouldBe(ViewportDropPrompt.MaterialIcon);
        refusing.IconKey.ShouldBe(ViewportDropPrompt.RefusingIcon);
        refusing.Accepts.ShouldBeFalse();
        refusing.Reason.ShouldContain("material");
    }

    [Fact]
    public void A_scope_change_produces_a_different_value_and_nothing_else_does()
    {
        ViewportDropPrompt face = ViewportDropPrompt.For(
            Material(), true, true, MaterialDropScope.Face);
        ViewportDropPrompt again = ViewportDropPrompt.For(
            Material(), true, true, MaterialDropScope.Face);
        ViewportDropPrompt block = ViewportDropPrompt.For(
            Material(), true, true, MaterialDropScope.Brush);

        // Record equality is the notification guard: DragOver fires at pointer
        // rate and every one of those carries the same answer, so a crossing
        // must raise nothing until the modifier moves.
        face.ShouldBe(again);
        face.ShouldNotBe(block);
    }

    [Fact]
    public void A_native_session_draws_nothing_whatever_the_scope_is()
    {
        // The airspace rule as data: over a native child every pixel of this
        // would be painted and composited away, and an overlay nobody can see is
        // worse than none because the code claims to have reported something.
        ViewportDropPrompt.For(Material(), true, viewportAcceptsDrops: false, MaterialDropScope.Brush)
            .ShouldBe(ViewportDropPrompt.None);
    }
}
