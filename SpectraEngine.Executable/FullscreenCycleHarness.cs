using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Windowing;
using System;
using System.Globalization;
using System.Threading;

namespace SpectraEngine.Executable;

// Opt-in smoke gate: toggles windowed <-> borderless fullscreen on a timer.
// It only requests through the latch. Engine.Run reshapes the window on the
// window thread while the render thread keeps presenting and resizing the swap
// chain, which is the overlap F11 produces and the one being tested.
internal sealed class FullscreenCycleHarness : IDisposable
{
    // Rendering time before the first toggle, so startup has settled.
    public const double SettleSeconds = 3.0;

    public const double DefaultIntervalSeconds = 2.0;

    private readonly ILogger _logger;
    private readonly IWindowModeLatch _windowMode;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Thread _thread;

    public FullscreenCycleHarness(ILogger logger, IWindowModeLatch windowMode, TimeSpan interval)
    {
        _logger = logger;
        _windowMode = windowMode;
        _interval = interval;

        _thread = new Thread(Run) { Name = "Spectra Fullscreen Cycle", IsBackground = true };
        _thread.Start();
    }

    private void Run()
    {
        CancellationToken token = _cancellation.Token;
        if (token.WaitHandle.WaitOne(TimeSpan.FromSeconds(SettleSeconds)))
            return;

        for (int index = 1; !token.IsCancellationRequested; index++)
        {
            // The applied mode, not the requested one.
            WindowMode from = _windowMode.WindowMode;
            _windowMode.ToggleFullscreen();
            _logger.LogInformation(
                "Fullscreen cycle: toggle #{Index} requested ({From} -> {To})",
                index, from, from == WindowMode.Windowed ? WindowMode.BorderlessFullscreen : WindowMode.Windowed);

            if (token.WaitHandle.WaitOne(_interval))
                return;
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _thread.Join(TimeSpan.FromSeconds(1));
        _cancellation.Dispose();
    }

    public static string DescribeStartup(TimeSpan interval) => string.Format(
        CultureInfo.InvariantCulture,
        "Fullscreen cycle ENABLED: after {0:0.#} s the window toggles windowed <-> borderless fullscreen every " +
        "{1:0.#} s, driven off the render thread, while rendering continues. Gate instrumentation for the " +
        "DXGI resize path — drop --fullscreen-cycle for a window that stays where you put it.",
        SettleSeconds, interval.TotalSeconds);
}
