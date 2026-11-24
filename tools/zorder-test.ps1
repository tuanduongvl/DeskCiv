# Checks that DeskCiv stays behind normal windows and desktop icons on the primary monitor.
# Opens a small gray test window, clicks one blank desktop spot, then restores the pointer. Uses a fresh unsaved world.
# With -RaiseDesktop it briefly moves the desktop above the test window and checks that DeskCiv puts it back.
param([ValidateSet("Normal", "Hidden")] [string]$StartStyle = "Normal", [int]$Seconds = 24, [switch]$RaiseDesktop)

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class Z {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  public delegate bool E(IntPtr h, IntPtr l);
  [DllImport("user32")] static extern bool EnumWindows(E e, IntPtr l);
  [DllImport("user32")] static extern bool EnumChildWindows(IntPtr h, E e, IntPtr l);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32")] static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32")] public static extern bool GetCursorPos(out P p);
  [DllImport("user32")] public static extern void mouse_event(uint f, int x, int y, int d, UIntPtr e);
  [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int t, uint f);
  [DllImport("user32")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string N(IntPtr h) { if (h == IntPtr.Zero) return "none"; var s = new StringBuilder(64); GetClassNameW(h, s, 64); return s.ToString(); }
  public static IntPtr Progman() { return FindWindowW("Progman", null); }
  public static string At(int x, int y) { return N(WindowFromPoint(new P { X = x, Y = y })); }
  public static string RootAt(int x, int y) { return N(GetAncestor(WindowFromPoint(new P { X = x, Y = y }), 2)); }
  public static int Rank(IntPtr target) { int i = 0; for (IntPtr h = GetWindow(Progman(), 0); h != IntPtr.Zero; h = GetWindow(h, 2)) { if (h == target) return i; if (IsWindowVisible(h)) i++; } return -1; }
  public static string Children() { var r = new List<string>(); for (IntPtr h = GetWindow(Progman(), 5); h != IntPtr.Zero; h = GetWindow(h, 2)) r.Add(N(h)); return string.Join(" > ", r); }
  public static int VisibleAfter(IntPtr target) { int n = 0; for (IntPtr h = GetWindow(target, 2); h != IntPtr.Zero; h = GetWindow(h, 2)) if (IsWindowVisible(h)) n++; return n; }
  public static bool Above(IntPtr first, IntPtr second) { for (IntPtr h = GetWindow(first, 2); h != IntPtr.Zero; h = GetWindow(h, 2)) if (h == second) return true; return false; }
}
"@

$oldCursor = New-Object Z+P; [void][Z]::GetCursorPos([ref]$oldCursor)
$oldForeground = [Z]::GetForegroundWindow()
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$a = @("--path", "`"$root\game`"", "--audio-driver", "Dummy", "--", "--silent", "--fresh", "--no-save", "--seed=1000", "--start-year=8", "--duration=$Seconds")
$proc = Start-Process -FilePath $godot -ArgumentList $a -WindowStyle $StartStyle -PassThru
$form = $null
$failures = 0
try {
    Start-Sleep -Seconds 7
    $form = [System.Windows.Forms.Form]::new()
    $form.Text = "DeskCiv z-order test"; $form.FormBorderStyle = "FixedToolWindow"; $form.StartPosition = "Manual"; $form.BackColor = "DimGray"; $form.ShowInTaskbar = $false
    $form.Bounds = [System.Drawing.Rectangle]::new($b.X + [int]($b.Width * .42), $b.Y + [int]($b.Height * .34), 300, 220)
    $form.Show(); [System.Windows.Forms.Application]::DoEvents(); $form.Activate(); [void][Z]::SetForegroundWindow($form.Handle); [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 1500; [System.Windows.Forms.Application]::DoEvents()
    $cx = $form.Left + 150; $cy = $form.Top + 110

    function Test-Layer($label) {
        [System.Windows.Forms.Application]::DoEvents()
        $hit = [Z]::At($cx, $cy)
        $above = [Z]::Above($form.Handle, [Z]::Progman())
        $ok = $hit -like "WindowsForms*" -and $above
        if (-not $ok) { $script:failures++ }
        "$label`: ok=$ok hit=$hit form_above_desktop=$above foreground=$([Z]::N([Z]::GetForegroundWindow())) desktop_windows_below=$([Z]::VisibleAfter([Z]::Progman()))"
    }

    Test-Layer "after_open"
    "desktop_children: $([Z]::Children())"
    $blank = $null
    foreach ($q in @(@(.66,.8), @(.74,.84), @(.82,.78), @(.9,.86), @(.58,.88))) { $x = [int]($b.X + $b.Width * $q[0]); $y = [int]($b.Y + $b.Height * $q[1]); if ([Z]::At($x, $y) -eq "SysListView32") { $blank = @($x, $y); break } }
    "icon_layer_hit: $(if ($blank) { [Z]::At($blank[0], $blank[1]) } else { 'no blank spot' })"
    if ($blank) {
        [void][Z]::SetCursorPos($blank[0], $blank[1]); Start-Sleep -Milliseconds 120
        [Z]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [Z]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
        [void][Z]::SetCursorPos($oldCursor.X, $oldCursor.Y)
        Test-Layer "after_desktop_click"
        $form.Activate(); [void][Z]::SetForegroundWindow($form.Handle); Start-Sleep -Milliseconds 1500
        Test-Layer "after_return_to_window"
    }
    if ($RaiseDesktop) {
        $form.Activate(); [void][Z]::SetForegroundWindow($form.Handle); Start-Sleep -Milliseconds 400
        [void][Z]::SetWindowPos([Z]::Progman(), [IntPtr]::Zero, 0, 0, 0, 0, 0x13)
        Start-Sleep -Milliseconds 150
        "raised_desktop_now: form_above_desktop=$([Z]::Above($form.Handle, [Z]::Progman()))"
        Start-Sleep -Milliseconds 2600
        Test-Layer "after_guard_wait"
        [void][Z]::SetWindowPos([Z]::Progman(), [IntPtr]1, 0, 0, 0, 0, 0x13)
    }
    foreach ($i in 1..4) { Start-Sleep -Seconds 1; [System.Windows.Forms.Application]::DoEvents(); if ([Z]::At($cx, $cy) -notlike "WindowsForms*") { $failures++; "lost_window_at_sample_$i hit=$([Z]::At($cx, $cy))" } }
    "desktop_children_end: $([Z]::Children())"
}
finally {
    if ($RaiseDesktop) { [void][Z]::SetWindowPos([Z]::Progman(), [IntPtr]1, 0, 0, 0, 0, 0x13) }
    if ($form) { $form.Close(); $form.Dispose() }
    [void][Z]::SetCursorPos($oldCursor.X, $oldCursor.Y)
    if ($oldForeground -ne [IntPtr]::Zero) { [void][Z]::SetForegroundWindow($oldForeground) }
    if (-not $proc.WaitForExit(($Seconds + 8) * 1000)) { Stop-Process -Id $proc.Id -Force; "forced stop" }
}
"style=$StartStyle failures=$failures exit=$($proc.ExitCode)"
