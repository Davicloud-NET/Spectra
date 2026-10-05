using SpectraEngine.Core.Input;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Who holds the mouse while a level plays: the view takes it, anything that
/// takes the keyboard away frees it, and a click takes it back.
/// </summary>
public sealed class PlayModeCursorTests
{
    [Fact]
    public void Playing_takes_the_cursor()
    {
        var rig = new PlayRig();

        rig.Play();
        rig.Input.ApplyPendingCursorMode();

        rig.Input.IsCursorLocked.ShouldBeTrue();
    }

    [Fact]
    public void A_click_takes_the_cursor_back_after_focus_was_lost_during_play()
    {
        var rig = new PlayRig();
        rig.Play();
        rig.Input.ApplyPendingCursorMode();

        // A console opened, or another window came to the front.
        rig.Input.Submit(InputEvent.FocusLost());
        rig.Input.ApplyPendingCursorMode();
        rig.Frame(ticks: 1);

        rig.Input.IsCursorLocked.ShouldBeFalse();
        rig.Session.IsActive.ShouldBeTrue();

        // Frames go by and nothing takes it back uninvited.
        for (int i = 0; i < 10; i++)
            rig.Frame(ticks: 1);
        rig.Input.RequestedCursorMode.ShouldBe(CursorMode.Normal);

        rig.Input.Submit(InputEvent.PointerDown(PointerButtons.Left));
        rig.Frame(ticks: 1);
        rig.Input.ApplyPendingCursorMode();

        rig.Input.IsCursorLocked.ShouldBeTrue();
    }

    [Fact]
    public void Another_button_does_not_take_the_cursor_back()
    {
        var rig = new PlayRig();
        rig.Play();
        rig.Input.ApplyPendingCursorMode();
        rig.Input.Submit(InputEvent.FocusLost());
        rig.Input.ApplyPendingCursorMode();
        rig.Frame(ticks: 1);

        rig.Input.Submit(InputEvent.PointerDown(PointerButtons.Right));
        rig.Frame(ticks: 1);

        rig.Input.RequestedCursorMode.ShouldBe(CursorMode.Normal);
    }

    [Fact]
    public void A_click_outside_play_mode_does_not_lock_the_cursor()
    {
        var rig = new PlayRig();

        rig.Input.Submit(InputEvent.PointerDown(PointerButtons.Left));
        rig.Frame(ticks: 1);

        rig.Input.RequestedCursorMode.ShouldBe(CursorMode.Normal);

        // Nor after play has ended.
        rig.Play();
        rig.View.Exit();
        rig.Session.Exit();
        rig.Input.Submit(InputEvent.PointerUp(PointerButtons.Left));
        rig.Frame(ticks: 1);
        rig.Input.Submit(InputEvent.PointerDown(PointerButtons.Left));
        rig.Frame(ticks: 1);

        rig.Input.RequestedCursorMode.ShouldBe(CursorMode.Normal);
    }
}
