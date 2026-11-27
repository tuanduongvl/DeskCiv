# Records a short showcase clip from several fixed scenes, with game sound and an English caption on each scene,
# then encodes a small 720p HEVC copy for upload (the browser upload tool accepts files under 10 MB).
# Usage: tools\record-showcase.ps1 -Scenes scenes.json -Out D:\clips\showcase.mp4
# scenes.json: [ { "caption": "...", "args": "--start-year=140 --hour=12", "seconds": 16, "lead": 30 }, ... ]
# "lead" (optional, default 30) is how many seconds run before the kept part: raise it for events that start late,
# such as a test fire (about 40) or the train standing at the station (about 50).
param(
    [Parameter(Mandatory)][string]$Scenes,
    [Parameter(Mandatory)][string]$Out,
    [int]$Seed = 1000,
    [string]$Resolution = "1280x720",
    [int]$VideoKbps = 640
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { $ffmpeg = Join-Path $env:USERPROFILE "miniconda3\Library\bin\ffmpeg.exe" }
$work = Join-Path ([IO.Path]::GetTempPath()) ("deskciv-showcase-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$list = Get-Content $Scenes -Raw | ConvertFrom-Json
$parts = @()
$font = "C\:/Windows/Fonts/segoeui.ttf"
for ($i = 0; $i -lt $list.Count; $i++) {
    $scene = $list[$i]
    $raw = Join-Path $work ("raw{0:D2}.mp4" -f $i)
    $a = @("--path", "`"$root\game`"", "--resolution", $Resolution, "--audio-driver", "Dummy", "--",
        "--windowed", "--fresh", "--no-save", "--seed=$Seed", "--weather=clear", "--duration=$($scene.seconds + $(if ($null -ne $scene.lead) { $scene.lead } else { 30 }))", "--stream-test=`"$raw`"") + ($scene.args -split ' ')
    $p = Start-Process -FilePath $godot -ArgumentList $a -PassThru
    if (-not $p.WaitForExit(240000)) { Stop-Process -Id $p.Id -Force; "scene $i timed out"; continue }
    if (-not (Test-Path $raw)) { "scene $i produced no file"; continue }
    # Loading a world at a late year takes part of --duration, so keep the last seconds of the recording; burn in the caption.
    $text = ($scene.caption -replace "'", "’" -replace ":", "\:")
    $part = Join-Path $work ("part{0:D2}.mp4" -f $i)
    $filter = "drawtext=fontfile='$font':text='$text':fontsize=30:fontcolor=white:box=1:boxcolor=black@0.45:boxborderw=14:x=40:y=40"
    & $ffmpeg -v error -y -sseof (-($scene.seconds + 0.5)) -t $scene.seconds -i $raw -vf "$filter,format=nv12" -af "afade=t=in:d=0.5,afade=t=out:st=$($scene.seconds - 0.6):d=0.6" `
        -c:v h264_mf -b:v 5000k -c:a aac -b:a 128k -ar 48000 -ac 2 $part
    if ((Test-Path $part) -and (Get-Item $part).Length -gt 100000) { $parts += $part; "scene $i ok" } else { "scene $i encode failed" }
}
if ($parts.Count -eq 0) { "no scenes recorded"; return }
$concat = Join-Path $work "list.txt"
$parts | ForEach-Object { "file '$($_ -replace '\\', '/')'" } | Set-Content $concat -Encoding ascii
& $ffmpeg -v error -y -f concat -safe 0 -i $concat -c:v hevc_mf -hw_encoding 1 -b:v "$($VideoKbps)k" -pix_fmt nv12 -c:a aac -b:a 64k -tag:v hvc1 -movflags +faststart $Out
if (-not (Test-Path $Out)) { "final encode failed; parts kept in $work"; return }
$size = (Get-Item $Out).Length / 1MB
"wrote $Out ({0:F1} MB)" -f $size
Remove-Item -Recurse -Force $work
