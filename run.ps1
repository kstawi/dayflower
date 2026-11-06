# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Builds Dayflower and starts it, as the plain program the installer ships.
#
#   .\run.ps1                         Debug build for this PC's processor
#   .\run.ps1 -Configuration Release
#   .\run.ps1 -NoLaunch               build without starting
#   .\run.ps1 -Packaged               build the MSIX package instead and install it for the current user as a
#                                     development package (needs Developer Mode: Settings > System > Advanced >
#                                     For developers)

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoLaunch,
    [switch] $Packaged
)

$ErrorActionPreference = 'Stop'
$packageName = 'kstawi.Dayflower'
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$appDir = Join-Path $PSScriptRoot 'src\Dayflower.App'
$outDir = Join-Path $appDir "bin\$platform\$Configuration"

if ($Packaged) {
    $devMode = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
    if ($devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
        throw 'Developer Mode is off. Switch it on in Settings > System > Advanced > For developers, then run this again.'
    }
}

# A copy running from this build's folder locks its files, so stop it before rebuilding. (Closing its window only
# hides it; it keeps running in the background.)
Get-Process -Name Dayflower -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$outDir\*" } |
    ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }

$msix = if ($Packaged) { 'true' } else { 'false' }
dotnet build (Join-Path $appDir 'Dayflower.App.csproj') -c $Configuration -p:Platform=$platform -p:PackageAsMsix=$msix
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

if (-not $Packaged) {
    $exe = Get-ChildItem $outDir -Recurse -Filter Dayflower.exe |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    Write-Host "Built $($exe.FullName)"
    if (-not $NoLaunch) {
        Start-Process $exe.FullName
    }

    return
}

# The packaged build leaves an unpacked package (AppxManifest.xml next to the app's files) in the output folder.
# Registering it installs the app for the current user, the same way Visual Studio's Deploy does.
$manifest = Get-ChildItem $outDir -Recurse -Filter AppxManifest.xml |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $manifest) { throw 'No AppxManifest.xml found in the build output.' }

Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
Write-Host "Installed $packageName from $($manifest.DirectoryName)"

if (-not $NoLaunch) {
    $package = Get-AppxPackage -Name $packageName
    Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
}
