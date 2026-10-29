<#
.SYNOPSIS
  Generates NOTICE.txt (third-party OSS license notices) for the CLI and the MCP server.
.DESCRIPTION
  Uses the ThirdLicense dotnet tool (restored from the root dotnet-tools.json) to walk the transitive
  NuGet dependencies of each shipped project. The output is written next to each project and is packed
  into the NuGet packages and published with the xelisten release packages.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Push-Location $root
try {
  dotnet tool restore
  if ($LASTEXITCODE) { throw 'dotnet tool restore failed' }
  foreach ($project in 'src\Cli\XEListen.csproj', 'src\Server\Bubbles.XEvent.MCPServer.csproj') {
    $out = Join-Path (Split-Path (Join-Path $root $project)) 'NOTICE.txt'
    dotnet tool run thirdlicense -- --project (Join-Path $root $project) --output $out
    if ($LASTEXITCODE) { throw "thirdlicense failed for $project" }
    Write-Host "Wrote $out"
  }
} finally {
  Pop-Location
}
