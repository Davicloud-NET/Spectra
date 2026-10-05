using SpectraEngine.Core.Audio;
using SpectraEngine.Core.Audio.Propagation;
using System.Buffers.Binary;
using System.Numerics;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// Doppler through the real OpenAL Soft: the tone that comes out of the
/// library is higher by the factor. Rendered into memory. Nothing is played.
/// </summary>
// Opt-in, like the other tests of this collection: SPECTRA_AUDIO_LOOPBACK=1.
[Collection(AudioLoopbackCollection.Name)]
[Trait("Suite", "AudioLoopback")]
public sealed class DopplerLoopbackTests
{
    private const string Siren = "Sounds/siren.wav";

    private const int Frame = LoopbackAudio.Rate / 60;

    // 900 Hz heard while closing in at a tenth of the speed of sound is
    // 1000 Hz: the factor is ten ninths. Both fit the window in whole cycles.
    private const double Still = 900;
    private const double ClosingIn = 1000;
    private const float Speed = 34.3f;

    [Fact]
    public void A_tone_the_listener_closes_in_on_comes_out_higher_by_the_factor()
    {
        AudioLoopbackCollection.Require();
        using var walk = new Walk();

        float[] standing = walk.Listen(seconds: 0.5, speed: 0f);
        Share(standing, Still).ShouldBeGreaterThan(0.98);
        Share(standing, ClosingIn).ShouldBeLessThan(0.02);

        float[] closing = walk.Listen(seconds: 1.0, speed: Speed);
        Share(closing, ClosingIn).ShouldBeGreaterThan(0.98);
        Share(closing, Still).ShouldBeLessThan(0.02);

        float[] standingAgain = walk.Listen(seconds: 1.0, speed: 0f);
        Share(standingAgain, Still).ShouldBeGreaterThan(0.98);
        Share(standingAgain, ClosingIn).ShouldBeLessThan(0.02);
    }

    // How much of what was rendered is the tone at this frequency: 1 when
    // it is all of it.
    private static double Share(float[] window, double hz)
    {
        double squares = 0;
        foreach (float sample in window)
            squares += sample * sample;

        double amplitude = Math.Sqrt(2 * squares / window.Length);
        amplitude.ShouldBeGreaterThan(0.05, "nothing was rendered");

        return ToneSignal.Level(window, LoopbackAudio.Rate, hz) / amplitude;
    }

    // A cooked mono sound that is one tone.
    private static byte[] ToneFile(double hz, int seconds)
    {
        int frames = seconds * LoopbackAudio.Rate;
        short[] pcm = ToneSignal.Synthesize(frames, LoopbackAudio.Rate, [new TestTone(hz, 0.5)]);

        byte[] file = HandBuiltSaudio.Resident(frames, sampleRate: LoopbackAudio.Rate);
        for (int i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(HandBuiltSaudio.HeaderSize + (i * 2)), pcm[i]);

        return file;
    }

    // A level with a siren 200 units ahead, a presenter that plays it on
    // the real library, and a listener that walks at it.
    private sealed class Walk : IDisposable
    {
        private readonly SoundPresenterRig _level = new();
        private readonly LoopbackAudio _loopback = new();
        private readonly SoundPresenter _presenter;
        private float _walked;

        public Walk()
        {
            _level.Cook(Siren, ToneFile(Still, seconds: 2));
            _presenter = new SoundPresenter(
                _loopback.Audio, _level.Assets, new DirectPropagation(), _level.NewCaptionFeed(), _level.Log);

            // As loud at any distance, so the tone's level holds still while its pitch is read.
            _level.Play(
                _level.Place("siren", new Vector3(0, 0, -200)),
                Siren,
                SoundPresenterRig.Looped with { Simulated = SoundSimulation.All & ~SoundSimulation.Fades });
        }

        public void Dispose()
        {
            _loopback.Dispose();
            _level.Dispose();
        }

        // Runs the level a frame at a time and returns the last tenth of a
        // second that was rendered.
        public float[] Listen(double seconds, float speed)
        {
            var window = new float[LoopbackAudio.Window];
            int frames = (int)Math.Round(seconds * 60);
            int firstKept = frames - (LoopbackAudio.Window / Frame);

            for (int frame = 0; frame < frames; frame++)
            {
                _walked += speed / 60f;
                _level.Tick();
                _loopback.Audio.SetListener(new Vector3(0, 0, -_walked), -Vector3.UnitZ, Vector3.UnitY);
                _loopback.Audio.Update();
                _presenter.Update(_level.World, SoundPresenterRig.TickSeconds);

                if (frame >= firstKept)
                    _loopback.Render(window.AsSpan((frame - firstKept) * Frame, Frame));
                else
                    _loopback.Render(Frame);
            }

            return window;
        }
    }
}
