<#
.SYNOPSIS
  Publishes the Chocolatey package and winget manifests produced by build-packages.ps1.
.PARAMETER PackagesDir
  Directory produced by build-packages.ps1.
.PARAMETER ChocolateyApiKey
  API key for community.chocolatey.org. Omit to skip Chocolatey.
.PARAMETER GitHubToken
  PAT with public_repo scope used by wingetcreate to open a PR against microsoft/winget-pkgs. Omit to skip winget.
#>
[CmdletBinding()]
param(
  [string]$PackagesDir = (Join-Path $PSScriptRoot '..\artifacts\packages'),
  [string]$ChocolateyApiKey = $env:CHOCOLATEY_API_KEY,
  [string]$GitHubToken = $env:WINGET_PAT,
  [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'
$PackagesDir = (Resolve-Path $PackagesDir).Path

if ($ChocolateyApiKey) {
  $nupkg = Get-ChildItem $PackagesDir -Filter 'xelisten.*.nupkg' | Select-Object -First 1
  if (-not $nupkg) { throw "No xelisten nupkg found in $PackagesDir" }
  Write-Host "Pushing $($nupkg.Name) to Chocolatey"
  if (-not $WhatIf) {
    choco push $nupkg.FullName --source https://push.chocolatey.org/ --api-key $ChocolateyApiKey
    if ($LASTEXITCODE) { throw 'choco push failed' }
  }
} else { Write-Warning 'No Chocolatey API key; skipping Chocolatey.' }

if ($GitHubToken) {
  $manifestDir = Get-ChildItem (Join-Path $PackagesDir 'winget\manifests') -Recurse -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName '*.installer.yaml') } | Select-Object -First 1
  if (-not $manifestDir) { throw "No winget manifests found in $PackagesDir" }
  Write-Host "Submitting $($manifestDir.FullName) to microsoft/winget-pkgs"
  if (-not $WhatIf) {
    $wc = Join-Path ([IO.Path]::GetTempPath()) 'wingetcreate.exe'
    if (-not (Test-Path $wc)) { Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile $wc }
    & $wc submit --token $GitHubToken $manifestDir.FullName
    if ($LASTEXITCODE) { throw 'wingetcreate submit failed' }
  }
} else { Write-Warning 'No GitHub token; skipping winget.' }
