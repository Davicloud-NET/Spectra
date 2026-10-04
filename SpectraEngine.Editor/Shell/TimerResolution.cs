using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Shell;

// Asks Windows for a 1 ms timer while an engine session is open. The default
// granularity is 15.6 ms, so an 8 ms DispatcherTimer would fire at 15.6.
// Held per session, not per process: a raised interrupt rate costs battery
// and the start page has nothing to pump.
internal static partial class TimerResolution
{
    private const uint Period = 1;

    private static bool _held;

    public static void Acquire(ILogger logger)
    {
        if (!OperatingSystem.IsWindows() || _held)
            return;

        try
        {
            if (TimeBeginPeriod(Period) != 0)
            {
                logger.LogDebug("timeBeginPeriod refused; the shell keeps the default timer granularity");
                return;
            }

            _held = true;

            // Off the UI thread: the measurement sleeps for about 100 ms.
            _ = Task.Run(() => logger.LogInformation(
                "Timer resolution raised; measured granularity {Granularity:F2} ms", Measure()));
        }
        catch (DllNotFoundException ex)
        {
            logger.LogDebug(ex, "winmm is unavailable; the shell keeps the default timer granularity");
        }
        catch (EntryPointNotFoundException ex)
        {
            logger.LogDebug(ex, "timeBeginPeriod is unavailable; the shell keeps the default timer granularity");
        }
    }

    // Safe to call twice.
    public static void Release()
    {
        if (!OperatingSystem.IsWindows() || !_held)
            return;

        _held = false;

        try
        {
            _ = TimeEndPeriod(Period);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // Median, so one scheduling hiccup at startup does not skew it.
    // Sleep(1), because Sleep(0) yields without touching the timer.
    private static double Measure()
    {
        Span<double> gaps = stackalloc double[100];
        long previous = Stopwatch.GetTimestamp();

        for (int i = 0; i < gaps.Length; i++)
        {
            Thread.Sleep(1);
            long now = Stopwatch.GetTimestamp();
            gaps[i] = (now - previous) * 1000.0 / Stopwatch.Frequency;
            previous = now;
        }

        gaps.Sort();
        return gaps[gaps.Length / 2];
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [SupportedOSPlatform("windows")]
    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);
}
