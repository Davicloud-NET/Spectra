using Avalonia;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Microsoft.Extensions.Logging;
using SpectraEngine.Core.Diagnostics;
using SpectraEngine.Core.Graphics;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace SpectraEngine.Editor.Viewport;

// Measures what this machine can do with a composited viewport. A compositor
// can advertise a handle kind and then refuse to import it, so a real 1-texel
// shared texture is imported before any engine session exists.
// Never throws: every failure means "use the native child". UI thread.
internal static class ViewportProbe
{
    private const int DryRunSize = 1;

    // Nothing else times out an import that cannot be synchronised.
    private static readonly TimeSpan DryRunDeadline = TimeSpan.FromSeconds(10);

    // anchor: any visual already attached to the window's tree.
    internal static async Task<ViewportCapabilities> MeasureAsync(
        Visual anchor, GraphicsBackend backend, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(logger);

        ViewportCapabilities capabilities = ViewportCapabilities.NotMeasured;

        try
        {
            if (ElementComposition.GetElementVisual(anchor)?.Compositor is not { } compositor)
                return capabilities;

            capabilities = capabilities with { HasCompositor = true };

            ICompositionGpuInterop? interop = await compositor.TryGetCompositionGpuInterop();
            if (interop is null)
                return capabilities;

            string kind = KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureNtHandle;
            capabilities = capabilities with
            {
                HasGpuInterop = true,
                AdapterLuid = FormatLuid(interop.DeviceLuid),
                SupportsD3D11NtHandle = interop.SupportedImageHandleTypes.Contains(kind),
            };

            if (!capabilities.SupportsD3D11NtHandle)
                return capabilities;

            // Per handle kind: a machine can accept a handle it cannot
            // synchronise with a keyed mutex.
            capabilities = capabilities with
            {
                SupportsKeyedMutex = HasKeyedMutex(interop, kind, logger),
            };

            if (!capabilities.SupportsKeyedMutex)
                return capabilities;

            return await DryRunAsync(interop, capabilities, backend, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Measuring the composited viewport's machine failed");
            return capabilities;
        }
    }

    private static bool HasKeyedMutex(ICompositionGpuInterop interop, string kind, ILogger logger)
    {
        try
        {
            return interop.GetSynchronizationCapabilities(kind)
                .HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "The compositor could not report its synchronisation capabilities for {Kind}", kind);
            return false;
        }
    }

    private static async Task<ViewportCapabilities> DryRunAsync(
        ICompositionGpuInterop interop,
        ViewportCapabilities capabilities,
        GraphicsBackend backend,
        ILogger logger)
    {
        InteropProbeTextures? textures = null;
        SharedProbeTexture? texture = null;
        ICompositionImportedGpuImage? image = null;

        try
        {
            textures = new InteropProbeTextures(interop.DeviceLuid, logger);
            capabilities = capabilities with
            {
                AdapterName = textures.AdapterName,
                DriverVersion = textures.DriverVersion,

                // Verdict of the last --viewport-compare on this backend. Keyed
                // by backend only: the producer cannot name its adapter.
                CompareGreen = ViewportCompareStamp.IsGreenFor(ViewportCompareStamp.Load(), backend),
            };

            texture = textures.CreateD3D11NtHandleTexture(DryRunSize);
            image = interop.ImportImage(
                new PlatformHandle(texture.Handle, texture.HandleKind),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = texture.Width,
                    Height = texture.Height,
                    Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
                    TopLeftOrigin = true,
                });

            await Bounded(image.ImportCompleted);

            // Import only. A hand-over would add no information.
            logger.LogInformation(
                "Composited viewport rehearsal: a {Size}x{Size} shared texture imported on {Adapter} " +
                "(driver {Driver}).",
                DryRunSize, DryRunSize, textures.AdapterName,
                textures.DriverVersion.Length > 0 ? textures.DriverVersion : "unknown");

            return capabilities with { DryRunImported = true };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The composited viewport's rehearsal import was refused");
            return capabilities;
        }
        finally
        {
            // Image before texture: freeing the texture under the compositor's
            // view of it crashes the driver.
            if (image is not null)
            {
                try
                {
                    await image.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Releasing the rehearsal import failed");
                }
            }

            texture?.Dispose();
            textures?.Dispose();
        }
    }

    private static async Task Bounded(Task task)
    {
        Task first = await Task.WhenAny(task, Task.Delay(DryRunDeadline));
        if (first != task)
            throw new TimeoutException($"the rehearsal import did not complete within {DryRunDeadline}");

        await task;
    }

    // Same spelling as the settings file. A missing LUID stays empty: a
    // placeholder would match itself across machines.
    private static string FormatLuid(byte[]? luid) =>
        luid is null or { Length: 0 }
            ? string.Empty
            : string.Concat(luid.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
}
