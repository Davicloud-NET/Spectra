using System;

namespace SpectraEngine.Core.Audio;

/// <summary>A contiguous run of source frames, in sample frames.</summary>
public readonly record struct AudioSegment(long Offset, long Count);

/// <summary>
/// A play position in sample frames that plans each buffer fill as runs of
/// source frames, wrapping at the loop end.
/// </summary>
// AL_LOOPING repeats a whole buffer, so it cannot loop a region with an intro.
// Looping sounds go through a buffer queue and this decides what each buffer holds.
// A fill can hold several runs: a loop may be shorter than one buffer.
public struct AudioLoopCursor
{
    private readonly long _totalFrames;
    private readonly LoopRegion _loop;
    private long _position;
    private bool _exhausted;

    public AudioLoopCursor(long totalFrames, LoopRegion loop)
    {
        if (totalFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(totalFrames), totalFrames, "A sound cannot have negative length.");
        if (loop.IsLooping && loop.EndFrame > totalFrames)
            throw new ArgumentOutOfRangeException(nameof(loop), loop, "A loop cannot end past the sound.");

        _totalFrames = totalFrames;
        _loop = loop;
        _position = 0;
        _exhausted = totalFrames == 0;
    }

    /// <summary>Frame the next fill reads from.</summary>
    public readonly long Position => _position;

    /// <summary>Total decoded frames in the sound.</summary>
    public readonly long TotalFrames => _totalFrames;

    /// <summary>The region being repeated, or <see cref="LoopRegion.None"/>.</summary>
    public readonly LoopRegion Loop => _loop;

    /// <summary>
    /// True once the last frame has been planned. A looping sound gets here
    /// only after a <see cref="Seek"/> past its loop end.
    /// </summary>
    public readonly bool IsExhausted => _exhausted;

    /// <summary>
    /// Plans the next fill as runs written into <paramref name="segments"/>
    /// and advances past them. Returns the number of runs written.
    /// </summary>
    /// <param name="segments">Caller-owned scratch. Planning stops when it is full.</param>
    /// <param name="plannedFrames">Frames covered, which can be fewer than requested.</param>
    public int Plan(Span<AudioSegment> segments, long requestedFrames, out long plannedFrames)
    {
        if (requestedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(requestedFrames), requestedFrames, "Cannot plan a negative fill.");

        plannedFrames = 0;
        int count = 0;
        long remaining = requestedFrames;

        while (remaining > 0 && count < segments.Length && !_exhausted)
        {
            // Past the loop end (a seek into the tail): play out to the end.
            long limit = _loop.IsLooping && _position < _loop.EndFrame ? _loop.EndFrame : _totalFrames;
            long available = limit - _position;
            if (available <= 0)
            {
                _exhausted = true;
                break;
            }

            long take = Math.Min(remaining, available);
            segments[count++] = new AudioSegment(_position, take);
            _position += take;
            plannedFrames += take;
            remaining -= take;

            // Wrap only when the loop end bounded this run, or a seek into
            // the tail would jump back instead of playing the outro.
            if (limit == _loop.EndFrame && _loop.IsLooping && _position >= _loop.EndFrame)
                _position = _loop.StartFrame;
            else if (_position >= _totalFrames)
                _exhausted = true;
        }

        return count;
    }

    /// <summary>
    /// Moves the play position, clamped to the end of the sound. A seek past
    /// the loop end plays the tail and finishes. A negative frame throws.
    /// </summary>
    public void Seek(long frame)
    {
        if (frame < 0)
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "Cannot seek before the start of a sound.");

        _position = Math.Min(frame, _totalFrames);
        _exhausted = _position >= _totalFrames;
    }

    /// <summary>Returns the cursor to frame 0.</summary>
    public void Rewind()
    {
        _position = 0;
        _exhausted = _totalFrames == 0;
    }
}
