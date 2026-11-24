# Exercises desktop hit testing, tray volume, mute and fullscreen pause. Uses a dummy audio driver, so it is silent.
param([string]$Shot = "")

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
$metrics = Join-Path $root "measurements\functional-metrics.csv"
$settings = Join-Path $env:LOCALAPPDATA "DeskCiv\settings.json"
New-Item -ItemType Directory -Force (Split-Path $metrics) | Out-Null
$backup = if (Test-Path $settings) { Get-Content $settings -Raw } else { $null }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class T {
  [DllImport("user32", CharSet=CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint a, uint p, string v, uint f);
  [StructLayout(LayoutKind.Sequential)] public struct P { public int X, Y; }
  [DllImport("user32", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr p, IntPtr a, string c, string t);
  [DllImport("user32")] public static extern IntPtr WindowFromPoint(P p);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32")] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32")] public static extern bool ShowWindow(IntPtr h, int c);
  public static string ClassAt(int x, int y) { var p = new P { X = x, Y = y }; var s = new StringBuilder(64); GetClassNameW(WindowFromPoint(p), s, 64); return s.ToString(); }
}
"@

function Get-Last { Import-Csv $metrics | Select-Object -Last 1 }
function Wait-Until($t) { $left = $t - ((Get-Date) - $script:start).TotalSeconds; if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) } }

try {
    $a = @("--path", "`"$root\game`"", "--audio-driver", "Dummy", "--", "--fresh", "--no-save", "--seed=1000", "--start-year=10", "--duration=88", "--metrics=`"$metrics`"")
    $proc = Start-Process -FilePath $godot -ArgumentList $a -PassThru
    $script:start = Get-Date

    Wait-Until 10
    $primary = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    "icon_hit_test: $([T]::ClassAt($primary.X + 40, $primary.Y + 40)) (expected SysListView32)"
    if ($Shot) {
        $v = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = [System.Drawing.Bitmap]::new($v.Width, $v.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($v.Location, [System.Drawing.Point]::Empty, $v.Size)
        ([System.Drawing.Bitmap]::new($bmp, [System.Drawing.Size]::new(1920, 540))).Save($Shot, [System.Drawing.Imaging.ImageFormat]::Png)
    }

    Wait-Until 12
    $tray = [T]::FindWindowExW([IntPtr]::Zero, [IntPtr]::Zero, "DeskCivTray", "DeskCiv")
    $popup = [T]::FindWindowExW([IntPtr]::Zero, [IntPtr]::Zero, "DeskCivTray", "DeskCiv volume")
    [void][T]::PostMessageW($tray, 0x8001, [IntPtr]1, [IntPtr]0x202)
    Start-Sleep -Milliseconds 500
    "popup_opens: $([T]::IsWindowVisible($popup))"
    $slider = [T]::FindWindowExW($popup, [IntPtr]::Zero, "msctls_trackbar32", [NullString]::Value)
    [void][T]::SendMessageW($slider, 0x405, [IntPtr]1, [IntPtr]40)
    [void][T]::SendMessageW($popup, 0x114, [IntPtr]8, $slider)
    "saved_master: $((Get-Content $settings -Raw | ConvertFrom-Json).Master) (expected 40)"
    [void][T]::ShowWindow($popup, 0)

    Wait-Until 26
    "bus_db_at_40pct: $((Get-Last).master_db) (expected about -8.0)"
    $mute = [T]::FindWindowExW($popup, [IntPtr]::Zero, "BUTTON", "Mute")
    if ($mute -eq [IntPtr]::Zero) { throw "Mute checkbox not found" }
    [void][T]::SendMessageW($mute, 0xF1, [IntPtr]1, [IntPtr]0)
    [void][T]::SendMessageW($popup, 0x111, [IntPtr]104, $mute)

    Wait-Until 39
    "bus_db_muted: $((Get-Last).master_db) (expected -60 or lower)"
    [void][T]::SendMessageW($mute, 0xF1, [IntPtr]0, [IntPtr]0)
    [void][T]::SendMessageW($popup, 0x111, [IntPtr]104, $mute)
    "state_before_fullscreen: $((Get-Last).state)"

    $screen = [System.Windows.Forms.Screen]::PrimaryScreen
    $form = [System.Windows.Forms.Form]::new()
    $form.FormBorderStyle = "None"; $form.StartPosition = "Manual"; $form.Bounds = $screen.Bounds; $form.BackColor = "Black"; $form.ShowInTaskbar = $false
    [void][T]::ShowWindow($form.Handle, 4); $form.Bounds = $screen.Bounds; [System.Windows.Forms.Application]::DoEvents()
    Wait-Until 52
    $during = Get-Last
    "state_during_fullscreen: $($during.state), fps=$($during.fps)"
    $form.Close(); $form.Dispose()

    Wait-Until 64
    $afterRow = Get-Last
    "state_after_fullscreen: $($afterRow.state), fps=$($afterRow.fps)"

    $speedSlider = $slider
    foreach ($n in 1..3) { $speedSlider = [T]::FindWindowExW($popup, $speedSlider, "msctls_trackbar32", [NullString]::Value) }
    if ($speedSlider -eq [IntPtr]::Zero) { throw "Speed slider not found" }
    [void][T]::SendMessageW($speedSlider, 0x405, [IntPtr]1, [IntPtr]6)
    [void][T]::SendMessageW($popup, 0x114, [IntPtr]8, $speedSlider)
    Start-Sleep -Milliseconds 1200
    $speedA = Get-Last
    Wait-Until 82
    $speedB = Get-Last
    $rate = ([double]$speedB.year - [double]$speedA.year) / ([double]$speedB.seconds - [double]$speedA.seconds) * 1800
    "speed_at_x100: measured x$([math]::Round($rate, 0)) (expected about x100), fps=$($speedB.fps)"
    Wait-Process -Id $proc.Id -Timeout 20 -ErrorAction SilentlyContinue
    "exit_code: $($proc.ExitCode)"
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        [void][T]::SystemParametersInfoW(20, 0, [NullString]::Value, 0)
    }
    if ($null -ne $backup) { Set-Content $settings $backup -NoNewline } elseif (Test-Path $settings) { Remove-Item $settings }
}
