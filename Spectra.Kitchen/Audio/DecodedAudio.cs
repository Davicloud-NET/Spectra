using SpectraEngine.Core.Audio;

namespace Spectra.Kitchen.Audio;

/// <summary>
/// A decoded source file: interleaved PCM16 at the file's own rate, plus the
/// loop it declared. The loop is in sample frames and is not validated here.
/// </summary>
/// <param name="SampleRate">Frames a second, as the file states it.</param>
/// <param name="Channels">1 or 2.</param>
/// <param name="Samples">Interleaved PCM16, <c>FrameCount * Channels</c> long.</param>
/// <param name="Loop">The declared loop region, or <see cref="LoopRegion.None"/>.</param>
/// <param name="LoopWasRefused">
/// True when the file declared a loop the engine cannot carry and the decoder dropped it.
/// </param>
public readonly record struct DecodedAudio(
    int SampleRate,
    int Channels,
    short[] Samples,
    LoopRegion Loop,
    bool LoopWasRefused)
{
    /// <summary>Decoded length in sample frames.</summary>
    public long FrameCount => Samples.Length / Channels;

    /// <summary>Seconds of audio.</summary>
    public double Duration => (double)FrameCount / SampleRate;
}
