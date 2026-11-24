using System;
using System.Runtime.InteropServices;
using System.Threading;
using static DeskCiv.Win32;

namespace DeskCiv;

/// <summary>System tray icon with a volume popup (left click) and menu (right click).</summary>
internal sealed class TrayIcon
{
    private const uint CallbackMessage = WM_APP + 1;
    private const uint WM_ENDSESSION = 0x16, WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A;
    private const int MenuPause = 1, MenuQuit = 2, MenuOverlay = 3, MenuStartup = 4, IdMaster = 101, IdAmbient = 102, IdMusic = 103, IdMute = 104, IdSpeed = 105;
    private const int PopupWidth = 310, PopupHeight = 212;
    private static readonly int[] Speeds = { 1, 2, 5, 10, 25, 50, 100, 200, 500 };

    private readonly AudioSettings _audio;
    private readonly WndProc _proc;
    private Thread _thread;
    private IntPtr _hwnd, _popup, _mute, _speedSlider, _speedLabel;
    private volatile int _speedIndex;
    private volatile bool _panning;
    private int _wheel;
    private readonly HookProc _mouseProc;
    private IntPtr _mouseHook;
    private readonly char[] _className = new char[32];
    private readonly IntPtr[] _sliders = new IntPtr[3];
    private volatile bool _paused, _quit;

    public bool Paused => _paused;
    public bool QuitRequested => _quit;
    public int Speed => Speeds[_speedIndex];
    public bool Panning => _panning;
    public int TakeWheel() => Interlocked.Exchange(ref _wheel, 0);
    public Action BeforeSessionEnd { get; init; }

    /// <summary>The command Windows runs at sign-in when the user turns on starting with Windows.</summary>
    public string StartupCommand { get; init; }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", RunName = "DeskCiv";

