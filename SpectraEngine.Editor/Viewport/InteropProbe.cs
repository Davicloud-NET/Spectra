using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Viewport;

// Reports what this machine's compositor accepts from the engine: adapter,
// importable shared-texture handle kinds, and how each can be synchronised.
// Capability flags are not proof (Avalonia's Windows interop is ANGLE over
// D3D11), so it also imports four real textures: D3D11 NT handle, D3D11 global
// handle, D3D12 NT handle, D3D11On12 NT handle. Each route catches its own
// failure so the rest still run.
internal static class InteropProbe
{
    // Runs the probe instead of opening the editor.
    public const string Switch = "--interop-probe";

    public static bool Requested(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));

    // Needs a real window: the compositor and its GPU interop only exist once
    // a top level does.
    public static async Task RunAsync(Window window, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            Compositor? compositor = ElementComposition.GetElementVisual(window)?.Compositor;

            if (compositor is null)
            {
                logger.LogWarning(
                    "Interop probe: the window has no compositor. The composited viewport is not " +
                    "available on this platform and the native child is the only path.");
                return;
            }

            ICompositionGpuInterop? interop = await compositor.TryGetCompositionGpuInterop();

            if (interop is null)
            {
                logger.LogWarning(
                    "Interop probe: this compositor exposes no GPU interop. The composited viewport " +
                    "cannot be built here; the native child stays.");
                return;
            }

            logger.LogInformation("Interop probe:\n{Report}", Describe(interop));

            logger.LogInformation(
                "Interop probe routes:\n{Report}", await RunRoutesAsync(compositor, interop, logger));
        }
        catch (Exception ex)
        {
            // The driver is the unknown here, so the probe must not crash the shell.
            logger.LogError(ex, "Interop probe: the query itself failed");
        }
    }

    private static string Describe(ICompositionGpuInterop interop)
    {
        var sb = new StringBuilder();

        sb.Append("  adapter LUID  ").Append(Format(interop.DeviceLuid)).Append('\n');
        sb.Append("  adapter UUID  ").Append(Format(interop.DeviceUuid)).Append('\n');

        IReadOnlyList<string> kinds = interop.SupportedImageHandleTypes;

        if (kinds.Count == 0)
        {
            sb.Append("  image handles (none) - nothing can be imported, so the composited\n");
            sb.Append("                 viewport is not available on this machine.\n");
            return sb.ToString();
        }

        sb.Append("  image handles ").Append(kinds.Count).Append('\n');

        foreach (string kind in kinds)
        {
            sb.Append("    ").Append(kind.PadRight(38));

            try
            {
                // Per handle kind, not per device: a machine can accept a
                // handle it cannot synchronise with a keyed mutex.
                sb.Append(interop.GetSynchronizationCapabilities(kind));
            }
            catch (Exception ex)
            {
                sb.Append("query failed: ").Append(ex.GetType().Name);
            }

            sb.Append('\n');
        }

        sb.Append("  verdict       ").Append(Verdict(kinds)).Append('\n');
        return sb.ToString();
    }

    private static string Verdict(IReadOnlyList<string> kinds)
    {
        bool nt = kinds.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle);
        bool global = kinds.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle);

        return (nt, global) switch
        {
            (true, _) =>
                "D3D11 NT handles import. The D3D11 backend can hand its resolve target straight " +
                "over; whether a D3D12-created handle is accepted is the remaining question, and " +
                "the route measurements below are what answer it.",

            (false, true) =>
                "Only the legacy global shared handle imports. Workable for D3D11, and it carries " +
                "no keyed mutex, so the hand-over needs a fence or a flush per frame.",

            _ =>
                "No D3D11 handle kind is accepted. Either this compositor is not on D3D at all " +
                "(a Vulkan or software backend), or the composited viewport wants a different " +
                "image type entirely - read the list above rather than assuming.",
        };
    }

    private static string Format(byte[]? bytes) =>
        bytes is null or { Length: 0 }
            ? "(none)"
            : string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    private const string Route1 = "1  D3D11 texture, NT handle";
    private const string Route2 = "2  D3D11 texture, global shared handle";
    private const string Route3 = "3  D3D12 resource, NT handle";
    private const string Route4 = "4  D3D11On12 texture, NT handle";

    // KeyedMutex is about the texture this side created, not the compositor.
    // A D3D12 resource has none, which explains an E_NOINTERFACE on import.
    private sealed record RouteResult(
        string Route,
        bool Imported,
        bool Updated,
        string Synchronization,
        bool KeyedMutex,
        string? Failure);

    private static async Task<string> RunRoutesAsync(
        Compositor compositor, ICompositionGpuInterop interop, ILogger logger)
    {
        InteropProbeTextures textures;
        try
        {
            textures = new InteropProbeTextures(interop.DeviceLuid, logger);
        }
        catch (Exception ex)
        {
            return "  no route could be measured: the probe could not open a graphics device.\n" +
                   $"  {Explain(ex)}\n";
        }

        var results = new List<RouteResult>(4);
        try
        {
            results.Add(await RunRouteAsync(
                compositor, interop, Route1, () => textures.CreateD3D11NtHandleTexture()));
            results.Add(await RunRouteAsync(compositor, interop, Route2, textures.CreateD3D11GlobalHandleTexture));
            results.Add(await RunRouteAsync(compositor, interop, Route3, textures.CreateD3D12Texture));
            results.Add(await RunRouteAsync(compositor, interop, Route4, textures.CreateD3D11On12Texture));

            return DescribeRoutes(textures.AdapterName, results);
        }
        finally
        {
            textures.Dispose();
        }
    }

    private static async Task<RouteResult> RunRouteAsync(
        Compositor compositor,
        ICompositionGpuInterop interop,
        string route,
        Func<SharedProbeTexture> create)
    {
        SharedProbeTexture? texture = null;
        ICompositionImportedGpuImage? image = null;
        CompositionDrawingSurface? surface = null;
        bool imported = false;
        bool updated = false;
        bool keyedMutex = false;
        string sync = "(not reached)";

        try
        {
            texture = create();
            keyedMutex = texture.KeyedMutex;
            sync = SynchronizationOf(interop, texture.HandleKind);

            image = interop.ImportImage(
                new PlatformHandle(texture.Handle, texture.HandleKind),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = texture.Width,
                    Height = texture.Height,
                    Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,

                    // D3D render targets are top-left origin. Wrong here flips
                    // the picture without failing the import.
                    TopLeftOrigin = true,
                });

            await Bounded(image.ImportCompleted, "the import");
            imported = true;

            surface = compositor.CreateDrawingSurface();
            await Bounded(
                surface.UpdateWithKeyedMutexAsync(image, texture.AcquireKey, texture.ReleaseKey),
                "the keyed-mutex hand-over");
            updated = true;

            return new RouteResult(route, imported, updated, sync, keyedMutex, null);
        }
        catch (Exception ex)
        {
            return new RouteResult(route, imported, updated, sync, keyedMutex, Explain(ex));
        }
        finally
        {
            // Image first: it is the compositor's view of the texture, and
            // freeing the texture under it crashes.
            if (image is not null)
            {
                try { await image.DisposeAsync(); }
                catch (Exception) { /* a lost device cannot be released cleanly either */ }
            }

            surface?.Dispose();
            texture?.Dispose();
        }
    }

    // A keyed-mutex acquire on a resource with no keyed mutex never times out
    // on its own. The abandoned task is left running; the API cannot cancel it.
    private static async Task Bounded(Task task, string what)
    {
        Task first = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (first != task)
            throw new TimeoutException($"{what} did not complete within 10 s");

        await task;
    }

    private static string SynchronizationOf(ICompositionGpuInterop interop, string kind)
    {
        try
        {
            return interop.GetSynchronizationCapabilities(kind).ToString();
        }
        catch (Exception ex)
        {
            return $"query failed: {ex.GetType().Name}";
        }
    }

    private static string DescribeRoutes(string adapter, IReadOnlyList<RouteResult> results)
    {
        var sb = new StringBuilder();
        sb.Append("  adapter       ").Append(adapter).Append('\n');

        foreach (RouteResult r in results)
        {
            sb.Append("  ").Append(r.Route.PadRight(40));
            sb.Append(r.Imported ? "import ok   " : "import NO   ");
            sb.Append(r.Updated ? "update ok   " : "update NO   ");
            sb.Append("compositor takes ").Append(r.Synchronization);
            sb.Append(r.KeyedMutex ? ", texture has one" : ", texture has none").Append('\n');

            if (r.Failure is { } failure)
                sb.Append("      ").Append(failure).Append('\n');
        }

        sb.Append("  verdict       ").Append(RouteVerdict(results)).Append('\n');
        return sb.ToString();
    }

    private static string RouteVerdict(IReadOnlyList<RouteResult> results)
    {
        bool direct = Succeeded(results, Route3);
        bool bridged = Succeeded(results, Route4);
        bool d3d11 = Succeeded(results, Route1) || Succeeded(results, Route2);

        if (direct)
        {
            return "route 3 is viable: a D3D12-created handle imports AND hands over here, so the " +
                   "composited viewport can take the D3D12 backend's texture directly, with no bridge.";
        }

        if (bridged)
        {
            return "route 4 is the viable D3D12 route: the D3D12 handle itself does not complete a " +
                   "hand-over, but a D3D11On12 device over the same D3D12 device does, so the " +
                   "composited viewport costs one copy per frame on that backend.";
        }

        if (d3d11)
        {
            return "no D3D12 route works here: only a native D3D11 device's texture completes a " +
                   "hand-over, so a composited viewport would be D3D11-only and D3D12 keeps the " +
                   "native child.";
        }

        return "no route completed a hand-over. Nothing on this machine can be composited yet; the " +
               "native child stays, and the failure text above is the reason rather than a guess.";
    }

    private static bool Succeeded(IReadOnlyList<RouteResult> results, string route) =>
        results.Any(r => r.Route == route && r.Updated);

    // Most failures here are only meaningful by their HRESULT.
    private static string Explain(Exception ex)
    {
        Exception real = ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
            ? aggregate.InnerExceptions[0]
            : ex;

        string code = real.HResult == 0
            ? string.Empty
            : $" (hr=0x{real.HResult:X8})";

        return $"{real.GetType().Name}: {real.Message}{code}";
    }
}
