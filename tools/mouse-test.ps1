# Tests wheel zoom, middle-drag pan, ignored input over a window, and director resume.
# It moves the pointer, uses only wheel and middle button on a blank desktop spot, then restores the pointer.
param([int]$ResumeSeconds = 60)

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
$metrics = Join-Path $root "measurements\mouse-metrics.csv"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class M {
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32")] public static extern bool GetCursorPos(out P p);
  [DllImport("user32")] public static extern void mouse_event(uint f, int x, int y, int d, UIntPtr e);
  [DllImport("user32")] public static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static string At(int x, int y) { var s = new StringBuilder(64); GetClassNameW(WindowFromPoint(new P { X = x, Y = y }), s, 64); return s.ToString(); }
  public static void Wheel(int notches) { for (int i = 0; i < Math.Abs(notches); i++) { mouse_event(0x800, 0, 0, notches > 0 ? 120 : -120, UIntPtr.Zero); System.Threading.Thread.Sleep(60); } }
  public static void Drag(int x, int y, int dx, int dy) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(120); mouse_event(0x20, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(120);
    for (int i = 1; i <= 25; i++) { SetCursorPos(x + dx * i / 25, y + dy * i / 25); System.Threading.Thread.Sleep(25); }
    System.Threading.Thread.Sleep(120); mouse_event(0x40, 0, 0, 0, UIntPtr.Zero);
  }
}
"@

function Get-Row { Start-Sleep -Milliseconds 5600; Import-Csv $metrics | Select-Object -Last 1 }
$old = New-Object M+P; [void][M]::GetCursorPos([ref]$old)
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$spot = $null
foreach ($q in @(@(0.66,0.8), @(0.74,0.84), @(0.82,0.78), @(0.9,0.86), @(0.58,0.88))) {
    $x = [int]($b.X + $b.Width * $q[0]); $y = [int]($b.Y + $b.Height * $q[1])
    if ([M]::At($x, $y) -eq "SysListView32") { $spot = @($x, $y); break }
}
if (-not $spot) { "skipped: no uncovered desktop spot"; exit 2 }

$a = @("--path", "`"$root\game`"", "--audio-driver", "Dummy", "--", "--silent", "--fresh", "--no-save", "--seed=1000", "--start-year=20", "--no-occlusion", "--duration=$($ResumeSeconds + 62)", "--metrics=`"$metrics`"")
$proc = Start-Process -FilePath $godot -ArgumentList $a -PassThru
try {
    Start-Sleep -Seconds 9
    [void][M]::SetCursorPos($spot[0], $spot[1]); Start-Sleep -Milliseconds 200
    $start = Get-Row
    [M]::Wheel(5)
    $in = Get-Row
    "zoom_in: distance $($start.camera_distance) (target $($start.distance_target)) -> $($in.camera_distance) (target $($in.distance_target)), manual=$($in.manual), left=$($in.manual_left)"
    [M]::Wheel(-3)
    $out = Get-Row
    "zoom_out: distance $($in.camera_distance) -> $($out.camera_distance) (target $($out.distance_target)), manual=$($out.manual), left=$($out.manual_left)"
    [M]::Drag($spot[0], $spot[1], -220, -90)
    [void][M]::SetCursorPos($spot[0], $spot[1])
    $pan = Get-Row
    "pan: focus $($out.focus_x),$($out.focus_z) -> $($pan.focus_x),$($pan.focus_z), manual=$($pan.manual), left=$($pan.manual_left)"

    $form = [System.Windows.Forms.Form]::new()
    $form.FormBorderStyle = "None"; $form.StartPosition = "Manual"; $form.ShowInTaskbar = $false; $form.BackColor = "DimGray"
    $form.Bounds = [System.Drawing.Rectangle]::new($spot[0] - 60, $spot[1] - 60, 120, 120)
    [void][M]::ShowWindow($form.Handle, 4)
    [void][M]::SetWindowPos($form.Handle, [IntPtr](-1), $spot[0] - 60, $spot[1] - 60, 120, 120, 0x10)
    [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 250
    "pointer_over_test_window: $([M]::At($spot[0], $spot[1]))"
    [M]::Wheel(3)
    $over = Get-Row
    "ignored_over_window: distance $($pan.camera_distance) -> $($over.camera_distance)"
    $form.Close(); $form.Dispose()
    [void][M]::SetCursorPos($old.X, $old.Y)

    Start-Sleep -Seconds ($ResumeSeconds + 2)
    $resume = Get-Row
    "director_resumed: manual=$($resume.manual), distance=$($resume.camera_distance)"
    "fps_after_input: $($resume.fps)"
    if (-not $proc.WaitForExit(40000)) { "app did not exit in time" } else { "exit_code: $($proc.ExitCode)" }
}
finally {
    [void][M]::SetCursorPos($old.X, $old.Y)
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
