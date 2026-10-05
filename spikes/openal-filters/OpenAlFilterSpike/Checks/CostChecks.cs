using System;
using System.Diagnostics;
using Silk.NET.OpenAL;

namespace OpenAlFilterSpike.Checks;

// Question 8: what a filter change costs.
internal static unsafe class CostChecks
{
    private const int Rounds = 9;
    private const int Calls = 200_000;
    private const int Voices = 32;

    // One frame of sound at 60 frames a second.
    private const int FrameSamples = Signal.Rate / 60;

    internal static void Run(Report report, OpenAl lib)
    {
        using Rig rig = Rig.Open(lib);
        AL al = rig.Al;
        Efx efx = rig.Efx;

        uint buffer = rig.CreateBuffer(ResponseChecks.TestSignal());
        uint[] sources = new uint[Voices];
        for (int i = 0; i < Voices; i++)
        {
            sources[i] = rig.CreateSource(buffer);
            al.SetSourceProperty(sources[i], SourceFloat.Gain, 1f / Voices);
            al.SetSourceProperty(sources[i], SourceBoolean.Looping, true);
            al.SourcePlay(sources[i]);
        }

        float[] frame = new float[FrameSamples];
        rig.Render(frame);
        uint playing = sources[0];
        uint filter = efx.CreateLowpass();

        report.Time("8", "alFilterf", PerCall(() =>
        {
            for (int i = 0; i < Calls; i++)
                efx.Filterf(filter, Efx.LowpassGainHf, (i & 1) == 0 ? 0.3f : 0.7f);
        }));

        report.Time("8", "alSourcei(AL_DIRECT_FILTER), playing", PerCall(() =>
        {
            for (int i = 0; i < Calls; i++)
                efx.Sourcei(playing, Efx.DirectFilter, (int)filter);
        }));

        report.Time("8", "two alFilterf and the attach, playing", PerCall(() =>
        {
            for (int i = 0; i < Calls; i++)
                rig.Attach(playing, filter, 1f, (i & 1) == 0 ? 0.3f : 0.7f);
        }));

        uint idle = rig.CreateSource(buffer);
        report.Time("8", "the same on a source that is not playing", PerCall(() =>
        {
            for (int i = 0; i < Calls; i++)
                rig.Attach(idle, filter, 1f, (i & 1) == 0 ? 0.3f : 0.7f);
        }));

        report.Time("8", "alSourcef(AL_GAIN) through Silk.NET, playing", PerCall(() =>
        {
            for (int i = 0; i < Calls; i++)
                al.SetSourceProperty(playing, SourceFloat.Gain, (i & 1) == 0 ? 0.03f : 0.031f);
        }));
        al.SetSourceProperty(playing, SourceFloat.Gain, 1f / Voices);

        // A frame as the engine would run it: every voice gets new values, then the mixer runs.
        foreach (uint source in sources)
            rig.Detach(source);
        double openMix = BestMicroseconds(() => rig.Render(frame));

        double update = BestMicroseconds(() =>
        {
            for (int i = 0; i < Voices; i++)
                rig.Attach(sources[i], filter, 1f, 0.3f + (i * 0.01f));
        });
        double filteredMix = BestMicroseconds(() => rig.Render(frame));

        double both = BestMicroseconds(() =>
        {
            for (int i = 0; i < Voices; i++)
                rig.Attach(sources[i], filter, 1f, 0.3f + (i * 0.01f));
            rig.Render(frame);
        });

        report.Time("8", "32 voices: set and attach all of them", $"{update:F2} us");
        report.Time("8", "32 voices: mix 800 samples, no filter", $"{openMix:F1} us");
        report.Time("8", "32 voices: mix 800 samples, filtered", $"{filteredMix:F1} us");
        report.Time("8", "32 voices: change all, then mix", $"{both:F1} us");
    }

    private static string PerCall(Action body)
    {
        long best = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long start = Stopwatch.GetTimestamp();
            body();
            best = Math.Min(best, Stopwatch.GetTimestamp() - start);
        }

        return $"{best * 1e9 / Stopwatch.Frequency / Calls:F1} ns";
    }

    private static double BestMicroseconds(Action body)
    {
        long best = long.MaxValue;
        for (int round = 0; round < 200; round++)
        {
            long start = Stopwatch.GetTimestamp();
            body();
            best = Math.Min(best, Stopwatch.GetTimestamp() - start);
        }

        return best * 1e6 / Stopwatch.Frequency;
    }
}
