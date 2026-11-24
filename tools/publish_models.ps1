# Copies new or changed generated models from the project to the portable build.
# A running DeskCiv watches its assets/models folder and swaps the models in about 2 seconds, without a restart.
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "game\assets\models"
$target = Join-Path $root "dist\DeskCiv\app\assets\models"
if (-not (Test-Path $target)) { "No portable build at $target; nothing to publish."; return }
$copied = 0
foreach ($file in Get-ChildItem $source -File) {
    $dest = Join-Path $target $file.Name
    if (-not (Test-Path $dest) -or (Get-Item $dest).LastWriteTimeUtc -lt $file.LastWriteTimeUtc -or (Get-Item $dest).Length -ne $file.Length) {
        Copy-Item $file.FullName $dest -Force
        $copied++
    }
}
"Published $copied model file(s) to the portable build."
