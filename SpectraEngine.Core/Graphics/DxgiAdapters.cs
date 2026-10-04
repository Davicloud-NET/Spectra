using Microsoft.Extensions.Logging;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;
using System;
using DxgiApi = Silk.NET.DXGI.DXGI;

namespace SpectraEngine.Core.Graphics;

// Adapter selection for both D3D backends (--adapter).
internal static unsafe class DxgiAdapters
{
    // Hardware adapter whose description contains `wanted`, or null for the
    // system default. Software adapters are skipped so WARP never matches.
    internal static ComPtr<IDXGIAdapter> Find(DxgiApi dxgi, string? wanted, ILogger logger, out string chosenName)
    {
        chosenName = "system default";
        if (string.IsNullOrWhiteSpace(wanted)) return default;

        IDXGIFactory1* factory = null;
        Guid factoryGuid = IDXGIFactory1.Guid;
        if (dxgi.CreateDXGIFactory1(&factoryGuid, (void**)&factory) < 0)
        {
            logger.LogWarning("Could not enumerate adapters; using the system default.");
            return default;
        }

        var factoryPtr = ComOwnership.Own(factory);
        try
        {
            for (uint index = 0; ; index++)
            {
                IDXGIAdapter1* adapter = null;
                if (((IDXGIFactory1*)factoryPtr.Handle)->EnumAdapters1(index, &adapter) < 0)
                    break;

                var owned = ComOwnership.Own(adapter);
                AdapterDesc1 desc = default;
                ((IDXGIAdapter1*)owned.Handle)->GetDesc1(&desc);

                string name = DescriptionOf(ref desc);
                bool software = (desc.Flags & (uint)AdapterFlag.Software) != 0;

                if (!software && name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    chosenName = name;
                    logger.LogInformation("Graphics adapter: {Adapter} (matched '{Wanted}')", name, wanted);

                    // QueryInterface, not a cast: the caller owns and releases it.
                    IDXGIAdapter* asBase = null;
                    Guid baseGuid = IDXGIAdapter.Guid;
                    if (((IDXGIAdapter1*)owned.Handle)->QueryInterface(&baseGuid, (void**)&asBase) >= 0)
                    {
                        ComOwnership.Release(ref owned);
                        return ComOwnership.Own(asBase);
                    }
                }

                logger.LogDebug("Graphics adapter {Index}: {Adapter}{Software}", index, name, software ? " (software)" : "");
                ComOwnership.Release(ref owned);
            }

            logger.LogWarning(
                "No graphics adapter matched '{Wanted}'; using the system default.", wanted);
            return default;
        }
        finally
        {
            ComOwnership.Release(ref factoryPtr);
        }
    }

    // Description is a fixed 128-char UTF-16 buffer.
    private static string DescriptionOf(ref AdapterDesc1 desc)
    {
        fixed (char* p = desc.Description)
        {
            var span = new ReadOnlySpan<char>(p, 128);
            int end = span.IndexOf('\0');
            return new string(end < 0 ? span : span[..end]);
        }
    }
}
