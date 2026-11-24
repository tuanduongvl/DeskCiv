using System;
using System.Collections.Generic;
using System.Diagnostics;
using static DeskCiv.Win32;

namespace DeskCiv;

public enum RenderState { Full, Reduced, Stopped }

/// <summary>Places the game window behind desktop icons and reports how much of it is visible.</summary>
internal sealed class DesktopHost
{
    private readonly IntPtr _hwnd;
    private readonly uint _pid = (uint)Environment.ProcessId;
    private IntPtr _parent, _defView;
    private RECT _bounds;

    public string Mode { get; private set; } = "none";

    private readonly bool _useLayered;

    public DesktopHost(IntPtr hwnd, bool useLayered = true)
    {
        _hwnd = hwnd;
        _useLayered = useLayered;
    }

    public bool Attach()
    {
        IntPtr progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero) return false;
        SendMessageTimeoutW(progman, 0x052C, 0xD, 1, 0, 1000, out _);

        _defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        IntPtr worker = FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
        bool raisedDesktop = _defView != IntPtr.Zero && worker != IntPtr.Zero;
        if (raisedDesktop)
        {
            _parent = progman;
            Mode = "progman-layered";
        }
        else
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((top, _) =>
            {
                IntPtr view = FindWindowExW(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero) return true;
                _defView = view;
                found = FindWindowExW(IntPtr.Zero, top, "WorkerW", null);
                return false;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero) return false;
            _parent = found;
            Mode = "workerw";
        }

        long style = GetWindowLongPtrW(_hwnd, GWL_STYLE);
        style = (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD | WS_VISIBLE;
        SetWindowLongPtrW(_hwnd, GWL_STYLE, style);
        long ex = GetWindowLongPtrW(_hwnd, GWL_EXSTYLE);
        ex = (ex & ~WS_EX_APPWINDOW) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (raisedDesktop && _useLayered) ex |= WS_EX_LAYERED;
        SetWindowLongPtrW(_hwnd, GWL_EXSTYLE, ex);
        if (raisedDesktop && _useLayered) SetLayeredWindowAttributes(_hwnd, 0, 255, 2);

        if (SetParent(_hwnd, _parent) == IntPtr.Zero) return false;

        // A wallpaper must never share Explorer's input queue or hold activation.
        uint shellThread = GetWindowThreadProcessId(_parent, out _);
        AttachThreadInput(GetCurrentThreadId(), shellThread, false);
        EnableWindow(_hwnd, false);
        if (GetForegroundWindow() == _hwnd) SetForegroundWindow(_parent);

        _bounds = default;
        UpdateBounds();
        if (raisedDesktop)
        {
            SetWindowPos(_hwnd, _defView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(worker, _hwnd, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        return true;
    }

    /// <summary>Tells Windows this UI thread is alive. Returns immediately and consumes no messages.</summary>
    public void KeepResponsive()
    {
        const uint allInput = 0x04FF, inputAvailable = 4;
        MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 0, allInput, inputAvailable);
        if (GetForegroundWindow() == _hwnd) SetForegroundWindow(_parent);
    }

    /// <summary>Refits the window to the primary monitor. Returns true when the layout changed.</summary>
    public bool UpdateBounds()
    {
        MONITORINFO primary = new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromPoint(default, 1), ref primary)) return false;
        int x = primary.rcMonitor.Left, y = primary.rcMonitor.Top, w = primary.rcMonitor.Width, h = primary.rcMonitor.Height;
        if (_bounds.Left == x && _bounds.Top == y && _bounds.Width == w && _bounds.Height == h) return false;
        _bounds = new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        POINT origin = new() { X = x, Y = y };
        MapWindowPoints(IntPtr.Zero, _parent, ref origin, 1);
        // One pixel short of the monitor prevents the GPU driver from promoting this window to direct scanout above other windows.
        SetWindowPos(_hwnd, IntPtr.Zero, origin.X, origin.Y, w, h - 1, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        return true;
    }

    /// <summary>Primary monitor work area in window coordinates.</summary>
    public Godot.Rect2I PrimaryWorkArea()
    {
        MONITORINFO info = new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(default, 1), ref info);
        return new Godot.Rect2I(info.rcWork.Left - _bounds.Left, info.rcWork.Top - _bounds.Top, info.rcWork.Width, info.rcWork.Height);
    }

    public Godot.Vector2 CursorInWindow()
    {
        GetCursorPos(out POINT point);
        return new Godot.Vector2(point.X - _bounds.Left, point.Y - _bounds.Top);
    }

    public RenderState Evaluate()
    {
        var monitors = new List<(RECT full, RECT work)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            MONITORINFO info = new() { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            const uint primaryFlag = 1;
            if (GetMonitorInfoW(monitor, ref info) && (info.dwFlags & primaryFlag) != 0) monitors.Add((info.rcMonitor, info.rcWork));
            return true;
        }, IntPtr.Zero);

        var covered = new bool[monitors.Count];
        bool fullscreen = false;
        char[] cls = new char[64];
        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidate(hwnd, cls) || !GetWindowRect(hwnd, out RECT r)) return true;
            bool zoomed = IsZoomed(hwnd);
            for (int i = 0; i < monitors.Count; i++)
            {
                if (!zoomed && Contains(r, monitors[i].full)) fullscreen = true;
                if (Contains(r, monitors[i].work)) covered[i] = true;
            }
            return true;
        }, IntPtr.Zero);

        if (fullscreen) return RenderState.Stopped;
        int count = 0;
        foreach (bool c in covered) if (c) count++;
        if (monitors.Count > 0 && count == monitors.Count) return RenderState.Stopped;
        return count > 0 ? RenderState.Reduced : RenderState.Full;
    }

    private bool IsCandidate(IntPtr hwnd, char[] cls)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _pid) return false;
        long ex = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return false;
        if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        int len = GetClassNameW(hwnd, cls, cls.Length);
        string name = new(cls, 0, Math.Max(0, len));
        return name is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static bool Contains(RECT outer, RECT inner) =>
        outer.Left <= inner.Left && outer.Top <= inner.Top && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    public void Detach()
    {
        SetParent(_hwnd, IntPtr.Zero);
        ShowWindow(_hwnd, 0);
        SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, null, 0);
    }
}
