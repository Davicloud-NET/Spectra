using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SpectraEngine.Editor.Shell;

// Paints the OS title bar to match the window through DWM attributes.
// Not a custom title bar: that costs Aero Snap and correct maximised insets,
// and needs hit-testing the native viewport child would swallow.
// Needs Windows 11 build 22000; older builds fail the HRESULT, which is ignored.
internal static partial class DarkCaption
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int DwmwaBorderColor = 34;

    // COLORREF is 0x00BBGGRR, byte-reversed from the theme's hex colours.
    private static uint ColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

    // Call for every top-level window, dialogs included, or they keep the
    // system accent caption.
    internal static void Apply(Window window) => Apply(window, logger: null);

    internal static void Apply(Window window, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (window.TryGetPlatformHandle() is not { } handle || handle.Handle == 0)
            return;

        try
        {
            int dark = 1;
            Set(handle.Handle, DwmwaUseImmersiveDarkMode, ref dark);

            // Read from the token dictionary so a palette change reaches the
            // title bar; DWM takes a COLORREF, not a brush.
            uint caption = ColorRef(Token("SpectraBgPanelColor", 0x24, 0x20, 0x21));
            uint text = ColorRef(Token("SpectraTextBodyColor", 0xC2, 0xBB, 0xBD));
            uint border = ColorRef(Token("SpectraBorderStrongColor", 0x3F, 0x39, 0x3B));

            Set(handle.Handle, DwmwaCaptionColor, ref caption);
            Set(handle.Handle, DwmwaTextColor, ref text);
            Set(handle.Handle, DwmwaBorderColor, ref border);
        }
        catch (DllNotFoundException ex)
        {
            logger?.LogDebug(ex, "dwmapi is unavailable; the title bar keeps the system colour");
        }
        catch (EntryPointNotFoundException ex)
        {
            logger?.LogDebug(ex, "DwmSetWindowAttribute is unavailable; the title bar keeps the system colour");
        }
    }

    // The fallback only matters if the key is missing.
    private static Color Token(string key, byte r, byte g, byte b)
        => Application.Current?.TryFindResource(key, out object? value) == true && value is Color c
            ? c
            : Color.FromRgb(r, g, b);

    [SupportedOSPlatform("windows")]
    private static void Set<T>(nint hwnd, int attribute, ref T value) where T : unmanaged
    {
        unsafe
        {
            fixed (T* p = &value)
                _ = DwmSetWindowAttribute(hwnd, attribute, p, sizeof(T));
        }
    }

    [LibraryImport("dwmapi.dll")]
    [SupportedOSPlatform("windows")]
    private static unsafe partial int DwmSetWindowAttribute(nint hwnd, int attribute, void* value, int size);
}
