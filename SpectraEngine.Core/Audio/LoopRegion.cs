using System;

namespace SpectraEngine.Core.Audio;

/// <summary>
/// The half-open region <c>[StartFrame, EndFrame)</c> a sound repeats, in
/// sample frames. <see cref="None"/> is the default and means play once.
/// </summary>
// Frames, not seconds: a loop point in seconds can be a sample off, and that
// clicks on every pass.
public readonly struct LoopRegion : IEquatable<LoopRegion>
{
    /// <summary>No loop: the sound plays once and finishes.</summary>
    public static LoopRegion None => default;

    /// <param name="endFrame">One past the last frame. An empty region throws: it would hang the fill loop.</param>
    public LoopRegion(long startFrame, long endFrame)
    {
        if (startFrame < 0)
            throw new ArgumentOutOfRangeException(nameof(startFrame), startFrame, "A loop cannot start before the sound.");
        if (endFrame <= startFrame)
            throw new ArgumentOutOfRangeException(nameof(endFrame), endFrame, "A loop region must contain at least one frame.");

        StartFrame = startFrame;
        EndFrame = endFrame;
    }

    /// <summary>First frame of the repeated region.</summary>
    public long StartFrame { get; }

    /// <summary>One past the last frame of the repeated region; 0 means no loop.</summary>
    public long EndFrame { get; }

    /// <summary>False for <see cref="None"/>.</summary>
    public bool IsLooping => EndFrame > StartFrame;

    /// <summary>Frames the region repeats; 0 when there is no loop.</summary>
    public long LengthFrames => IsLooping ? EndFrame - StartFrame : 0;

    public bool Equals(LoopRegion other) => StartFrame == other.StartFrame && EndFrame == other.EndFrame;

    public override bool Equals(object? obj) => obj is LoopRegion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StartFrame, EndFrame);

    public override string ToString() => IsLooping ? $"[{StartFrame}, {EndFrame}) frames" : "no loop";

    public static bool operator ==(LoopRegion left, LoopRegion right) => left.Equals(right);

    public static bool operator !=(LoopRegion left, LoopRegion right) => !left.Equals(right);
}
