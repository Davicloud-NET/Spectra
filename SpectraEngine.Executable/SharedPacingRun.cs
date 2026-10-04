using Microsoft.Extensions.Logging;
using SpectraEngine.Core;
using SpectraEngine.Core.Graphics;
using System;
using System.Diagnostics;
using System.Threading;

namespace SpectraEngine.Executable;

// Runs the engine on a windowless composited surface until SharedPacingProbe
// has printed its table. The shared hand-over only exists on such a surface.
internal static class SharedPacingRun
{
    // Viewport-sized: the consumer's turn copies the whole texture.
    private const int Width = 1280;

    private const int Height = 720;

    // The engine ends the run itself; this only guards against a hang.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    // True when a measurement was produced.
    internal static bool Run(Engine engine, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(logger);

        engine.RunSharedPacingProbe = true;

        var surface = new CompositedProbeSurface(Width, Height);
        engine.Start(surface);
        try
        {
            // Faulted too: a render thread that crashed before its first frame
            // never requests a shutdown.
            var waited = Stopwatch.StartNew();
            while (!engine.Host.ShutdownRequested && !engine.Faulted && waited.Elapsed < Timeout)
                Thread.Sleep(10);

            if (!engine.Host.ShutdownRequested && !engine.Faulted)
            {
                logger.LogError(
                    "Shared pacing probe: FAIL - the probe did not report within {Seconds:0} s. The render " +
                    "thread is still running; see the log above for where it stopped.",
                    Timeout.TotalSeconds);
            }
        }
        finally
        {
            // Blocks until the render thread, which owns every GPU resource, is done.
            engine.Stop();
        }

        return engine.SharedPacingProbePassed == true && !engine.Faulted;
    }
}
