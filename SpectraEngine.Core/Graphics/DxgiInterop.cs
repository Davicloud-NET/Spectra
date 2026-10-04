using Microsoft.Extensions.Logging;
using Silk.NET.DXGI;

namespace SpectraEngine.Core.Graphics;

// DXGI constants Silk.NET does not name (DXGI_MWA_*, DXGI_ERROR_*), shared by
// both D3D backends.
internal static unsafe class DxgiInterop
{
    internal const uint MwaNoWindowChanges = 1u << 0;

    internal const uint MwaNoAltEnter = 1u << 1;

    internal const uint MwaNoPrintScreen = 1u << 2;

    internal const int ErrorInvalidCall = unchecked((int)0x887A0001);

    internal const int ErrorDeviceRemoved = unchecked((int)0x887A0005);

    internal const int ErrorDeviceHung = unchecked((int)0x887A0006);

    internal const int ErrorDeviceReset = unchecked((int)0x887A0007);

    internal const int ErrorDriverInternalError = unchecked((int)0x887A0020);

    internal const int ErrorAccessLost = unchecked((int)0x887A0026);

    // D3DDDIERR_DEVICEREMOVED
    internal const int DdiErrorDeviceRemoved = unchecked((int)0x88760870);

    // Device gone, as opposed to a bad call. A loss ends the run; anything
    // else keeps the previous state and carries on.
    internal static bool IsDeviceLost(int hr) =>
        hr is ErrorDeviceRemoved or ErrorDeviceReset or ErrorDeviceHung
           or ErrorDriverInternalError or ErrorAccessLost or DdiErrorDeviceRemoved;

    internal static string Describe(int hr) => hr switch
    {
        0 => "S_OK",
        ErrorInvalidCall => "DXGI_ERROR_INVALID_CALL",
        ErrorDeviceRemoved => "DXGI_ERROR_DEVICE_REMOVED",
        ErrorDeviceHung => "DXGI_ERROR_DEVICE_HUNG",
        ErrorDeviceReset => "DXGI_ERROR_DEVICE_RESET",
        ErrorDriverInternalError => "DXGI_ERROR_DRIVER_INTERNAL_ERROR",
        ErrorAccessLost => "DXGI_ERROR_ACCESS_LOST",
        DdiErrorDeviceRemoved => "D3DDDIERR_DEVICEREMOVED",
        unchecked((int)0x80070057) => "E_INVALIDARG",
        unchecked((int)0x8007000E) => "E_OUTOFMEMORY",
        _ => "unknown HRESULT",
    };

    // Stops DXGI handling Alt+Enter. Its own SetFullscreenState runs in the
    // window procedure and races the render thread's ResizeBuffers.
    // `factory` must be the one that created the swap chain: the association
    // is per factory and does nothing on another.
    internal static void SuppressAltEnter(IDXGIFactory2* factory, nint hwnd, ILogger logger, string backend)
    {
        int hr = factory->MakeWindowAssociation(hwnd, MwaNoAltEnter);
        if (hr < 0)
        {
            logger.LogWarning(
                "{Backend}: MakeWindowAssociation(DXGI_MWA_NO_ALT_ENTER) failed ({Code}, 0x{Hr:X8}). DXGI may still " +
                "drive its own fullscreen transition on Alt+Enter, which races the render thread's ResizeBuffers.",
                backend, Describe(hr), hr);
            return;
        }

        logger.LogDebug("{Backend}: Alt+Enter taken from DXGI; fullscreen is the engine's (F11, borderless).", backend);
    }
}
