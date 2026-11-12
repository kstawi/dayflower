# Dayflower - https://github.com/kstawi/dayflower
# Copyright (C) 2026 kstawi
# Licensed under the GNU General Public License v3.0; see the LICENSE file.
#
# Builds the homepage into _site: the pages as they are, plus privacy.html with PRIVACY.md rendered into it by
# GitHub's Markdown API, so the privacy policy has one source. Needs the GitHub CLI (gh), signed in.
#
#   ./site/build.ps1            (then open _site/index.html)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$out = Join-Path $root '_site'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item $out -ItemType Directory | Out-Null
Get-ChildItem $PSScriptRoot -File | Where-Object { $_.Name -ne 'build.ps1' } | Copy-Item -Destination $out

$utf8 = New-Object Text.UTF8Encoding $false
[Console]::OutputEncoding = $utf8
$request = Join-Path $out 'privacy-request.json'
$json = @{ text = [IO.File]::ReadAllText((Join-Path $root 'PRIVACY.md'), $utf8); mode = 'markdown' } | ConvertTo-Json
[IO.File]::WriteAllText($request, $json, $utf8)
$policy = (gh api markdown --input $request) -join "`n"
if ($LASTEXITCODE -ne 0 -or -not $policy) { throw 'GitHub could not render PRIVACY.md.' }
Remove-Item $request

$page = Join-Path $out 'privacy.html'
$html = [IO.File]::ReadAllText($page, $utf8)
$html = [regex]::Replace($html, '<!-- PRIVACY:.*?-->', { param($m) $policy })
[IO.File]::WriteAllText($page, $html, $utf8)
"Built $out"
