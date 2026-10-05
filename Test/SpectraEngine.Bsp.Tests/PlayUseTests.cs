using System.Numerics;
using SpectraEngine.Core.Input;
using SpectraEngine.Core.Physics.Character;
using SpectraEngine.Core.Play;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The use button: what a press reaches, how far, and that one press is one
/// use however the frames and ticks fall.
/// </summary>
public sealed class PlayUseTests
{
    // The eye is at x = 0, about 1.63 up, looking down +x.
    private const float EyeLevel = 1.6f;

    private static readonly Vector3 PanelHalf = new(0.25f, 0.5f, 0.5f);

    private static readonly CharacterCommand Idle = default;

    private static readonly CharacterCommand UseAhead = new() { Buttons = CharacterButtons.Use };

    [Fact]
    public void A_use_reaches_the_entity_in_front_of_the_eye_on_the_next_tick()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Tick(in UseAhead);
        rig.Log.ShouldBeEmpty();

        rig.Tick(in Idle);

        // No activator and no caller: the player is not an entity.
        rig.Log.ShouldBe(["button:Use::-:-"]);
    }

    [Fact]
    public void A_held_button_uses_once()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Tick(in UseAhead, ticks: 30);

        rig.Log.Count.ShouldBe(1);
    }

    [Fact]
    public void A_button_let_go_and_pressed_again_uses_again()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Tick(in UseAhead, ticks: 5);
        rig.Tick(in Idle);
        rig.Tick(in UseAhead, ticks: 5);

        rig.Log.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(1.95f, true)]
    [InlineData(2.05f, false)]
    [InlineData(2.5f, false)]
    public void A_use_reaches_two_units_and_no_further(float distance, bool reached)
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: distance);
        rig.Play();

        Use(rig, in UseAhead);

        rig.Log.Count.ShouldBe(reached ? 1 : 0);
    }

    [Fact]
    public void A_use_passes_through_a_trigger_volume_to_the_button_behind_it()
    {
        var rig = new PlayRig();
        SceneNode volume = Panel(rig, "volume", nearFace: 0.6f);
        volume.CanQuery = false;
        volume.CanCollide = false;
        volume.IsRendered = false;
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        Use(rig, in UseAhead);

        rig.Log.ShouldBe(["button:Use::-:-"]);
    }

    [Fact]
    public void A_use_stops_at_the_first_thing_a_query_sees()
    {
        var rig = new PlayRig();
        Panel(rig, "crate", nearFace: 0.6f);
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        Use(rig, in UseAhead);

        rig.Log.ShouldBe(["crate:Use::-:-"]);
    }

    [Fact]
    public void A_use_on_a_child_brush_reaches_the_entity_on_the_group_above_it()
    {
        var rig = new PlayRig();
        SceneNode lift = EntityRuntime.Place(rig.Scene.Root, "lift", "recorder");
        SceneNode cage = lift.CreateChild("cage");
        rig.Part("panel", new Vector3(1.5f, EyeLevel, 0f), PanelHalf, cage);
        rig.Play();

        Use(rig, in UseAhead);

        rig.Log.ShouldBe(["lift:Use::-:-"]);
    }

    [Fact]
    public void A_use_on_a_part_no_entity_owns_reaches_nothing()
    {
        var rig = new PlayRig();
        rig.Part("crate", new Vector3(1.5f, EyeLevel, 0f), PanelHalf);
        EntityRuntime.Place(rig.Scene.Root, "bystander", "recorder");
        rig.Play();

        Use(rig, in UseAhead);

        rig.Log.ShouldBeEmpty();
        rig.Entities.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_use_is_aimed_by_the_commands_yaw_and_not_by_the_camera()
    {
        var rig = new PlayRig();
        Panel(rig, "ahead", nearFace: 1.25f);
        rig.Recorder("behind", new Vector3(-1.5f, EyeLevel, 0f), PanelHalf);
        rig.Play();

        // The view still looks down +x.
        rig.Scene.Camera.Yaw.ShouldBe(0f);
        var turned = new CharacterCommand { Buttons = CharacterButtons.Use, Yaw = MathF.PI };

        Use(rig, in turned);

        rig.Log.ShouldBe(["behind:Use::-:-"]);
    }

    [Fact]
    public void A_use_is_aimed_by_the_commands_pitch()
    {
        var rig = new PlayRig();
        rig.Recorder("hatch", new Vector3(0f, 2.75f, 0f), new Vector3(0.5f, 0.25f, 0.5f));
        rig.Play();

        Use(rig, in UseAhead);
        rig.Log.ShouldBeEmpty();

        var lookingUp = new CharacterCommand { Buttons = CharacterButtons.Use, Pitch = 1.5f };
        Use(rig, in lookingUp);

        rig.Log.ShouldBe(["hatch:Use::-:-"]);
    }

    [Fact]
    public void A_use_at_the_static_world_reaches_nothing()
    {
        var rig = new PlayRig();
        EntityRuntime.Place(rig.Scene.Root, "bystander", "recorder");
        rig.Play();
        var lookingDown = new CharacterCommand { Buttons = CharacterButtons.Use, Pitch = -1.5f };

        // The floor is in reach of the eye, so the ray does hit it.
        var down = new Ray3(rig.Eye, -Vector3.UnitY);
        rig.Scene.RaycastGameplay(in down, out GameplayRayHit floor, PlaySession.UseReach).ShouldBeTrue();
        floor.StaticWorld.ShouldBeTrue();

        Use(rig, in lookingDown);

        rig.Log.ShouldBeEmpty();
        rig.Entities.PendingEventCount.ShouldBe(0);
    }

    [Fact]
    public void A_use_with_no_entities_running_does_nothing()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        // A map loaded during play stops the entities and not the character.
        rig.Manager.OnSceneReplaced();
        Use(rig, in UseAhead);

        rig.Session.IsActive.ShouldBeTrue();
        rig.Log.ShouldBeEmpty();
    }

    [Fact]
    public void A_held_key_uses_once()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Input.Submit(InputEvent.KeyDown(InputKey.E));
        for (int i = 0; i < 30; i++)
            rig.Frame(ticks: 1);

        rig.Log.Count.ShouldBe(1);
    }

    [Fact]
    public void A_press_uses_once_on_a_frame_that_runs_several_ticks()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Input.Submit(InputEvent.KeyDown(InputKey.E));
        rig.Frame(ticks: 5);
        rig.Frame(ticks: 5);

        rig.Log.Count.ShouldBe(1);
    }

    [Fact]
    public void A_press_on_a_frame_with_no_tick_is_used_by_the_next_tick()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        // A tap: down and up again before the frame samples the keyboard.
        rig.Input.Submit(InputEvent.KeyDown(InputKey.E));
        rig.Input.Submit(InputEvent.KeyUp(InputKey.E));
        rig.Frame(ticks: 0);
        rig.Frame(ticks: 0);
        rig.Log.ShouldBeEmpty();

        rig.Frame(ticks: 1);
        rig.Frame(ticks: 1);
        rig.Log.Count.ShouldBe(1);

        for (int i = 0; i < 10; i++)
            rig.Frame(ticks: 1);
        rig.Log.Count.ShouldBe(1);
    }

    [Fact]
    public void A_key_let_go_and_pressed_again_uses_again()
    {
        var rig = new PlayRig();
        Panel(rig, "button", nearFace: 1.25f);
        rig.Play();

        rig.Input.Submit(InputEvent.KeyDown(InputKey.E));
        rig.Frame(ticks: 1);
        rig.Input.Submit(InputEvent.KeyUp(InputKey.E));
        rig.Frame(ticks: 1);
        rig.Input.Submit(InputEvent.KeyDown(InputKey.E));
        rig.Frame(ticks: 1);
        rig.Frame(ticks: 1);

        rig.Log.Count.ShouldBe(2);
    }

    // A recorder panel across the view, its near face this far down +x.
    private static SceneNode Panel(PlayRig rig, string name, float nearFace) =>
        rig.Recorder(name, new Vector3(nearFace + PanelHalf.X, EyeLevel, 0f), PanelHalf);

    // The press, then the tick that delivers it.
    private static void Use(PlayRig rig, in CharacterCommand command)
    {
        rig.Tick(in command);
        rig.Tick(in Idle);
    }
}
