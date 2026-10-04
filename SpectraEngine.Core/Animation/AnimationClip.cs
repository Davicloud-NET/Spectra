using System;
using System.Collections.Generic;
using System.Numerics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Animation;

/// <summary>A vector keyframe: a time in seconds and the value at it.</summary>
public readonly record struct Vector3Key(float Time, Vector3 Value);

/// <summary>A rotation keyframe: a time in seconds and the value at it.</summary>
public readonly record struct QuaternionKey(float Time, Quaternion Value);

/// <summary>
/// One bone's animation: independent position, rotation and scale tracks. An
/// empty track keeps the bind value. Position and scale lerp, rotation slerps.
/// </summary>
public sealed class AnimationChannel
{
    private static readonly Vector3Key[] NoVectors = [];
    private static readonly QuaternionKey[] NoRotations = [];

    public AnimationChannel(
        int boneIndex,
        Vector3Key[]? positions = null,
        QuaternionKey[]? rotations = null,
        Vector3Key[]? scales = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(boneIndex);

        BoneIndex = boneIndex;
        Positions = positions ?? NoVectors;
        Rotations = rotations ?? NoRotations;
        Scales = scales ?? NoVectors;
    }

    /// <summary>The bone this channel drives, resolved against the skeleton at import time.</summary>
    public int BoneIndex { get; }

    public Vector3Key[] Positions { get; }

    public QuaternionKey[] Rotations { get; }

    public Vector3Key[] Scales { get; }

    /// <summary>
    /// The transform this channel produces at <paramref name="time"/>, falling
    /// back to <paramref name="bind"/> for any track it does not carry.
    /// </summary>
    public Transform SampleAt(float time, in Transform bind) => new()
    {
        Position = SampleVector(Positions, time, bind.Position),
        Rotation = SampleRotation(Rotations, time, bind.Rotation),
        Scale = SampleVector(Scales, time, bind.Scale),
    };

    private static Vector3 SampleVector(Vector3Key[] keys, float time, Vector3 fallback)
    {
        if (keys.Length == 0)
            return fallback;
        if (keys.Length == 1)
            return keys[0].Value;

        int i = FindSegment(keys, time);
        if (i < 0)
            return keys[0].Value;
        if (i >= keys.Length - 1)
            return keys[^1].Value;

        float t = SegmentFraction(keys[i].Time, keys[i + 1].Time, time);
        return Vector3.Lerp(keys[i].Value, keys[i + 1].Value, t);
    }

    private static Quaternion SampleRotation(QuaternionKey[] keys, float time, Quaternion fallback)
    {
        if (keys.Length == 0)
            return fallback;
        if (keys.Length == 1)
            return keys[0].Value;

        int i = FindSegment(keys, time);
        if (i < 0)
            return keys[0].Value;
        if (i >= keys.Length - 1)
            return keys[^1].Value;

        float t = SegmentFraction(keys[i].Time, keys[i + 1].Time, time);

        // Shortest path: q and -q are the same orientation. Slerp does this
        // itself today, but that is not documented, so the flip stays.
        Quaternion a = keys[i].Value;
        Quaternion b = keys[i + 1].Value;
        if (Quaternion.Dot(a, b) < 0f)
            b = -b;

        return Quaternion.Normalize(Quaternion.Slerp(a, b, t));
    }

    // Index of the key at or before time, or -1 before the first key.
    // Duplicated per key type: a shared version would need a delegate call per
    // search step, and this runs per bone per frame.
    private static int FindSegment(Vector3Key[] keys, float time)
    {
        if (time < keys[0].Time)
            return -1;

        int low = 0;
        int high = keys.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (keys[mid].Time <= time) low = mid;
            else high = mid - 1;
        }

        return low;
    }

    private static int FindSegment(QuaternionKey[] keys, float time)
    {
        if (time < keys[0].Time)
            return -1;

        int low = 0;
        int high = keys.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (keys[mid].Time <= time) low = mid;
            else high = mid - 1;
        }

        return low;
    }

    // Authoring tools do emit coincident key times; unguarded that is a NaN pose.
    private static float SegmentFraction(float start, float end, float time)
    {
        float span = end - start;
        return span <= 1e-9f ? 0f : Math.Clamp((time - start) / span, 0f, 1f);
    }
}

/// <summary>
/// One animation: a duration, a loop flag, and the channels that drive bones.
/// Immutable, so one clip is shared by every character playing it.
/// </summary>
public sealed class AnimationClip
{
    private readonly AnimationChannel[] _channels;

    public AnimationClip(string name, float duration, IReadOnlyList<AnimationChannel> channels, bool looping = true)
    {
        ArgumentNullException.ThrowIfNull(channels);

        if (!float.IsFinite(duration) || duration < 0f)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration must be finite and non-negative.");

        Name = name ?? string.Empty;
        Duration = duration;
        Looping = looping;

        _channels = new AnimationChannel[channels.Count];
        for (int i = 0; i < channels.Count; i++)
            _channels[i] = channels[i] ?? throw new ArgumentException($"Channel {i} is null.", nameof(channels));
    }

    public string Name { get; }

    /// <summary>Length in seconds. Exporter ticks are converted at import.</summary>
    public float Duration { get; }

    public bool Looping { get; }

    public ReadOnlySpan<AnimationChannel> Channels => _channels;

    /// <summary>
    /// Maps a play head onto the clip: wrapped when looping, clamped when not.
    /// Negative times wrap too.
    /// </summary>
    public float NormalizeTime(float time)
    {
        if (Duration <= 0f)
            return 0f;

        if (!Looping)
            return Math.Clamp(time, 0f, Duration);

        float wrapped = time % Duration;
        return wrapped < 0f ? wrapped + Duration : wrapped;
    }
}
