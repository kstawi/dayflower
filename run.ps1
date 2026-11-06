# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Builds Dayflower, installs it for the current user as a development package and starts it.
# Installing an unsigned development package needs Developer Mode
# (Settings > System > Advanced > For developers).
#
#   .\run.ps1                         Debug build for this PC's processor
#   .\run.ps1 -Configuration Release
#   .\run.ps1 -NoLaunch               build and install without starting

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
$packageName = 'kstawi.Dayflower'
$platform = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'ARM64' } else { 'x64' }
$appDir = Join-Path $PSScriptRoot 'src\Dayflower.App'

$devMode = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if ($devMode.AllowDevelopmentWithoutDevLicense -ne 1) {
    throw 'Developer Mode is off. Switch it on in Settings > System > Advanced > For developers, then run this again.'
}

# The installed development build runs straight from the build output, so stop it before rebuilding. (Closing
# its window only hides it to the notification area.)
Get-Process -Name Dayflower -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like "$appDir\*" } |
    ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }

dotnet build (Join-Path $appDir 'Dayflower.App.csproj') -c $Configuration -p:Platform=$platform
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# The build leaves an unpacked package (AppxManifest.xml next to the app's files) in the output folder.
# Registering it installs the app for the current user, the same way Visual Studio's Deploy does.
$manifest = Get-ChildItem (Join-Path $appDir "bin\$platform\$Configuration") -Recurse -Filter AppxManifest.xml |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if (-not $manifest) { throw 'No AppxManifest.xml found in the build output.' }

Add-AppxPackage -Register $manifest.FullName -ForceApplicationShutdown
Write-Host "Installed $packageName from $($manifest.DirectoryName)"

if (-not $NoLaunch) {
    $package = Get-AppxPackage -Name $packageName
    Start-Process "shell:AppsFolder\$($package.PackageFamilyName)!App"
}
