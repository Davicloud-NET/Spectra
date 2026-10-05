using System.Numerics;
using SpectraEngine.Core.Input;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Where the first-person view puts the camera when the character is moved
/// across the map: a teleport or a respawn.
/// </summary>
public sealed class FirstPersonViewTests
{
    private static readonly Vector3 FarAway = new(20f, 0.05f, 10f);

    [Fact]
    public void A_teleport_on_a_frame_with_no_tick_moves_the_view_there_at_once()
    {
        var rig = new PlayRig();
        rig.Play();

        rig.Character.Teleport(FarAway);
        rig.Frame(ticks: 0, alpha: 0.5f);

        // Half way between the last two ticks is still the destination.
        rig.Character.State.Position.ShouldBe(FarAway);
        rig.Scene.Camera.Position.ShouldBe(rig.Eye);
    }

    [Fact]
    public void A_teleport_followed_by_ticks_is_not_blended_either()
    {
        var rig = new PlayRig();
        rig.Play();
        var inTheAir = new Vector3(20f, 1f, 10f);

        // Long enough that the fall moves the feet on every tick.
        rig.Character.Teleport(inTheAir);
        rig.Frame(ticks: 6, alpha: 0.5f);

        rig.Character.State.Position.Y.ShouldBeLessThan(inTheAir.Y);
        Vector3.Distance(rig.Character.State.Position, inTheAir).ShouldBeLessThan(0.1f);
        rig.Scene.Camera.Position.ShouldBe(rig.Eye);
    }

    [Fact]
    public void A_teleport_takes_the_yaw_it_asks_for()
    {
        var rig = new PlayRig();
        rig.Play();

        rig.Character.Teleport(FarAway, yaw: 1.25f);
        rig.Frame(ticks: 1);

        rig.Scene.Camera.Yaw.ShouldBe(1.25f);

        // The next command walks the way the view now faces.
        rig.Frame(ticks: 1);
        rig.View.Command.Yaw.ShouldBe(1.25f);
    }

    [Fact]
    public void A_teleport_with_no_yaw_keeps_the_view_facing_the_way_it_was()
    {
        var rig = new PlayRig();
        rig.Play();
        float turned = Turn(rig);

        rig.Character.Teleport(FarAway);
        rig.Frame(ticks: 1);

        rig.Scene.Camera.Yaw.ShouldBe(turned);
    }

    [Fact]
    public void A_respawn_puts_the_view_at_the_spawn_facing_the_spawn_yaw()
    {
        var rig = new PlayRig();
        rig.Play();

        // After Play: starting a level sets the spawn from the level.
        rig.View.SpawnYaw = 0.5f;
        Turn(rig);

        // Past the plate's edge at x = 32: nothing to land on.
        rig.Character.Teleport(new Vector3(40f, 1f, 0f));
        for (int i = 0; i < 600 && rig.View.Respawns == 0; i++)
            rig.Frame(ticks: 1, alpha: 0.5f);

        rig.View.Respawns.ShouldBe(1);
        rig.Character.State.Position.ShouldBe(rig.Manager.PlayerSpawn);
        rig.Scene.Camera.Position.ShouldBe(rig.Eye);
        rig.Scene.Camera.Yaw.ShouldBe(0.5f);
    }

    [Fact]
    public void A_teleport_however_short_is_told_once_to_whoever_follows_the_camera()
    {
        var rig = new PlayRig();
        rig.Play();
        rig.View.TryTakeViewJump().ShouldBeFalse();

        rig.Character.Teleport(rig.Character.State.Position + new Vector3(1f, 0f, 0f));
        rig.Frame(ticks: 1);

        rig.View.TryTakeViewJump().ShouldBeTrue();
        rig.View.TryTakeViewJump().ShouldBeFalse();
    }

    [Fact]
    public void A_respawn_is_told_too()
    {
        var rig = new PlayRig();
        rig.Play();

        rig.Character.Teleport(new Vector3(40f, 1f, 0f));
        rig.Frame(ticks: 1);
        rig.View.TryTakeViewJump().ShouldBeTrue();

        for (int i = 0; i < 600 && rig.View.Respawns == 0; i++)
            rig.Frame(ticks: 1, alpha: 0.5f);

        rig.View.Respawns.ShouldBe(1);
        rig.View.TryTakeViewJump().ShouldBeTrue();
    }

    [Fact]
    public void Walking_and_falling_tell_of_no_jump()
    {
        var rig = new PlayRig();
        rig.Play();

        rig.Input.Submit(InputEvent.KeyDown(InputKey.W));
        for (int i = 0; i < 30; i++)
            rig.Frame(ticks: 1, alpha: 0.5f);

        rig.Character.State.Position.ShouldNotBe(rig.Manager.PlayerSpawn);
        rig.View.TryTakeViewJump().ShouldBeFalse();
    }

    [Fact]
    public void A_jump_nobody_took_does_not_carry_into_the_next_run()
    {
        var rig = new PlayRig();
        rig.Play();
        rig.Character.Teleport(FarAway);
        rig.Frame(ticks: 1);

        rig.View.Exit();
        rig.Session.Exit();
        rig.Play();

        rig.View.TryTakeViewJump().ShouldBeFalse();
    }

    // Looks to the side with the mouse. Returns the yaw the view ends at.
    private static float Turn(PlayRig rig)
    {
        // The view only looks once its cursor lock has landed.
        rig.Input.ApplyPendingCursorMode();
        rig.Input.Submit(InputEvent.PointerDelta(new Vector2(300f, 0f)));
        rig.Frame(ticks: 1);

        float yaw = rig.Scene.Camera.Yaw;
        yaw.ShouldNotBe(rig.View.SpawnYaw);
        return yaw;
    }
}
