using System;
using System.Runtime.InteropServices;

namespace DeskCiv;

internal static class Win32
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000;
    public const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8, WS_EX_APPWINDOW = 0x40000;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40;
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    public const uint WM_DESTROY = 2, WM_ACTIVATE = 6, WM_CLOSE = 0x10, WM_NULL = 0, WM_SETFONT = 0x30, WM_COMMAND = 0x111, WM_HSCROLL = 0x114;
    public const uint WM_LBUTTONUP = 0x202, WM_RBUTTONUP = 0x205, WM_APP = 0x8000, WM_USER = 0x400;
    public const uint TBM_GETPOS = WM_USER, TBM_SETPOS = WM_USER + 5, TBM_SETRANGE = WM_USER + 6;
    public const uint BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1;
    public const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    public const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100;
    public const uint SPI_SETDESKWALLPAPER = 20;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct INITCOMMONCONTROLSEX { public int dwSize; public uint dwICC; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string lpszMenuName, lpszClassName; public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32")] public static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32")] public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool enable);
    [DllImport("kernel32")] public static extern uint GetCurrentThreadId();
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr SetWindowsHookExW(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32")] public static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool SetWindowTextW(IntPtr hwnd, string text);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);
    [DllImport("user32")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32")] public static extern long GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32")] public static extern long SetWindowLongPtrW(IntPtr hwnd, int index, long value);
    [DllImport("user32")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT points, uint count);
    [DllImport("user32")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32")] public static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, char[] name, int max);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint action, uint param, string value, uint flags);
    [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEX cls);
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32")] public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32")] public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32")] public static extern void PostQuitMessage(int code);
    [DllImport("user32")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] public static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32")] public static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32")] public static extern IntPtr LoadIconW(IntPtr inst, IntPtr name);
    [DllImport("user32")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32", CharSet = CharSet.Unicode)] public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32")] public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("gdi32")] public static extern IntPtr GetStockObject(int id);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);
    [DllImport("comctl32")] public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX init);
    [DllImport("shell32", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);
}