    private static bool StartsWithWindows()
    {
        using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) != null;
    }

    private void ToggleStartup()
    {
        using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (key.GetValue(RunName) != null) key.DeleteValue(RunName, false);
        else if (!string.IsNullOrEmpty(StartupCommand)) key.SetValue(RunName, StartupCommand);
    }

    public TrayIcon(AudioSettings audio)
    {
        _audio = audio;
        _proc = Proc;
        _mouseProc = MouseHook;
    }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "DeskCiv tray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_CLOSE, 0, 0);
        _thread?.Join(1000);
    }

    private void Run()
    {
        IntPtr inst = GetModuleHandleW(null);
        INITCOMMONCONTROLSEX init = new() { dwSize = Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = 4 };
        InitCommonControlsEx(ref init);
        WNDCLASSEX cls = new()
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = inst,
            hbrBackground = 16,
            lpszClassName = "DeskCivTray",
        };
        RegisterClassExW(ref cls);
        _hwnd = CreateWindowExW(0, "DeskCivTray", "DeskCiv", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        CreatePopup(inst);
        _mouseHook = SetWindowsHookExW(14, _mouseProc, inst, 0);

        NOTIFYICONDATA data = IconData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = LoadIconW(IntPtr.Zero, 32512);
        data.szTip = "DeskCiv";
        Shell_NotifyIconW(NIM_ADD, ref data);

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private NOTIFYICONDATA IconData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1, szTip = "", szInfo = "", szInfoTitle = "",
    };

    private void CreatePopup(IntPtr inst)
    {
        _popup = CreateWindowExW((uint)(WS_EX_TOOLWINDOW | WS_EX_TOPMOST), "DeskCivTray", "Âm lượng DeskCiv",
            unchecked((uint)(WS_POPUP | WS_BORDER)), 0, 0, PopupWidth, PopupHeight, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
        IntPtr font = GetStockObject(17);
        string[] labels = { "Tổng", "Môi trường", "Nhạc" };
        int[] values = { _audio.Master, _audio.Ambient, _audio.Music };
        for (int i = 0; i < 3; i++)
        {
            int y = 12 + i * 40;
            IntPtr label = CreateWindowExW(0, "STATIC", labels[i], (uint)(WS_CHILD | WS_VISIBLE), 12, y + 6, 76, 20, _popup, IntPtr.Zero, inst, IntPtr.Zero);
            SendMessageW(label, WM_SETFONT, font, 1);
            _sliders[i] = CreateWindowExW(0, "msctls_trackbar32", "", (uint)(WS_CHILD | WS_VISIBLE), 92, y, 204, 32, _popup, IdMaster + i, inst, IntPtr.Zero);
            SendMessageW(_sliders[i], TBM_SETRANGE, 1, 100 << 16);
            SendMessageW(_sliders[i], TBM_SETPOS, 1, values[i]);
        }
        _speedLabel = CreateWindowExW(0, "STATIC", "Tốc độ x1", (uint)(WS_CHILD | WS_VISIBLE), 12, 138, 78, 20, _popup, IntPtr.Zero, inst, IntPtr.Zero);
        SendMessageW(_speedLabel, WM_SETFONT, font, 1);
        _speedSlider = CreateWindowExW(0, "msctls_trackbar32", "", (uint)(WS_CHILD | WS_VISIBLE), 92, 132, 204, 32, _popup, IdSpeed, inst, IntPtr.Zero);
        SendMessageW(_speedSlider, TBM_SETRANGE, 1, (Speeds.Length - 1) << 16);
        SendMessageW(_speedSlider, TBM_SETPOS, 1, 0);
        _mute = CreateWindowExW(0, "BUTTON", "Tắt tiếng", (uint)(WS_CHILD | WS_VISIBLE | 3), 12, 178, 120, 22, _popup, IdMute, inst, IntPtr.Zero);
        SendMessageW(_mute, WM_SETFONT, font, 1);
        SendMessageW(_mute, BM_SETCHECK, _audio.Muted ? 1 : 0, 0);
    }

    private IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case CallbackMessage when (uint)lParam == WM_LBUTTONUP:
                ShowPopup();
                return 0;
            case CallbackMessage when (uint)lParam == WM_RBUTTONUP:
                ShowMenu();
                return 0;
            case WM_HSCROLL when hwnd == _popup && lParam == _speedSlider:
                _speedIndex = Math.Clamp((int)SendMessageW(_speedSlider, TBM_GETPOS, 0, 0), 0, Speeds.Length - 1);
                SetWindowTextW(_speedLabel, $"Tốc độ x{Speed}");
                return 0;
            case WM_HSCROLL when hwnd == _popup:
                ReadSliders((int)wParam & 0xFFFF);
                return 0;
            case WM_COMMAND when hwnd == _popup && ((int)wParam & 0xFFFF) == IdMute:
                _audio.Muted = SendMessageW(_mute, BM_GETCHECK, 0, 0) == 1;
                _audio.Save();
                return 0;
            case WM_ACTIVATE when hwnd == _popup && ((int)wParam & 0xFFFF) == 0:
                ShowWindow(_popup, 0);
                return 0;
            case WM_ENDSESSION when hwnd == _hwnd && wParam != 0:
                BeforeSessionEnd?.Invoke();
                _quit = true;
                return 0;
            case WM_CLOSE when hwnd == _hwnd:
                _quit = true;
                DestroyWindow(_popup);
                DestroyWindow(_hwnd);
                return 0;
            case WM_DESTROY when hwnd == _hwnd:
                if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
                NOTIFYICONDATA data = IconData();
                Shell_NotifyIconW(NIM_DELETE, ref data);
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Observes wheel and middle button and always passes them on, so Windows behaves normally.</summary>
    private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            uint kind = (uint)message;
            if (kind == WM_MBUTTONUP) _panning = false;
            else if (kind == WM_MBUTTONDOWN && PointerOnDesktop(ReadPoint(data))) _panning = true;
            else if (kind == WM_MOUSEWHEEL && GetAsyncKeyState(0x11) >= 0 && PointerOnDesktop(ReadPoint(data)))
            {
                short amount = (short)(Marshal.ReadInt32(data, 8) >> 16);
                if (amount != 0) Interlocked.Add(ref _wheel, amount > 0 ? 1 : -1);
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private static POINT ReadPoint(IntPtr data) => new() { X = Marshal.ReadInt32(data, 0), Y = Marshal.ReadInt32(data, 4) };

    private bool PointerOnDesktop(POINT point)
    {
        MONITORINFO info = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromPoint(default, 1), ref info)) return false;
        if (point.X < info.rcMonitor.Left || point.X >= info.rcMonitor.Right || point.Y < info.rcMonitor.Top || point.Y >= info.rcMonitor.Bottom) return false;
        int length = GetClassNameW(WindowFromPoint(point), _className, _className.Length);
        string name = new(_className, 0, Math.Max(0, length));
        return name is "SysListView32" or "SHELLDLL_DefView" or "Progman" or "WorkerW" or "Engine";
    }

    private void ReadSliders(int scrollCode)
    {
        _audio.Master = (int)SendMessageW(_sliders[0], TBM_GETPOS, 0, 0);
        _audio.Ambient = (int)SendMessageW(_sliders[1], TBM_GETPOS, 0, 0);
        _audio.Music = (int)SendMessageW(_sliders[2], TBM_GETPOS, 0, 0);
        const int endScroll = 8;
        if (scrollCode == endScroll) _audio.Save();
    }

    private void ShowPopup()
    {
        GetCursorPos(out POINT cursor);
        MONITORINFO info = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref info);
        int x = Math.Clamp(cursor.X - PopupWidth / 2, info.rcWork.Left, info.rcWork.Right - PopupWidth);
        int y = Math.Clamp(cursor.Y - PopupHeight - 12, info.rcWork.Top, info.rcWork.Bottom - PopupHeight);
        SetWindowPos(_popup, new IntPtr(-1), x, y, PopupWidth, PopupHeight, SWP_SHOWWINDOW);
        SetForegroundWindow(_popup);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        AppendMenuW(menu, MF_STRING | (_paused ? MF_CHECKED : 0), MenuPause, "Tạm dừng");
        AppendMenuW(menu, MF_STRING | (_audio.ShowOverlay ? MF_CHECKED : 0), MenuOverlay, "Hiện thông tin");
        AppendMenuW(menu, MF_STRING | (StartsWithWindows() ? MF_CHECKED : 0), MenuStartup, "Chạy cùng Windows");
        AppendMenuW(menu, MF_SEPARATOR, 0, null);
        AppendMenuW(menu, MF_STRING, MenuQuit, "Thoát");
        GetCursorPos(out POINT p);
        SetForegroundWindow(_hwnd);
        int command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, 0, 0);
        DestroyMenu(menu);
        if (command == MenuOverlay)
        {
            _audio.ShowOverlay = !_audio.ShowOverlay;
            _audio.Save();
        }
        else if (command == MenuStartup) ToggleStartup();
        else if (command == MenuPause) _paused = !_paused;
        else if (command == MenuQuit) _quit = true;
    }
}
