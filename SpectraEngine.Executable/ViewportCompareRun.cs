using Microsoft.Extensions.Logging;
using SpectraEngine.Core;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Graphics;
using System;
using System.Diagnostics;
using System.Threading;

namespace SpectraEngine.Executable;

// Runs the engine on a windowless composited surface until ViewportCompareProbe
// reports. No window: the shared present target only exists on a composited
// surface, and a window would give the frame a swap chain instead.
internal static class ViewportCompareRun
{
    // Viewport-sized, so the comparison covers a whole real frame.
    private const int Width = 1280;

    private const int Height = 720;

    // The engine ends the run itself; this only guards against a hang.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    // True when the two pictures agreed. The verdict is also written to disk,
    // because the editor shell reads it from another process.
    internal static bool Run(Engine engine, Renderer renderer, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(logger);

        engine.RunViewportCompare = true;

        var surface = new CompositedProbeSurface(Width, Height);
        engine.Start(surface);
        try
        {
            var waited = Stopwatch.StartNew();
            while (!engine.Host.ShutdownRequested && waited.Elapsed < Timeout)
                Thread.Sleep(10);

            if (!engine.Host.ShutdownRequested)
            {
                logger.LogError(
                    "Viewport compare: FAIL - the probe did not report within {Seconds:0} s. The render " +
                    "thread is still running; see the log above for where it stopped.",
                    Timeout.TotalSeconds);
            }
        }
        finally
        {
            // Blocks until the render thread, which owns every GPU resource, is done.
            engine.Stop();
        }

        bool passed = engine.ViewportComparePassed == true && !engine.Faulted;

        // Record a failure too, or a broken machine keeps its last green stamp.
        RecordVerdict(renderer, passed, logger);
        return passed;
    }

    private static void RecordVerdict(Renderer renderer, bool passed, ILogger logger)
    {
        var stamp = new ViewportCompareStamp(
            renderer.AdapterName, renderer.Backend, passed, DateTime.UtcNow);

        if (stamp.Save())
        {
            logger.LogInformation(
                "Viewport compare: verdict recorded for {Adapter} on {Backend} at {Path}.",
                stamp.Adapter, stamp.Backend, ViewportCompareStamp.DefaultPath);
        }
        else
        {
            logger.LogWarning(
                "Viewport compare: the verdict could not be written to {Path}; the editor shell will see " +
                "no colour measurement for this machine.",
                ViewportCompareStamp.DefaultPath);
        }
    }
}
