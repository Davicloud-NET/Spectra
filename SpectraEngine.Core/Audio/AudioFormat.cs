using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// Sample rate and channel count of a block of PCM16. Positions and lengths
/// in this namespace are in sample frames (one sample per channel), and the
/// conversions to samples and seconds live here.
/// </summary>
// Mono or stereo only: that is all OpenAL's PCM16 formats carry. OpenAL does
// not spatialise a stereo buffer, so positional sounds want mono.
public readonly struct AudioFormat : IEquatable<AudioFormat>
{
    /// <summary>Bytes per sample of one channel.</summary>
    public const int BytesPerSample = sizeof(short);

    /// <param name="sampleRate">Frames per second. Must be positive.</param>
    /// <param name="channels">1 (mono) or 2 (stereo).</param>
    public AudioFormat(int sampleRate, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        if (channels is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono and stereo PCM16 exist.");

        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Sample frames per second.</summary>
    public int SampleRate { get; }

    /// <summary>Interleaved channels per sample frame: 1 or 2.</summary>
    public int Channels { get; }

    /// <summary>False for a default-constructed value.</summary>
    public bool IsValid => SampleRate > 0 && Channels > 0;

    /// <summary>Interleaved samples in <paramref name="frames"/> sample frames.</summary>
    public long FramesToSamples(long frames) => frames * Channels;

    /// <summary>Whole sample frames in <paramref name="samples"/> interleaved samples.</summary>
    public long SamplesToFrames(long samples) => samples / Channels;

    /// <summary>Seconds of audio in <paramref name="frames"/> sample frames.</summary>
    public double FramesToSeconds(long frames) => (double)frames / SampleRate;

    /// <summary>Whole sample frames in <paramref name="seconds"/> of audio.</summary>
    public long SecondsToFrames(double seconds) => (long)(seconds * SampleRate);

    public bool Equals(AudioFormat other) => SampleRate == other.SampleRate && Channels == other.Channels;

    public override bool Equals(object? obj) => obj is AudioFormat other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(SampleRate, Channels);

    public override string ToString() => $"{SampleRate} Hz, {(Channels == 1 ? "mono" : "stereo")}";

    public static bool operator ==(AudioFormat left, AudioFormat right) => left.Equals(right);

    public static bool operator !=(AudioFormat left, AudioFormat right) => !left.Equals(right);
}
