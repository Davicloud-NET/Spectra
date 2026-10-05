using System.Numerics;

namespace SpectraEngine.Core.Audio.Captions;

/// <summary>
/// One caption that shows now. It says what there is to read and nothing
/// about how it looks.
/// </summary>
/// <param name="Id">
/// The same number for as long as the caption shows, and a new one when it
/// shows again later. Never zero.
/// </param>
/// <param name="Kind">Speech or another sound.</param>
/// <param name="Text">The words. A line break in them is one the writer chose.</param>
/// <param name="Speaker">Who says it, or null when nobody is named.</param>
/// <param name="Position">
/// Where the sound is in the world, or null for a sound with no place: one
/// that plays at the listener.
/// </param>
/// <param name="Audibility">
/// How well the sound is heard at the listener, from 0 to 1. It is 0 while
/// the caption stays up after its sound has gone.
/// </param>
/// <param name="StartedAt">When it started to show, in seconds on <see cref="CaptionFeed.Now"/>.</param>
/// <param name="EarliestEnd">
/// The earliest it may go, on the same clock. Until then it shows whatever
/// its sound does. It moves later when the sound plays again.
/// </param>
public readonly record struct Caption(
    long Id,
    CaptionKind Kind,
    string Text,
    string? Speaker,
    Vector3? Position,
    float Audibility,
    double StartedAt,
    double EarliestEnd);
