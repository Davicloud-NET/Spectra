using System;
using System.Numerics;
using SpectraEngine.Core.Audio.Acoustics;
using SpectraEngine.Core.Scene;

namespace SpectraEngine.Core.Audio.Propagation;

// Traces one sound's lines to the listener's head and makes one answer of
// them: what the walls leave of the sound, and of its high end.
// The lines' gains are averaged, not their decibels: an opening that half
// the lines pass lets half the sound through.
internal sealed class SoundLines(
    ISoundObstacles world, IAcousticMaterials materials, WallPropagationSettings settings)
{
    // More solids than this on one line are past the most walls can take.
    private const int MaxSpans = 16;

    private readonly HeadLines _ends = new(settings.Lines, settings.HeadRadius);
    private readonly SolidSpan[] _spans = new SolidSpan[MaxSpans];

    // How many lines a sound has in full.
    public int Count => _ends.Count;

    // Traces the first so many of a sound's lines. The first is the one to
    // the listener.
    public AcousticGains Through(in SoundQuery sound, Vector3 listener, int lines)
    {
        HeadLines.Ends ends = _ends.From(sound.Position, listener);

        AcousticGains toListener = Along(in sound, ends[0], out bool listenerInSolid);
        float gain = toListener.Gain;
        float high = toListener.Gain * toListener.GainHf;

        for (int line = 1; line < lines; line++)
        {
            AcousticGains through = Along(in sound, ends[line], out bool endInSolid);

            // A point of the ring inside a wall the listener is not in is no
            // place to listen from. Its line counts as the listener's own.
            if (endInSolid && !listenerInSolid)
                through = toListener;

            gain += through.Gain;
            high += through.Gain * through.GainHf;
        }

        // The high end is what the lines leave at 5 kHz over what they leave
        // in all, so the open lines carry it, as an opening does.
        return new AcousticGains(gain / lines, MathF.Min(high / gain, 1f));
    }

    // What the solids on the line from a sound to one end leave of it.
    private AcousticGains Along(in SoundQuery sound, Vector3 end, out bool endInSolid)
    {
        Vector3 from = world.HeardFrom(sound.Position, end, sound.Body);
        float length = Vector3.Distance(from, end);

        // A list that ran out of room still counts for what it holds.
        int count = world.Trace(from, end, sound.Body, _spans, out _);
        ReadOnlySpan<SolidSpan> solids = _spans.AsSpan(0, count);

        endInSolid = WallLoss.EndsInSolid(solids, length);
        return WallLoss.Sum(solids, length, materials).ToGains();
    }
}
