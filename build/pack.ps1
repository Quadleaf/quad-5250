<#
.SYNOPSIS
Build the Quad 5250 installer: the desktop client and the MCP server in one
Velopack package, so one install (and every update) covers both.

.EXAMPLE
./build/pack.ps1 -Version 1.0.0 -UpdateFeedUrl https://example.blob.core.windows.net/quad5250

.NOTES
Output in artifacts/releases: Quad5250-win-Setup.exe for users, plus the
nupkg/releases.win.json files that go to the update feed. Pack each new
version into the same folder (or download the previous release there
first) so Velopack can build a small delta update.
Requires the vpk tool: dotnet tool install -g vpk --version 1.2.161
#>
param(
    [Parameter(Mandatory)][string]$Version,
    # Where installed copies look for updates. Empty: the build never checks.
    [string]$UpdateFeedUrl = "",
    [string]$OutputDir = "artifacts/releases",
    # Passed to signtool when a signing certificate is available, e.g.
    # '/fd sha256 /tr http://timestamp.digicert.com /td sha256 /a'.
    [string]$SignParams = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root "artifacts/publish"
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }

# Both apps are self-contained for the same runtime, so publishing them into
# one folder shares the runtime files rather than shipping them twice.
foreach ($project in "src/Quad5250.Desktop", "src/Quad5250.Mcp") {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained `
        -p:Version=$Version -p:UpdateFeedUrl=$UpdateFeedUrl -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
}

$packArgs = @(
    "pack",
    "--packId", "Quad5250",
    "--packVersion", $Version,
    "--packDir", $publish,
    "--mainExe", "Quad5250.Desktop.exe",
    "--packTitle", "Quad 5250",
    "--packAuthors", "Quadleaf",
    "--shortcuts", "Desktop,StartMenuRoot",
    "--outputDir", (Join-Path $root $OutputDir)
)
if ($SignParams) { $packArgs += @("--signParams", $SignParams) }

vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
