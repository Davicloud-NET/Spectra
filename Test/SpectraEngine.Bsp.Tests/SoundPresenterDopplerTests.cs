using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Doppler on the audio device: the pitch a voice is given while its sound
/// and the listener close in or part, at the frame rates a level is seen at.
/// </summary>
public sealed class SoundPresenterDopplerTests
{
    // How close a steady voice comes to its pitch times the factor.
    private const double Tolerance = 0.0005;

    // The most a steady voice's pitch wanders, in cents. Measured: 0.004.
    private const double Flutter = 0.05;

    private const float Speed = 34.3f;
    private const double Closing = 343.0 / (343.0 - 34.3);

    // Heard from far enough away to close in on for seconds.
    private static readonly SoundEmitterSettings Siren = SoundPresenterRig.Looped with { MaxDistance = 100_000f };

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_that_closes_in_plays_at_its_pitch_times_1_111(int framesPerSecond)
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -100));
        rig.Play(siren, SoundPresenterRig.Beep, Siren with { Pitch = 1.5f });
        var pacer = new SoundFramePacer(rig, framesPerSecond)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -100 + (Speed * (float)ticked)),
        };

        pacer.Run(1);

        ((double)rig.OnlyVoice().Pitch).ShouldBe(1.5 * Closing, 1.5 * Tolerance);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_listener_that_closes_in_on_a_still_sound_hears_the_same(int framesPerSecond)
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -100)), SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, framesPerSecond)
        {
            OnFrame = now => rig.Listen(new Vector3(0, 0, -Speed * (float)now)),
        };

        pacer.Run(1);

        ((double)rig.OnlyVoice().Pitch).ShouldBe(Closing, Tolerance);
    }

    [Fact]
    public void A_sound_that_parts_plays_lower()
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -10));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -10 - (Speed * (float)ticked)),
        };

        pacer.Run(1);

        ((double)rig.OnlyVoice().Pitch).ShouldBe(343.0 / (343.0 + 34.3), Tolerance);
    }

    [Fact]
    public void With_nothing_moving_a_voice_plays_at_exactly_its_sounds_pitch()
    {
        using var rig = new SoundPresenterRig();
        rig.Listen(new Vector3(1, 2, 3));
        rig.Play(rig.Place("siren", new Vector3(0.1f, 0.7f, -8.3f)), SoundPresenterRig.Beep, Siren with { Pitch = 1.5f });
        var pacer = new SoundFramePacer(rig, 144)
        {
            AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1.5f),
        };

        pacer.Run(1);
    }

    [Theory]
    [InlineData("doppler off")]
    [InlineData("not placed")]
    [InlineData("stereo")]
    public void A_sound_without_doppler_keeps_its_pitch_while_it_closes_in(string which)
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -100));
        rig.Play(
            siren,
            which == "stereo" ? SoundPresenterRig.Music : SoundPresenterRig.Beep,
            which switch
            {
                "doppler off" => Siren with { Simulated = SoundSimulation.All & ~SoundSimulation.Doppler },
                "not placed" => Siren with { Simulated = SoundSimulation.All & ~SoundSimulation.Placed },
                _ => Siren,
            });

        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -100 + (Speed * (float)ticked)),
            AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1f),
        };

        pacer.Run(1);
    }

    [Fact]
    public void A_stereo_sound_the_listener_walks_into_hearing_of_starts_at_its_own_pitch()
    {
        // Out of earshot its file is not loaded, and nothing says it is stereo.
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("radio", new Vector3(0, 0, -60)), SoundPresenterRig.Music, SoundPresenterRig.Looped);
        int framesHeard = 0;
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnFrame = now => rig.Listen(new Vector3(0, 0, -20 * (float)now)),
            AfterFrame = () =>
            {
                foreach (uint source in rig.Backend.PlayingSources())
                {
                    rig.Backend.SettingsWhenStarted(source).Pitch.ShouldBe(1f);
                    rig.Backend.SettingsOf(source).Pitch.ShouldBe(1f);
                    framesHeard++;
                }
            },
        };

        pacer.Run(1);
        framesHeard.ShouldBe(0);

        pacer.Run(1.5);

        framesHeard.ShouldBeGreaterThan(0);
        rig.OnlyVoice().Relative.ShouldBeTrue();
    }

    [Fact]
    public void With_doppler_off_in_the_engine_nothing_shifts_until_it_is_switched_on_again()
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -200));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -200 + (Speed * (float)ticked)),
        };

        rig.Presenter.Simulation.Set(SoundSimulation.Doppler, isOn: false);
        pacer.AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1f);
        pacer.Run(1);

        // It comes in from the sound's own pitch, with no step.
        rig.Presenter.Simulation.Set(SoundSimulation.Doppler, isOn: true);
        pacer.AfterFrame = null;
        pacer.Frame();
        rig.OnlyVoice().Pitch.ShouldBe(1f);

        pacer.Run(1);
        ((double)rig.OnlyVoice().Pitch).ShouldBe(Closing, Tolerance);
    }

    [Fact]
    public void Strength_0_shifts_nothing_and_2_doubles_the_shift_in_cents()
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -300));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -300 + (Speed * (float)ticked)),
        };

        rig.Presenter.Simulation.DopplerStrength = 0f;
        pacer.Run(1);
        rig.OnlyVoice().Pitch.ShouldBe(1f);

        rig.Presenter.Simulation.DopplerStrength = 1f;
        pacer.Run(1);
        double real = DopplerBench.Cents(rig.OnlyVoice().Pitch);

        rig.Presenter.Simulation.DopplerStrength = 2f;
        pacer.Run(1);
        double doubled = DopplerBench.Cents(rig.OnlyVoice().Pitch);

        real.ShouldBe(DopplerBench.Cents(Closing), 0.5);
        doubled.ShouldBe(2 * real, 0.5);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(5f)]
    public void A_listener_that_teleports_hears_no_shift_on_that_frame_and_no_chirp_after(float units)
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -40)), SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1f),
        };
        pacer.Run(0.5);

        rig.Listen(new Vector3(0, 0, -units));

        pacer.Run(1);
    }

    // Too short to tell from fast motion by the positions alone.
    [Theory]
    [InlineData(60, 1f, 0f)]
    [InlineData(60, 2.8f, 0f)]
    [InlineData(144, 1f, 0f)]
    [InlineData(144, 2f, 0f)]
    [InlineData(240, 1f, 0f)]
    [InlineData(240, 2.8f, 0f)]
    [InlineData(144, 0f, 6f)]
    public void A_short_hop_the_presenter_is_told_of_is_no_shift_on_that_frame_and_no_chirp_after(
        int framesPerSecond, float closer, float sideways)
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -10)), SoundPresenterRig.Beep, Siren);
        bool hopped = false;
        var pacer = new SoundFramePacer(rig, framesPerSecond)
        {
            // In a tick, as a trigger moves the player.
            OnTick = ticked =>
            {
                if (hopped || ticked < 0.5)
                    return;

                rig.Listen(new Vector3(sideways, 0, -closer));
                rig.Presenter.ListenerJumped();
                hopped = true;
            },
            AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1f),
        };

        pacer.Run(1.5);

        hopped.ShouldBeTrue();
    }

    [Fact]
    public void A_hop_the_presenter_is_told_of_lands_on_the_new_loudness_at_once()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -10)), SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        rig.Frame(30);
        rig.OnlyVoice().Gain.ShouldBe(SoundFalloff.Gain(10f, 2f, 30f));

        rig.Listen(new Vector3(0, 0, -4));
        rig.Presenter.ListenerJumped();
        rig.Frame();

        rig.OnlyVoice().Gain.ShouldBe(SoundFalloff.Gain(6f, 2f, 30f));
    }

    [Fact]
    public void Being_told_of_a_jump_holds_for_one_frame_only()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -400)), SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnFrame = now => rig.Listen(new Vector3(0, 0, -Speed * (float)now)),
        };
        rig.Presenter.ListenerJumped();

        pacer.Run(1);

        ((double)rig.OnlyVoice().Pitch).ShouldBe(Closing, Tolerance);
    }

    [Fact]
    public void A_listener_that_teleports_while_closing_in_starts_over_from_the_sounds_own_pitch()
    {
        using var rig = new SoundPresenterRig();
        rig.Play(rig.Place("siren", new Vector3(0, 0, -400)), SoundPresenterRig.Beep, Siren);
        float jump = 0f;
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnFrame = now => rig.Listen(new Vector3(0, 0, -jump - (Speed * (float)now))),
        };
        pacer.Run(1);
        float steady = rig.OnlyVoice().Pitch;
        ((double)steady).ShouldBe(Closing, Tolerance);

        jump = 50f;
        pacer.Frame();
        rig.OnlyVoice().Pitch.ShouldBe(1f);

        float highest = 0f;
        pacer.AfterFrame = () => highest = MathF.Max(highest, rig.OnlyVoice().Pitch);
        pacer.Run(1);

        highest.ShouldBeLessThanOrEqualTo(steady + 0.0002f);
        ((double)rig.OnlyVoice().Pitch).ShouldBe(Closing, Tolerance);
    }

    [Fact]
    public void A_sound_that_teleports_plays_at_its_own_pitch_on_that_frame_and_after()
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -40));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, ticked < 0.5 ? -40 : -37),
            AfterFrame = () => rig.OnlyVoice().Pitch.ShouldBe(1f),
        };

        pacer.Run(1.5);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void A_sound_passing_at_20_units_a_second_does_not_flutter(int framesPerSecond)
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(3, 1, -200));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        rig.Listen(new Vector3(3, 1, 0));
        var pacer = new SoundFramePacer(rig, framesPerSecond)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(3, 1, -200 + (20 * (float)ticked)),
        };
        pacer.Run(1);

        double lowest = double.PositiveInfinity;
        double highest = double.NegativeInfinity;
        pacer.AfterFrame = () =>
        {
            double cents = DopplerBench.Cents(rig.OnlyVoice().Pitch);
            lowest = Math.Min(lowest, cents);
            highest = Math.Max(highest, cents);
        };
        pacer.Run(4);

        (highest - lowest).ShouldBeLessThan(Flutter);
        lowest.ShouldBe(DopplerBench.Cents(343.0 / 323.0), Flutter);
    }

    [Fact]
    public void A_new_level_starts_with_no_shift()
    {
        using var rig = new SoundPresenterRig();
        SceneNode siren = rig.Place("siren", new Vector3(0, 0, -100));
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        var pacer = new SoundFramePacer(rig, 144)
        {
            OnTick = ticked => siren.LocalPosition = new Vector3(0, 0, -100 + (Speed * (float)ticked)),
        };
        pacer.Run(1);
        rig.OnlyVoice().Pitch.ShouldBeGreaterThan(1.1f);

        rig.StartLevel();
        rig.Play(siren, SoundPresenterRig.Beep, Siren);
        pacer.Frame();

        rig.OnlyVoice().Pitch.ShouldBe(1f);
    }

    [Fact]
    public void Two_hundred_moving_sounds_allocate_nothing_a_frame_once_running()
    {
        using var rig = new SoundPresenterRig(sources: 32);
        SceneNode train = rig.Place("train", Vector3.Zero);
        for (int i = 0; i < 200; i++)
        {
            SceneNode speaker = train.CreateChild($"speaker{i}");
            speaker.LocalPosition = new Vector3((i % 20) - 10, 0, -(i / 20) - 1);
            rig.Play(speaker, SoundPresenterRig.Beep, SoundPresenterRig.Looped);
        }

        rig.Backend.KeepsUploads = false;
        RunTrain(rig, train, 120);
        rig.Stats.WithSource.ShouldBe(32);

        // The least of several rounds: a one-off from the runtime is not a
        // cost per frame.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunTrain(rig, train, 100);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        least.ShouldBe(0L);

        // They were shifted while they were counted.
        rig.Backend.PlayingSources().Count(source => rig.Backend.SettingsOf(source).Pitch != 1f).ShouldBe(32);
    }

    // The train runs back and forth at 6 units a second, and frames come
    // faster than ticks, so every sound's path changes on both clocks.
    private static void RunTrain(SoundPresenterRig rig, SceneNode train, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            long tick = rig.World.TickNumber;
            float along = (tick % 120) < 60 ? tick % 60 : 60 - (tick % 60);
            train.LocalPosition = new Vector3(0, 0, along * 0.1f);
            rig.World.Tick(SoundPresenterRig.TickSeconds);

            for (int frame = 0; frame < 2; frame++)
            {
                rig.Listen(new Vector3(((tick * 2) + frame) * 0.001f, 0, 12));
                rig.Backend.ConsumeOneEverywhere();
                rig.Audio.Update();
                rig.Presenter.Update(rig.World, SoundPresenterRig.TickSeconds / 2);
            }
        }
    }
}
