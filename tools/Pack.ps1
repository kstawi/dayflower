# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Builds Dayflower's installer and update packages with Velopack, for one kind of processor:
# <PackId>-<runtime>-Setup.exe, the full and delta update packages, and releases.<runtime>.json, in .\releases.
# Packing a newer version into the same folder adds a delta update from the previous one.
#
#   .\tools\Pack.ps1 -Version 0.7.0
#   .\tools\Pack.ps1 -Version 0.7.0 -Runtime win-arm64
#   .\tools\Pack.ps1 -Version 0.7.0 -PackId DayflowerTest     a test install: its own folder, data and start-up
#                                                             entry, so it never touches a real one
#   -ReleaseNotes notes.md                                    shown with the update

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [string] $PackId = 'Dayflower',
    [string] $OutputDir,
    [string] $ReleaseNotes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $root 'releases' }
$platform = if ($Runtime -eq 'win-arm64') { 'ARM64' } else { 'x64' }
$title = if ($PackId -eq 'Dayflower') { 'Dayflower' } else { "Dayflower ($PackId)" }
$publish = Join-Path $root "artifacts\publish\$Runtime"

# The app with the .NET runtime and the Windows App SDK inside, so nothing has to be installed first.
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\Dayflower.App\Dayflower.App.csproj') -c Release -r $Runtime -p:Platform=$platform `
    --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Each processor type is its own update channel, so an x64 copy only ever downloads x64 updates.
Push-Location $root
try {
    dotnet tool restore | Out-Null
    $notes = if ($ReleaseNotes) { @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) } else { @() }
    dotnet vpk pack --packId $PackId --packVersion $Version --packDir $publish --mainExe Dayflower.exe `
        --packTitle $title --packAuthors kstawi --icon (Join-Path $root 'src\Dayflower.App\Assets\app.ico') `
        --runtime $Runtime --channel $Runtime --shortcuts StartMenuRoot --noPortable --outputDir $OutputDir @notes
    if ($LASTEXITCODE -ne 0) { throw 'Packing failed.' }
}
finally {
    Pop-Location
}

Get-ChildItem $OutputDir | Select-Object Name, @{ n = 'MB'; e = { [Math]::Round($_.Length / 1MB, 1) } }
