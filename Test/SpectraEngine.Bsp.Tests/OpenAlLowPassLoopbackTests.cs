using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// The low-pass filter against the real OpenAL Soft, in numbers. Sounds are
/// rendered into memory through the library's loopback device. Nothing is
/// played and no sound card is needed.
/// </summary>
// Opt-in, so a plain run of the suite loads no audio library. To run them:
//   PowerShell:  $env:SPECTRA_AUDIO_LOOPBACK = "1"
//   bash:        export SPECTRA_AUDIO_LOOPBACK=1
//   dotnet run --project Test/SpectraEngine.Bsp.Tests -- -trait "Suite=AudioLoopback"
// Levels are compared with a tolerance, not as bytes: the library promises no
// bytes from one version to the next.
[Collection(AudioLoopbackCollection.Name)]
[Trait("Suite", "AudioLoopback")]
public sealed class OpenAlLowPassLoopbackTests
{
    // In dB. The expected levels were measured on OpenAL Soft 1.23.1, and this
    // rig reads them back within 0.005 dB.
    private const double Tolerance = 0.05;

    // The most a 200 Hz tone may move between two samples while its high-end
    // gain is smoothed, as a share of its amplitude. Measured: 4.0%. The tone
    // alone moves 2.6%.
    private const double WorstJump = 0.045;

    private const int Frame = LoopbackAudio.Rate / 60;

    private static readonly TestTone Low = new(200, 0.2);
    private static readonly TestTone Mid = new(5000, 0.2);
    private static readonly TestTone High = new(8000, 0.2);

    private static readonly TestTone LowAlone = new(200, 0.5);

    [Fact]
    public void A_gain_hf_of_one_is_the_same_as_no_filter()
    {
        AudioLoopbackCollection.Require();

        float[] unfiltered = FirstWindow(gainHf: 1f, withheld: name => name == "ALC_EXT_EFX");
        float[] clear = FirstWindow(gainHf: 1f, withheld: null);

        LargestDifference(clear, unfiltered).ShouldBeLessThan(1e-6);
        Decibels(clear, Low).ShouldBe(0, Tolerance);
        Decibels(clear, Mid).ShouldBe(0, Tolerance);
        Decibels(clear, High).ShouldBe(0, Tolerance);
    }

    [Theory]
    [InlineData(0.5f, -6.02, -10.47)]
    [InlineData(0.25f, -12.04, -19.47)]
    [InlineData(0.1f, -20.00, -28.88)]
    public void Gain_hf_is_the_level_at_5_kHz_and_the_low_end_passes(float gainHf, double at5kHz, double at8kHz)
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio();

        rig.Play(rig.Clip(Low, Mid, High), gainHf);
        float[] window = rig.SettledWindow();

        Decibels(window, Low).ShouldBe(0, Tolerance);
        Decibels(window, Mid).ShouldBe(at5kHz, Tolerance);
        Decibels(window, High).ShouldBe(at8kHz, Tolerance);
        rig.Logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(rig.Logger.Describe());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_dull_source_taken_by_another_sound_is_clear_again(bool looping)
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio(sources: 1);
        AudioVoice dull = rig.Play(rig.Clip(High), gainHf: 0.1f);
        uint source = dull.Source;
        Decibels(rig.SettledWindow(), High).ShouldBe(-28.88, Tolerance);

        // The pool has one source, so the next sound takes it while it plays.
        AudioVoice next = rig.Play(looping ? rig.LoopingClip(High) : rig.Clip(High));

