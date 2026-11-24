# Builds a self-contained portable folder at dist\DeskCiv. Run from anywhere.
# It bundles the Godot .NET runtime binary, the local .NET runtime and the built game. No download is needed.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$engineSource = Join-Path $repo '.tools\Godot_v4.7.2-stable_mono_win64'
$dist = Join-Path $repo 'dist\DeskCiv'

dotnet build (Join-Path $repo 'game\DeskCiv.csproj') -c Debug -v q
if ($LASTEXITCODE -ne 0) { throw 'Game build failed' }

$config = Get-Content (Join-Path $repo 'game\.godot\mono\temp\bin\Debug\DeskCiv.runtimeconfig.json') -Raw | ConvertFrom-Json
$wanted = $config.runtimeOptions.framework.version
$dotnetHome = Split-Path -Parent (Get-Command dotnet).Source
$major = ($wanted -split '\.')[0..1] -join '.'
$runtime = Get-ChildItem (Join-Path $dotnetHome 'shared\Microsoft.NETCore.App') -Directory |
    Where-Object { $_.Name -like "$major.*" } | Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
if (-not $runtime) { throw ".NET runtime $major was not found" }
$fxr = Get-ChildItem (Join-Path $dotnetHome 'host\fxr') -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $dist 'engine'), (Join-Path $dist 'app'), (Join-Path $dist 'data') | Out-Null

Copy-Item (Join-Path $engineSource 'Godot_v4.7.2-stable_mono_win64.exe') (Join-Path $dist 'engine\DeskCivEngine.exe')
Copy-Item (Join-Path $engineSource 'GodotSharp') (Join-Path $dist 'engine\GodotSharp') -Recurse

$runtimeTarget = Join-Path $dist "dotnet\shared\Microsoft.NETCore.App\$($runtime.Name)"
$fxrTarget = Join-Path $dist "dotnet\host\fxr\$($fxr.Name)"
New-Item -ItemType Directory -Force (Split-Path $runtimeTarget), (Split-Path $fxrTarget) | Out-Null
Copy-Item $runtime.FullName $runtimeTarget -Recurse
Copy-Item $fxr.FullName $fxrTarget -Recurse
Copy-Item (Join-Path $dotnetHome 'dotnet.exe') (Join-Path $dist 'dotnet\dotnet.exe')

$game = Join-Path $repo 'game'
foreach ($item in 'project.godot', 'assets', 'src', 'Main.tscn', 'DeskCiv.csproj', 'DeskCiv.sln') {
    $source = Join-Path $game $item
    if (Test-Path $source) { Copy-Item $source (Join-Path $dist "app\$item") -Recurse }
}
Get-ChildItem $game -File -Include *.tscn, *.tres, *.godot, *.svg -ErrorAction SilentlyContinue | Copy-Item -Destination (Join-Path $dist 'app') -Force
foreach ($folder in 'scenes') {
    $source = Join-Path $game $folder
    if (Test-Path $source) { Copy-Item $source (Join-Path $dist "app\$folder") -Recurse }
}
Get-ChildItem $game -File | Where-Object { $_.Extension -in '.tscn', '.tres', '.svg', '.import', '.uid' } | Copy-Item -Destination (Join-Path $dist 'app') -Force
$imported = Join-Path $game '.godot'
New-Item -ItemType Directory -Force (Join-Path $dist 'app\.godot\mono\temp\bin') | Out-Null
foreach ($item in 'imported', 'global_script_class_cache.cfg', 'uid_cache.bin', 'extension_list.cfg') {
    $source = Join-Path $imported $item
    if (Test-Path $source) { Copy-Item $source (Join-Path $dist "app\.godot\$item") -Recurse }
}
Copy-Item (Join-Path $imported 'mono\temp\bin\Debug') (Join-Path $dist 'app\.godot\mono\temp\bin\Debug') -Recurse

@'
@echo off
rem Starts DeskCiv as the desktop background. Quit from the system tray icon.
set "DOTNET_ROOT=%~dp0dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=0"
set "DESKCIV_DATA=%~dp0data"
start "" "%~dp0engine\DeskCivEngine.exe" --path "%~dp0app" %*
'@ | Set-Content (Join-Path $dist 'DeskCiv.cmd') -Encoding ascii

@'
@echo off
rem Closes DeskCiv. The world was saved a few minutes ago at most.
taskkill /IM DeskCivEngine.exe >nul 2>&1
'@ | Set-Content (Join-Path $dist 'Stop DeskCiv.cmd') -Encoding ascii

$size = (Get-ChildItem $dist -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Portable folder: $dist ($([math]::Round($size)) MB, .NET $($runtime.Name))"

