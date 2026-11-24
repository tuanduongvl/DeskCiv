# Builds one realistic building model from a 2x2 orthographic sheet.
# Usage: tools\make_building.ps1 -Sheet assets-candidates\concepts\buildings\hut-t2.png -Name hut_t2 -Height 7 [-Faces 4000] [-Yaw 0] [-SwapSides]
# Steps: cut views, Hunyuan3D-2mv shape (both side labels), pick the best outline, decimate, unwrap, project texture.
param(
    [Parameter(Mandatory)][string]$Sheet,
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][double]$Height,
    [int]$Faces = 4000,
    [double]$Yaw = 0,
    [switch]$SwapSides
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$version = ($Name -replace '_', '-') + "-v1"
$py = "$root\.tools\hunyuan3d-env\python.exe"
$cut = @("-I", "tools\prepare_grid_views.py", $Sheet, $version)
if ($SwapSides) { $cut += "--swap-sides" }
& python @cut | Select-Object -Last 1
& $py tools\generate_hunyuan_shape.py $version 2>&1 | Select-String "seconds=" | ForEach-Object { $_.Line.Substring(0, [Math]::Min(60, $_.Line.Length)) }
& $py tools\build_hunyuan_model.py $version $Height $Faces $Name $Yaw 2>&1 | Select-String "IoU|BUILD_OK" | ForEach-Object { $_.Line }
& (Join-Path $PSScriptRoot "publish_models.ps1")
