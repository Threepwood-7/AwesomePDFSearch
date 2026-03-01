$ErrorActionPreference = "Stop"

$releaseDir = Join-Path $PSScriptRoot "bin\Release"
$binDir = Join-Path $PSScriptRoot "bin"
$datestamp = Get-Date -Format "yyyyMMdd"
$zipName = "AwesomePDFSearch_release_$datestamp.zip"
$zipPath = Join-Path $binDir $zipName

if (-not (Test-Path $releaseDir)) {
    Write-Error "bin\Release not found. Run rebuild.cmd first."
    exit 1
}

$tempDir = Join-Path $env:TEMP "AwesomePDFSearch_pack_$datestamp"
$innerDir = Join-Path $tempDir "AwesomePDFSearch"

if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $innerDir | Out-Null

Copy-Item "$releaseDir\*" $innerDir -Recurse

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -Path $innerDir -DestinationPath $zipPath

Remove-Item $tempDir -Recurse -Force

Write-Host "Created: $zipPath"
