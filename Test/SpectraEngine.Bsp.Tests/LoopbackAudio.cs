using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Audio;
using System.Diagnostics.CodeAnalysis;

namespace SpectraEngine.Bsp.Tests;

// An AudioManager over the real OpenAL Soft, rendering into memory. Nothing
// reaches a sound card, and a sound only moves on when samples are rendered.
internal sealed class LoopbackAudio : IDisposable
{
    public const int Rate = 48000;

    // 0.1 s. Holds whole cycles of every tone that is a multiple of 10 Hz.
    public const int Window = 4800;

    private OpenAlBackend? _backend;

    // withheld names the extensions and entry points the library is to seem to lack.
    public LoopbackAudio(int sources = 4, Predicate<string>? withheld = null)
    {
        Audio = new AudioManager(
            Logger,
            (ILogger logger, [NotNullWhen(true)] out IAudioBackend? created, out string reason) =>
            {
                bool isOpen = OpenAlBackend.TryCreateLoopback(logger, out _backend, out reason, withheld);
                created = _backend;
                return isOpen;
            },
            sources);

        Audio.Initialize();
        Audio.IsEnabled.ShouldBeTrue(Audio.DisabledReason);
    }

    public CapturingLogger Logger { get; } = new();

    public AudioManager Audio { get; }

    // Two seconds of the tones, as a sound that plays once.
    public AudioClip Clip(params TestTone[] tones) =>
        Audio.CreateClip(new AudioFormat(Rate, 1), ToneSignal.Synthesize(2 * Rate, Rate, tones)).ShouldNotBeNull();

    // The same tones as a sound that loops, which plays through a buffer queue.
    public AudioClip LoopingClip(params TestTone[] tones) =>
        Audio.CreateClip(
            new AudioFormat(Rate, 1),
            ToneSignal.Synthesize(2 * Rate, Rate, tones),
            new LoopRegion(0, 2 * Rate)).ShouldNotBeNull();

    public AudioVoice Play(AudioClip clip, float gainHf = 1f) =>
        Audio.Play(clip, AudioSourceSettings.Default with { GainHf = gainHf }).ShouldNotBeNull();

    public float[] Render(int frames)
    {
        var samples = new float[frames];
        Render(samples);
        return samples;
    }

    public void Render(Span<float> samples) => _backend.ShouldNotBeNull().Render(samples);

    // One window of the mix, taken after a window that lets a change settle.
    public float[] SettledWindow()
    {
        Render(Window);
        return Render(Window);
    }

    public void Dispose() => Audio.Dispose();
}