        next.Source.ShouldBe(source);
        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);
    }

    [Fact]
    public void A_dull_source_that_was_given_back_is_clear_for_the_next_sound()
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio(sources: 1);
        AudioClip clip = rig.Clip(High);
        AudioVoice dull = rig.Play(clip, gainHf: 0.1f);
        rig.Render(LoopbackAudio.Window);

        rig.Audio.Release(dull);
        rig.Play(clip);

        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);
    }

    [Fact]
    public void A_source_handed_on_at_the_gain_hf_it_had_is_as_dull_as_before()
    {
        // The backend makes no call for a value the source already has, so
        // this holds only while a source keeps its filter from sound to sound.
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio(sources: 1);
        AudioClip clip = rig.Clip(High);
        AudioVoice first = rig.Play(clip, gainHf: 0.1f);
        rig.Render(LoopbackAudio.Window);

        rig.Audio.Release(first);
        rig.Play(clip, gainHf: 0.1f);

        Decibels(rig.SettledWindow(), High).ShouldBe(-28.88, Tolerance);
    }

    [Fact]
    public void Two_sources_keep_their_own_gain_hf()
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio();
        var higher = new TestTone(10000, 0.2);

        // One filter object is set for each in turn.
        rig.Play(rig.Clip(High), gainHf: 0.1f);
        rig.Play(rig.Clip(higher), gainHf: 0.5f);
        float[] window = rig.SettledWindow();

        Decibels(window, High).ShouldBe(-28.88, Tolerance);
        Decibels(window, higher).ShouldBe(-11.46, Tolerance);
    }

    [Fact]
    public void A_change_of_gain_hf_reaches_a_sound_that_is_playing()
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio();
        AudioVoice voice = rig.Play(rig.Clip(Low, Mid, High));
        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);

        voice.Configure(AudioSourceSettings.Default with { GainHf = 0.25f });
        float[] dull = rig.SettledWindow();

        Decibels(dull, Low).ShouldBe(0, Tolerance);
        Decibels(dull, Mid).ShouldBe(-12.04, Tolerance);
        Decibels(dull, High).ShouldBe(-19.47, Tolerance);

        voice.Configure(AudioSourceSettings.Default);

        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);
    }

    [Fact]
    public void A_smoothed_move_from_clear_to_dull_knocks_the_low_end_by_no_more_than_the_limit()
    {
        AudioLoopbackCollection.Require();

        // Each lead-in has the steps meet the tone at other points of its
        // cycle. Together they cover it every 15 degrees.
        for (int leadIn = 0; leadIn < 80; leadIn += 10)
        {
            using var rig = new LoopbackAudio();
            AudioVoice voice = rig.Play(rig.Clip(LowAlone));
            float previous = rig.Render(LoopbackAudio.Window + leadIn)[^1];
            var smoother = new SoundPathSmoother();
            smoother.Step(new SoundPath(Vector3.Zero, 1f, 1f), 1f / 60f);

            double largest = 0;
            var block = new float[Frame];
            for (int updates = 0; smoother.GainHf != 0.1f; updates++)
            {
                updates.ShouldBeLessThan(120);
                smoother.Step(new SoundPath(Vector3.Zero, 1f, 0.1f), 1f / 60f);
                voice.Configure(AudioSourceSettings.Default with { GainHf = smoother.GainHf });
                rig.Render(block);
                largest = Math.Max(largest, ToneSignal.LargestMove(block, ref previous));
            }

            (largest / LowAlone.Amplitude).ShouldBeLessThan(WorstJump, $"lead-in {leadIn}");
        }
    }

    [Fact]
    public void One_large_step_in_gain_hf_knocks_the_low_end_hard()
    {
        // Why the smoother takes small steps. If this fails after an OpenAL
        // Soft upgrade, the library eases the filter in itself and
        // SoundPathSmoother's step limit can go.
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio();
        AudioVoice voice = rig.Play(rig.Clip(LowAlone));

        // 60 samples past a whole cycle: the tone is on its peak.
        float previous = rig.Render(LoopbackAudio.Window + 60)[^1];

        voice.Configure(AudioSourceSettings.Default with { GainHf = 0.1f });
        double largest = ToneSignal.LargestMove(rig.Render(Frame), ref previous);

        (largest / LowAlone.Amplitude).ShouldBeGreaterThan(0.5);
    }

    [Fact]
    public void A_gain_hf_outside_its_range_raises_no_error_in_the_library()
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio();
        AudioClip clip = rig.Clip(High);
        AudioVoice voice = rig.Play(clip, gainHf: float.NaN);
        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);

        voice.Configure(AudioSourceSettings.Default with { GainHf = -1f });
        voice.Configure(AudioSourceSettings.Default with { GainHf = 5f });
        Decibels(rig.SettledWindow(), High).ShouldBe(0, Tolerance);

        // Making a buffer reads the library's error latch and warns about what it finds.
        rig.Clip(Low);
        rig.Logger.MessagesAt(LogLevel.Warning).ShouldBeEmpty(rig.Logger.Describe());
    }

    [Theory]
    [InlineData("ALC_EXT_EFX")]
    [InlineData("alGenFilters")]
    [InlineData("alDeleteFilters")]
    [InlineData("alFilteri")]
    [InlineData("alFilterf")]
    public void A_library_that_lacks_a_part_of_the_filter_plays_unfiltered_and_warns_once(string missing)
    {
        AudioLoopbackCollection.Require();
        using var rig = new LoopbackAudio(withheld: name => name == missing);
        AudioVoice voice = rig.Play(rig.Clip(Low, High), gainHf: 0.1f);

        for (int frame = 0; frame < 30; frame++)
        {
            voice.Configure(AudioSourceSettings.Default with { GainHf = 0.1f + (frame * 0.01f) });
            rig.Audio.Update();
            rig.Render(Frame);
        }

        float[] window = rig.Render(LoopbackAudio.Window);
        Decibels(window, Low).ShouldBe(0, Tolerance);
        Decibels(window, High).ShouldBe(0, Tolerance);

        string warning = rig.Logger.MessagesAt(LogLevel.Warning).ShouldHaveSingleItem();
        warning.ShouldContain(missing);
        warning.ShouldContain("quieter but not duller");
        rig.Logger.MessagesAt(LogLevel.Error).ShouldBeEmpty(rig.Logger.Describe());
    }

    private static float[] FirstWindow(float gainHf, Predicate<string>? withheld)
    {
        using var rig = new LoopbackAudio(withheld: withheld);
        rig.Play(rig.Clip(Low, Mid, High), gainHf);
        return rig.Render(LoopbackAudio.Window);
    }

    private static double Decibels(float[] window, TestTone tone) =>
        ToneSignal.Decibels(ToneSignal.Level(window, LoopbackAudio.Rate, tone.Hz) / tone.Amplitude);

    private static double LargestDifference(float[] one, float[] other)
    {
        one.Length.ShouldBe(other.Length);

        double largest = 0;
        for (int i = 0; i < one.Length; i++)
            largest = Math.Max(largest, Math.Abs(one[i] - other[i]));

        return largest;
    }
}
