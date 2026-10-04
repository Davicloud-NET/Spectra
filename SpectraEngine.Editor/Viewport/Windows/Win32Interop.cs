using System;
using System.Runtime.InteropServices;

namespace SpectraEngine.Editor.Viewport.Windows;

// Only what Win32ViewportWindow and the composited viewport call.
internal static partial class Win32Interop
{
    internal const int WS_CHILD = 0x40000000;
    internal const int WS_VISIBLE = 0x10000000;
    internal const int WS_CLIPSIBLINGS = 0x04000000;
    internal const int WS_CLIPCHILDREN = 0x02000000;

    // CS_OWNDC: an OpenGL context needs the window's own DC. H/VREDRAW: no
    // stale strip along the edge during a resize.
    internal const int CS_OWNDC = 0x0020;
    internal const int CS_HREDRAW = 0x0002;
    internal const int CS_VREDRAW = 0x0001;

    internal const int WM_DESTROY = 0x0002;
    internal const int WM_SIZE = 0x0005;
    internal const int WM_SETFOCUS = 0x0007;
    internal const int WM_KILLFOCUS = 0x0008;
    internal const int WM_ERASEBKGND = 0x0014;
    internal const int WM_SETCURSOR = 0x0020;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const int WM_MOUSEMOVE = 0x0200;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MBUTTONUP = 0x0208;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_MOUSEHWHEEL = 0x020E;
    internal const int WM_CAPTURECHANGED = 0x0215;

    internal const int WHEEL_DELTA = 120;
    internal const int HTCLIENT = 1;

    internal const uint MAPVK_VSC_TO_VK_EX = 3;

    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_LSHIFT = 0xA0;
    internal const int VK_RSHIFT = 0xA1;

    // Windows has no grab or rotate cursor: IDC_HAND and IDC_SIZEALL stand in.
    internal const int IDC_ARROW = 32512;
    internal const int IDC_CROSS = 32515;
    internal const int IDC_SIZENWSE = 32642;
    internal const int IDC_SIZENESW = 32643;
    internal const int IDC_SIZEWE = 32644;
    internal const int IDC_SIZENS = 32645;
    internal const int IDC_SIZEALL = 32646;
    internal const int IDC_NO = 32648;
    internal const int IDC_HAND = 32649;

    internal delegate nint WndProc(nint hwnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEX
    {
        internal uint cbSize;
        internal uint style;
        internal nint lpfnWndProc;
        internal int cbClsExtra;
        internal int cbWndExtra;
        internal nint hInstance;
        internal nint hIcon;
        internal nint hCursor;
        internal nint hbrBackground;
        internal nint lpszMenuName;
        internal nint lpszClassName;
        internal nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    internal static partial ushort RegisterClassEx(ref WNDCLASSEX windowClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(
        int exStyle,
        string className,
        string? windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    // Not GetAsyncKeyState: that reads the keyboard as of now, not as of the
    // message, and loses a quickly released modifier.
    [LibraryImport("user32.dll", EntryPoint = "GetKeyState")]
    internal static partial short GetKeyState(int virtualKey);

    internal static bool IsKeyDown(int virtualKey) => (GetKeyState(virtualKey) & 0x8000) != 0;

    [LibraryImport("user32.dll", EntryPoint = "SetFocus")]
    internal static partial nint SetFocus(nint hwnd);

    // How far a press may travel and still be a click.
    internal const int SM_CXDRAG = 68;

    // Windows 10 1607 and later.
    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetricsForDpi")]
    internal static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll", EntryPoint = "GetDpiForWindow")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "SetCapture")]
    internal static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "ClientToScreen")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll", EntryPoint = "SetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetCursorPos(int x, int y);

    // Two entry points: the release form takes NULL, which `in RECT` cannot express.
    [LibraryImport("user32.dll", EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursor(in RECT rect);

    [LibraryImport("user32.dll", EntryPoint = "ClipCursor")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClipCursorRelease(nint rect);

    [LibraryImport("user32.dll", EntryPoint = "ShowCursor")]
    internal static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
    internal static partial nint LoadCursor(nint instance, nint cursorName);

    [LibraryImport("user32.dll", EntryPoint = "SetCursor")]
    internal static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static partial uint MapVirtualKey(uint code, uint mapType);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint GetModuleHandle(string? moduleName);

    // Avalonia's importer does not take ownership of a shared-texture handle,
    // and the renderer may close its own at any resize. The consumer
    // duplicates, owns and closes its own.

    internal const uint DUPLICATE_SAME_ACCESS = 0x00000002;

    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentProcess")]
    internal static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "DuplicateHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DuplicateHandle(
        nint sourceProcess,
        nint sourceHandle,
        nint targetProcess,
        out nint targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    // Zero if the handle could not be duplicated. Handle values are recycled,
    // so duplicate while the value is known to be live.
    internal static nint DuplicateForCaller(nint handle)
    {
        if (handle == 0)
            return 0;

        nint self = GetCurrentProcess();
        return DuplicateHandle(self, handle, self, out nint duplicate, 0, false, DUPLICATE_SAME_ACCESS)
            ? duplicate
            : 0;
    }

    // Signed: a client coordinate may be negative.
    internal static int LowInt16(nint value) => (short)((long)value & 0xFFFF);

    internal static int HighInt16(nint value) => (short)(((long)value >> 16) & 0xFFFF);
}
