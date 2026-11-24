# Runs the character pipeline for roles whose T-pose concept is already saved.
# Usage: tools\build_roles.ps1 woman builder baker
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Roles)

$root = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $root
$env:PYTHONIOENCODING = "utf-8"
$python = ".tools\hunyuan3d-env\python.exe"
$blender = ".tools\blender\Blender Foundation\Blender 5.3\blender.exe"
$godot = ".tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"

foreach ($role in $Roles) {
    $version = "$role-v1"
    "=== $role"
    & $python -I tools\prepare_tpose_views.py $role $version 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match 'Expected|Error' }
    if ($LASTEXITCODE -ne 0) { "FAILED prepare $role"; continue }
    & $python tools\generate_hunyuan_shape.py $version 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match 'side-as-right|Traceback|Error' }
    if ($LASTEXITCODE -ne 0) { "FAILED shape $role"; continue }
    & $python tools\build_tpose_character.py $version 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match 'BUILD_OK|Traceback|Error' }
    if ($LASTEXITCODE -ne 0) { "FAILED build $role"; continue }
    & $blender -b --factory-startup --python tools\rig_ual_character.py -- $version 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -match 'GAME_EXPORT|RIG_FAILED' }
    if ($LASTEXITCODE -ne 0) { "FAILED rig $role"; continue }
    New-Item -ItemType Directory -Force "assets-candidates\hunyuan3d\$version\review" | Out-Null
    & $godot --path tools\character-review --resolution 480x270 --audio-driver Dummy res://clips.tscn -- $version 2>&1 | Select-String -Pattern 'CLIPS_SAVED|LOAD_FAILED|NO_CLIP|SCRIPT ERROR' | ForEach-Object Line
}
"ROLES_DONE"
