<#
.SYNOPSIS
  Builds xelisten release packages: zip, tar.gz, MSI, Chocolatey nupkg and winget manifests.
.DESCRIPTION
  The version is read from the root version.json. Output goes to -OutputDir (default: artifacts/packages).
  Windows runtimes get a zip (plus MSI); Linux/macOS runtimes get a tar.gz.
  Requires: dotnet SDK, tar, and on Windows: WiX (installed as a dotnet tool if missing) and choco (for the nupkg).
.PARAMETER BaseUrl
  Download URL prefix used in the winget installer manifest, e.g.
  https://github.com/shueybubbles/xevent-mcp/releases/download/v1.0.0
#>
[CmdletBinding()]
param(
  [string]$OutputDir = (Join-Path $PSScriptRoot '..\artifacts\packages'),
  [string]$Configuration = 'Release',
  [string]$BaseUrl,
  [string[]]$Rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'linux-musl-x64', 'osx-arm64')
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\Cli\XEListen.csproj'

# version.json uses "major.minor"; pad to a three-part version for MSI/choco/winget
$raw = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version -replace '-.*$', ''
$parts = @($raw.Split('.'))
while ($parts.Count -lt 3) { $parts += '0' }
$version = ($parts[0..2]) -join '.'
if (-not $BaseUrl) { $BaseUrl = "https://github.com/shueybubbles/xevent-mcp/releases/download/v$version" }
Write-Host "Packaging xelisten $version"

$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$work = Join-Path $OutputDir '_work'
Remove-Item $OutputDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null

& (Join-Path $PSScriptRoot 'build-notice.ps1')
if ($LASTEXITCODE) { throw 'build-notice failed' }

$publishDirs = @{}
foreach ($rid in $Rids) {
  $dir = Join-Path $work "publish\$rid"
  dotnet publish $project -c $Configuration -r $rid --self-contained -o $dir "-p:Version=$version" "-p:PublishSingleFile=true"
  if ($LASTEXITCODE) { throw "publish failed for $rid" }
  $publishDirs[$rid] = $dir
}

# zip for Windows RIDs, tar.gz for Linux/macOS RIDs
foreach ($rid in $Rids) {
  $dir = $publishDirs[$rid]
  $name = "xelisten-$version-$rid"
  if ($rid -like 'win-*') {
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath (Join-Path $OutputDir "$name.zip") -Force
  } else {
    tar -czf (Join-Path $OutputDir "$name.tar.gz") -C $dir .
    if ($LASTEXITCODE) { throw "tar failed for $rid" }
  }
}

$onWindows = $IsWindows -or $PSVersionTable.PSEdition -eq 'Desktop'

# MSI (Windows only)
$msis = @{}
if ($onWindows) {
  if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    dotnet tool install --global wix --version 5.0.2
    $env:PATH += [IO.Path]::PathSeparator + (Join-Path $HOME '.dotnet\tools')
  }
  foreach ($rid in $Rids | Where-Object { $_ -like 'win-*' }) {
    $arch = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $msi = Join-Path $OutputDir "xelisten-$version-$rid.msi"
    wix build (Join-Path $root 'packaging\xelisten.wxs') -arch $arch -d "Version=$version" -d "SourceDir=$($publishDirs[$rid])" -pdbtype none -o $msi
    if ($LASTEXITCODE) { throw "wix failed for $rid" }
    $msis[$rid] = $msi
  }
} else {
  Write-Warning 'Skipping MSI: requires Windows.'
}

# Chocolatey: embeds the win-x64 exe; choco shims it onto PATH automatically
if ($onWindows -and $publishDirs.ContainsKey('win-x64')) {
  $choco = Join-Path $work 'choco'
  New-Item -ItemType Directory -Force (Join-Path $choco 'tools') | Out-Null
  Copy-Item (Join-Path $publishDirs['win-x64'] 'xelisten.exe') (Join-Path $choco 'tools')
  Copy-Item (Join-Path $publishDirs['win-x64'] 'NOTICE.txt') (Join-Path $choco 'tools')
  (Get-Content (Join-Path $root 'packaging\chocolatey\xelisten.nuspec') -Raw).Replace('$version$', $version) |
    Set-Content (Join-Path $choco 'xelisten.nuspec') -Encoding utf8
  if (Get-Command choco -ErrorAction SilentlyContinue) {
    choco pack (Join-Path $choco 'xelisten.nuspec') --outputdirectory $OutputDir
    if ($LASTEXITCODE) { throw 'choco pack failed' }
  } else {
    Write-Warning 'choco not found; skipping Chocolatey package.'
  }
}

# winget manifests (multi-file, schema 1.6.0) referencing the released MSIs
if ($msis.Count) {
  $wg = Join-Path $OutputDir "winget\manifests\s\shueybubbles\xelisten\$version"
  New-Item -ItemType Directory -Force $wg | Out-Null
  $id = 'shueybubbles.xelisten'
  $installers = foreach ($rid in $msis.Keys | Sort-Object) {
    $arch = if ($rid -eq 'win-arm64') { 'arm64' } else { 'x64' }
    $file = Split-Path $msis[$rid] -Leaf
    $hash = (Get-FileHash $msis[$rid] -Algorithm SHA256).Hash
    "- Architecture: $arch`n  InstallerType: wix`n  Scope: machine`n  InstallerUrl: $BaseUrl/$file`n  InstallerSha256: $hash"
  }
  $header = "PackageIdentifier: $id`nPackageVersion: $version`n"
  Set-Content (Join-Path $wg "$id.yaml") -Encoding utf8 -Value ($header +
    "DefaultLocale: en-US`nManifestType: version`nManifestVersion: 1.6.0`n")
  Set-Content (Join-Path $wg "$id.installer.yaml") -Encoding utf8 -Value ($header +
    "Commands:`n- xelisten`nInstallers:`n" + ($installers -join "`n") + "`nManifestType: installer`nManifestVersion: 1.6.0`n")
  Set-Content (Join-Path $wg "$id.locale.en-US.yaml") -Encoding utf8 -Value ($header + @"
PackageLocale: en-US
Publisher: shueybubbles
PackageName: xelisten
PackageUrl: https://github.com/shueybubbles/xevent-mcp
License: MIT
ShortDescription: Command line app that reads SQL Server extended events from xel files and session targets.
Tags:
- sql-server
- extended-events
- xevents
- cli
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@)
}

Remove-Item $work -Recurse -Force
Get-ChildItem $OutputDir -Recurse -File | ForEach-Object { Write-Host $_.FullName }
