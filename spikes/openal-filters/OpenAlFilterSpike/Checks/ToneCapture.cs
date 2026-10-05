using System;

namespace OpenAlFilterSpike.Checks;

internal static class ToneCapture
{
    internal const double Level = 0.5;

    /// <summary>
    /// Plays one tone, lets it settle, makes the change between two render
    /// calls and returns one window from each side of it.
    /// </summary>
    /// <param name="startGainHf">Null starts with no filter on the source.</param>
    // The change falls after a whole number of cycles of any tone that is a
    // multiple of 10 Hz. Phase 0 puts it on a zero crossing, a quarter turn
    // puts it on the peak.
    internal static void AcrossChange(
        OpenAl lib, Tone tone, float? startGainHf, Change change, int callSize,
        out float[] before, out float[] after)
    {
        using Rig rig = Rig.Open(lib);
        uint source = Start(rig, tone, startGainHf, out uint filter);
        before = rig.Render(Signal.Window);

        change(rig, source, filter);

        after = new float[Signal.Window];
        for (int done = 0; done < after.Length; done += callSize)
            rig.Render(after.AsSpan(done, Math.Min(callSize, after.Length - done)));
    }

    /// <summary>A playing tone, two windows in, with a low-pass filter object ready.</summary>
    internal static uint Start(Rig rig, Tone tone, float? startGainHf, out uint filter)
    {
        uint buffer = rig.CreateBuffer(2.0, tone);
        uint source = rig.CreateSource(buffer);
        filter = rig.Efx.CreateLowpass();
        if (startGainHf is not null)
            rig.Attach(source, filter, 1f, startGainHf.Value);
        rig.Al.SourcePlay(source);
        rig.Render(2 * Signal.Window);
        return source;
    }
}
