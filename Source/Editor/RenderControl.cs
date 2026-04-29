using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace JzRE.Editor;

public class RenderControl : NativeControlHost
{
    private const string WindowClassName = "JzRE_RenderHost";
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;

    private static ushort s_classAtom;
    private static bool s_classRegistered;
    private static readonly WndProc s_wndProc = WindowProc;

    private IPlatformHandle? _nativeHandle;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            _nativeHandle = base.CreateNativeControlCore(parent);
            return _nativeHandle;
        }

        EnsureWindowClass();

        nint parentHwnd = parent.Handle;
        nint hInstance = GetModuleHandle(null);
        nint hwnd = CreateWindowExW(
            0,
            WindowClassName,
            string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, 1, 1,
            parentHwnd,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create native render host window.");

        _nativeHandle = new PlatformHandle(hwnd, "HWND");
        return _nativeHandle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsWindows())
        {
            if (control.Handle != IntPtr.Zero)
                DestroyWindow(control.Handle);

            _nativeHandle = null;
            return;
        }

        _nativeHandle = null;
        base.DestroyNativeControlCore(control);
    }

    public IntPtr TryGetNativeHandle() => _nativeHandle?.Handle ?? IntPtr.Zero;

    public void UpdateNativeBounds()
    {
        var handle = _nativeHandle?.Handle ?? IntPtr.Zero;
        if (!OperatingSystem.IsWindows() || handle == IntPtr.Zero)
            return;

        var bounds = Bounds;
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        SetWindowPos(handle, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private static void EnsureWindowClass()
    {
        if (s_classRegistered)
            return;

        var wc = new WNDCLASSW
        {
            lpfnWndProc = s_wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = WindowClassName
        };

        s_classAtom = RegisterClassW(ref wc);
        int error = Marshal.GetLastWin32Error();
        if (s_classAtom == 0 && error != 1410)
            throw new InvalidOperationException($"Failed to register render host window class. Win32 error: {error}");

        s_classRegistered = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    private static nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
        => DefWindowProcW(hWnd, msg, wParam, lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
}
