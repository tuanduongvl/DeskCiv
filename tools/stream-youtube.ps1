# Streams a DeskCiv world to YouTube Live (1280x720, 30 fps, GPU-encoded). Only the game picture is sent.
#
# One-time setup:
#   1. In YouTube Studio > Create > Go live > Stream, copy the stream key.
#   2. Save it, alone, in %USERPROFILE%\.deskciv\youtube-stream-key.txt (keep this file private).
# Then run this script. The game window is placed off-screen; stop the stream with stop-deskciv.cmd
# or the tray icon's Quit.
param(
    [string]$KeyFile = (Join-Path $env:USERPROFILE ".deskciv\youtube-stream-key.txt"),
    [string]$Resolution = "1280x720"
)
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path $KeyFile)) {
    New-Item -ItemType Directory -Force (Split-Path $KeyFile) | Out-Null
    "Put your YouTube stream key, alone, into: $KeyFile"
    return
}
$godot = Join-Path $root ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { $ffmpeg = Join-Path $env:USERPROFILE "miniconda3\Library\bin\ffmpeg.exe" }
if (-not (Test-Path $ffmpeg)) { "ffmpeg not found; install it or add it to PATH."; return }
$gameArgs = @("--path", "`"$root\game`"", "--resolution", $Resolution, "--position", "-4000,0", "--audio-driver", "Dummy", "--",
          "--windowed", "--silent", "--no-save", "--stream-key-file=`"$KeyFile`"", "--ffmpeg=`"$ffmpeg`"")
Start-Process -FilePath $godot -ArgumentList $gameArgs
"DeskCiv is streaming to YouTube. Open YouTube Studio to see the preview (it takes 10-20 seconds to appear)."
