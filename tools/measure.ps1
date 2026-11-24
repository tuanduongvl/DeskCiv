# Runs the DeskCiv prototype on the desktop and reports per-process FPS, CPU, GPU and RAM.
param(
    [int]$Seconds = 45,
    [string]$OutDir = "$PSScriptRoot\..\measurements",
    [string]$Label = "run",
    [int]$StartYear = 40,
    [switch]$RespectOcclusion
)

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$metrics = Join-Path $OutDir "$Label-metrics.csv"
$gpuFile = Join-Path $OutDir "$Label-gpu.csv"

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class DeskTree {
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern IntPtr FindWindowW(string c, string t);
  [DllImport("user32")] static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32", CharSet=CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  public static string Children(uint pid) {
    var r = new List<string>();
    for (IntPtr h = GetWindow(FindWindowW("Progman", null), 5); h != IntPtr.Zero; h = GetWindow(h, 2)) {
      var s = new StringBuilder(64); GetClassNameW(h, s, 64); uint p; GetWindowThreadProcessId(h, out p);
      r.Add(p == pid ? "DESKCIV(" + s + ")" : s.ToString());
    }
    return string.Join(" > ", r);
  }
}
"@

$argsList = @("--path", "`"$root\game`"", "--", "--silent", "--fresh", "--no-save", "--seed=1000", "--start-year=$StartYear", "--duration=$Seconds", "--metrics=`"$metrics`"")
if (-not $RespectOcclusion) { $argsList += "--no-occlusion" }
$proc = Start-Process -FilePath $godot -ArgumentList $argsList -PassThru
$samples = [System.Collections.Generic.List[double]]::new()
"seconds,gpu_3d_pct" | Set-Content $gpuFile
$started = Get-Date
$tree = ""
while (-not $proc.HasExited) {
    $elapsed = ((Get-Date) - $started).TotalSeconds
    if (-not $tree -and $elapsed -gt 8) { $tree = [DeskTree]::Children([uint32]$proc.Id) }
    try {
        $paths = (Get-Counter -ListSet "GPU Engine").PathsWithInstances | Where-Object { $_ -like "*pid_$($proc.Id)_*engtype_3D*Utilization Percentage" }
        if ($paths) {
            $value = ((Get-Counter -Counter $paths -SampleInterval 4 -MaxSamples 1 -ErrorAction Stop).CounterSamples | Measure-Object CookedValue -Sum).Sum
            if ($elapsed -gt 10) { $samples.Add($value) }
            "$([int]$elapsed),$([math]::Round($value,2))" | Add-Content $gpuFile
        } else { Wait-Process -Id $proc.Id -Timeout 2 -ErrorAction SilentlyContinue }
    } catch { if (-not $proc.HasExited) { Wait-Process -Id $proc.Id -Timeout 2 -ErrorAction SilentlyContinue } }
}

$rows = @(Import-Csv $metrics)
$settled = @($rows | Where-Object { [double]$_.seconds -gt 10 })
$full = @($settled | Where-Object { $_.state -eq "Full" })
function Get-Avg($items, $name) { if ($items.Count) { [math]::Round(($items | ForEach-Object { [double]$_.$name } | Measure-Object -Average).Average, 2) } else { $null } }
[pscustomobject]@{
    exit_code = $proc.ExitCode
    samples = $rows.Count
    full_state_samples = $full.Count
    states = (($settled | Group-Object state | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join " ")
    mode = if ($rows.Count) { $rows[-1].mode } else { "" }
    size = if ($rows.Count) { "$($rows[-1].width)x$($rows[-1].height)" } else { "" }
    fps_avg_full = Get-Avg $full "fps"
    cpu_avg_all_cores = Get-Avg $settled "cpu_pct_all_cores"
    gpu_avg_3d = if ($samples.Count) { [math]::Round(($samples | Measure-Object -Average).Average, 2) } else { $null }
    gpu_max_3d = if ($samples.Count) { [math]::Round(($samples | Measure-Object -Maximum).Maximum, 2) } else { $null }
    ram_first_mb = if ($settled.Count) { $settled[0].ram_mb } else { $null }
    ram_last_mb = if ($settled.Count) { $settled[-1].ram_mb } else { $null }
    ram_max_mb = if ($settled.Count) { ($settled | ForEach-Object { [double]$_.ram_mb } | Measure-Object -Maximum).Maximum } else { $null }
    progman_children = $tree
} | Format-List
