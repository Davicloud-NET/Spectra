using SpectraEngine.Editing.Hosting;
using SpectraEngine.Editor.Shell;
using System.Collections.Generic;
using System.ComponentModel;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// The drop overlay over the viewport: when it shows, what it says, and that a
/// native session never shows it.
/// </summary>
// Covers the decisions only. How the overlay looks over a render needs a person.
public sealed class ViewportDropOverlayTests
{
    private static ContentDragPayload Model() =>
        new(ContentKind.Model, "Models/crate.obj", "crate.obj");

    private static ContentDragPayload Texture() =>
        new(ContentKind.Texture, "Textures/wall_brick.png", "wall_brick.png");

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

        // Content-relative path, not the bare file name: two folders can hold a crate.obj.
        prompt.Subject.ShouldBe("Models/crate.obj");
        prompt.Reason.ShouldBeEmpty();
    }

    [Fact]
    public void A_texture_is_refused_IN_THE_VIEWPORT_rather_than_with_a_cursor()
    {
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Texture(), hasSession: true, viewportAcceptsDrops: true);

        prompt.IsVisible.ShouldBeTrue();
        prompt.Accepts.ShouldBeFalse();
        prompt.Reason.ShouldContain("wall_brick.png");

        // Empty: the reason already names the file.
        prompt.Subject.ShouldBeEmpty();
    }

    [Fact]
    public void A_native_session_draws_NO_overlay_even_though_the_drop_is_refused()
    {
        // A native child window composites above anything Avalonia draws, so the
        // overlay would never be seen. The refusal goes to the status bar instead.
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Model(), hasSession: true, viewportAcceptsDrops: false);

        prompt.ShouldBe(ViewportDropPrompt.None);

        // The policy still refuses in words for the same inputs.
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
        // A null bound to TextBlock.Text leaves the previous drag's text on screen.
        ViewportDropPrompt none = ViewportDropPrompt.None;

        none.Headline.ShouldBeEmpty();
        none.Subject.ShouldBeEmpty();
        none.Reason.ShouldBeEmpty();
    }

    [Fact]
    public void The_overlay_accepts_exactly_what_the_drop_would_place()
    {
        // Compared against the policy, not a list of kinds, so a new kind cannot split the two.
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
        ViewportDropPrompt prompt =
            ViewportDropPrompt.For(Texture(), hasSession: true, viewportAcceptsDrops: true);

        prompt.Reason.ShouldBe(
            AssetDropPolicy.Refuse(Texture(), hasSession: true, viewportAcceptsDrops: true));
    }

    [Fact]
    public void One_gesture_produces_one_prompt_however_far_the_pointer_travels()
    {
        // DragOver fires per pointer move; value equality is the change guard.
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
/// The material arm of the drop overlay: what a drop would paint, and the
/// modifier hint shown during the drag.
/// </summary>
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

        // Both arms are amber, so the icon has to tell them apart.
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

        face.ShouldBe(again);
        face.ShouldNotBe(block);
    }

    [Fact]
    public void A_native_session_draws_nothing_whatever_the_scope_is()
    {
        ViewportDropPrompt.For(Material(), true, viewportAcceptsDrops: false, MaterialDropScope.Brush)
            .ShouldBe(ViewportDropPrompt.None);
    }
}
