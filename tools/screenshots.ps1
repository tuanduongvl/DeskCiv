# Renders windowed review screenshots for fixed years, hours and activities without touching saves or the wallpaper.
param([string]$OutDir = "$PSScriptRoot\..\measurements\shots", [int]$Seed = 1000)

$root = (Resolve-Path "$PSScriptRoot\..").Path
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
Get-ChildItem $OutDir -Filter *.png | Remove-Item
$shots = @(
    @{ n = "01-chop"; y = 34.3; h = 11; w = "chop" },
    @{ n = "02-sow"; y = 34.06; h = 10; w = "sow" },
    @{ n = "03-harvest"; y = 34.68; h = 15; w = "gather" },
    @{ n = "04-fish"; y = 34.4; h = 9; w = "fish" },
    @{ n = "05-mine"; y = 34.35; h = 13; w = "mine" },
    @{ n = "06-build"; y = 18.2; h = 12; w = "build" },
    @{ n = "07-knead"; y = 34.45; h = 14; w = "knead" },
    @{ n = "08-night"; y = 34.45; h = 22; w = "chop" },
    @{ n = "09-winter"; y = 35.88; h = 12; w = "" }
)
foreach ($s in $shots) {
    $file = Join-Path $OutDir "$($s.n).png"
    $a = @("--path", "`"$root\game`"", "--resolution", "1920x1080", "--audio-driver", "Dummy", "--",
        "--windowed", "--silent", "--fresh", "--no-save", "--seed=$Seed",
        "--start-year=$($s.y.ToString([Globalization.CultureInfo]::InvariantCulture))", "--hour=$($s.h)", "--duration=6", "--shot=`"$file`"")
    if ($s.w) { $a += "--watch=$($s.w)" }
    $p = Start-Process -FilePath $godot -ArgumentList $a -PassThru
    if (-not $p.WaitForExit(40000)) { Stop-Process -Id $p.Id -Force; "timeout: $($s.n)"; continue }
    "$($s.n): exit=$($p.ExitCode) saved=$(Test-Path $file)"
}
