using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Linq;

namespace SpectraEngine.Entities.Tests;

/// <summary>
/// The lift's spoken line in the demo's start room: the button says it, its
/// markers fire on the ticks they stand on, and the lift goes up on its
/// second word.
/// </summary>
// At 48 kHz and 60 ticks a second a tick is 800 frames.
public sealed class DemoStartRoomLiftVoiceTests : IClassFixture<CookedDemoSounds>
{
    private const string LiftVoice = "Sounds/lift_voice.wav";

    private const int FramesATick = 800;

    // The second word starts 0.6 seconds into the line.
    private const int TicksToUp = 36;

    // The line is 1.2 seconds long.
    private const int LineTicks = 72;

    // From the tick the use key goes down to the first tick the lift moves
    // on: the button takes the press and starts the line, the line reaches
    // its second word, the case hears the marker and sends the lift up.
    private const int TicksFromUseToLift = 1 + TicksToUp + 1;

    private readonly CookedDemoSounds _cooked;

    public DemoStartRoomLiftVoiceTests(CookedDemoSounds cooked) => _cooked = cooked;

    [Fact]
    public void The_line_is_cooked_with_a_marker_where_each_word_starts()
    {
        using var rig = new StartRoomSoundRig(_cooked);

        new AssetSoundCatalog(rig.Assets)
            .TryDescribe(LiftVoice, out SoundDescription line, out string reason)
            .ShouldBeTrue(reason);

        line.FrameCount.ShouldBe(LineTicks * FramesATick);
        line.SampleRate.ShouldBe(60 * FramesATick);
        line.Markers.ShouldBe([new AudioMarker(0, "going"), new AudioMarker(TicksToUp * FramesATick, "up")]);
    }

    [Fact]
    public void The_button_starts_the_line_and_it_is_said_from_the_lift()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();

        long start = StartTheLine(rig);

        rig.Outputs.Fired.ShouldContain($"{start}:{DemoPlayArea.LiftButtonName}.{FuncButton.OnPressed}");
        rig.TryGetEmitter(DemoPlayArea.LiftVoiceName, out SoundEmitter line).ShouldBeTrue();
        line.Path.ShouldBe(LiftVoice);
        line.Node.Parent.ShouldBeSameAs(rig.Node(DemoPlayArea.LiftName));
    }

    [Fact]
    public void Each_marker_fires_with_its_name_on_the_tick_it_stands_on()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();

        long start = StartTheLine(rig);
        rig.Idle(LineTicks);

        string marker = $"{DemoPlayArea.LiftVoiceName}.{PointSound.OnMarker}";
        rig.Outputs.Carried.ShouldBe([$"{start}:{marker}:going", $"{start + TicksToUp}:{marker}:up"]);
        rig.Outputs.Fired.ShouldContain($"{start + LineTicks}:{DemoPlayArea.LiftVoiceName}.{PointSound.OnEnded}");
    }

    [Fact]
    public void The_case_hears_a_marker_on_the_tick_after_it_and_fires_for_the_second_word_only()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();

        long start = StartTheLine(rig);
        rig.Idle(LineTicks);

        string[] fired = [.. rig.Outputs.Fired.Where(entry => entry.Contains($":{DemoPlayArea.LiftVoiceMarkersName}."))];
        fired.ShouldBe(
        [
            $"{start + 1}:{DemoPlayArea.LiftVoiceMarkersName}.{LogicCase.OnDefault}",
            $"{start + TicksToUp + 1}:{DemoPlayArea.LiftVoiceMarkersName}.{LogicCase.OnCase01}",
        ]);
    }

    [Fact]
    public void The_lift_stays_down_through_the_first_word_and_starts_on_the_tick_after_the_second_ones_marker()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        FuncMoveLinear lift = rig.Live<FuncMoveLinear>(DemoPlayArea.LiftName);

        long start = StartTheLine(rig);
        while (rig.World.TickNumber < start + TicksToUp)
        {
            rig.Tick(default);
            lift.TicksTravelled.ShouldBe(0, $"{rig.World.TickNumber - start} ticks into the line");
        }

        rig.Tick(default);

        lift.TicksTravelled.ShouldBe(1);
        rig.TryGetEmitter(DemoPlayArea.LiftMoveSoundName, out SoundEmitter hum).ShouldBeTrue();
        hum.StartTick.ShouldBe(start + TicksToUp + 1);
    }

    [Fact]
    public void The_button_takes_no_second_press_until_the_line_has_been_said()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        FuncButton button = rig.Live<FuncButton>(DemoPlayArea.LiftButtonName);
        string pressed = $":{DemoPlayArea.LiftButtonName}.{FuncButton.OnPressed}";

        long start = StartTheLine(rig);
        rig.TryGetEmitter(DemoPlayArea.LiftVoiceName, out SoundEmitter first).ShouldBeTrue();

        // A press on every tick, the way a wire would send one.
        for (int i = 0; i < 300 && rig.Outputs.Fired.Count(fired => fired.EndsWith(pressed)) < 2; i++)
        {
            rig.World.QueueInput(DemoPlayArea.LiftButtonName, "Press");
            rig.Tick(default);

            if (rig.World.TickNumber < start + LineTicks)
            {
                button.IsPressed.ShouldBeTrue();
                rig.TryGetEmitter(DemoPlayArea.LiftVoiceName, out SoundEmitter now).ShouldBeTrue();
                now.Id.ShouldBe(first.Id);
            }
        }

        rig.Outputs.Fired.Count(fired => fired.EndsWith(pressed)).ShouldBe(2);
        rig.World.TickNumber.ShouldBeGreaterThan(start + LineTicks);
    }

    [Fact]
    public void The_button_sends_the_lift_up_with_the_player_on_it_and_it_comes_back_down()
    {
        using var rig = new StartRoomSoundRig(_cooked);
        rig.Session.Enter();
        SceneNode liftNode = rig.Node(DemoPlayArea.LiftName);
        FuncMoveLinear lift = rig.Live<FuncMoveLinear>(DemoPlayArea.LiftName);

        rig.PressTheLiftButton();
        rig.Character.State.Grounded.ShouldBeTrue();
        rig.Character.State.GroundNodeId.ShouldBe(liftNode.Id);
        rig.Idle(TicksFromUseToLift + lift.TravelTicks + 5);

        lift.TicksTravelled.ShouldBe(lift.TravelTicks);
        liftNode.LocalPosition.Y.ShouldBe(2.35f, 1e-4f);
        rig.Character.State.Grounded.ShouldBeTrue();

        // Level with the tops of the walls, which are 2.5 high.
        rig.Character.State.Position.Y.ShouldBe(2.5f, 0.05f);

        // Three seconds up there, then the way down.
        rig.Idle(180 + lift.TravelTicks + 5);

        lift.TicksTravelled.ShouldBe(0);
        rig.Character.State.Grounded.ShouldBeTrue();
        rig.Character.State.Position.Y.ShouldBe(0.3f, 0.05f);
    }

    // Uses the button and runs to the tick the line starts on. The button
    // takes the press on the next tick, and its wire reaches the sound on
    // that tick too.
    private static long StartTheLine(StartRoomSoundRig rig)
    {
        rig.PressTheLiftButton();
        rig.Idle(1);

        rig.TryGetEmitter(DemoPlayArea.LiftVoiceName, out SoundEmitter line).ShouldBeTrue();
        line.StartTick.ShouldBe(rig.World.TickNumber);
        return line.StartTick;
    }
}
