# build.ps1 — Developer tool
# Recompiles GodotSharp.Relay.exe and its cross-platform siblings, then drops them
# into the plugin's bin/ folder.
#
# End users do NOT need to run this. Pre-built binaries are included in the repo.
# Run this only when you've made changes to src/Relay/ and need to update the executables.
#
# Usage:
#   .\build.ps1                          # Build all platforms (default)
#   .\build.ps1 -Platform win-x64        # Build one platform only
#   .\build.ps1 -Configuration Debug     # Debug build (all platforms)

param(
    [string]$Configuration = "Release",
    [ValidateSet("all","win-x64","linux-x64","osx-x64","osx-arm64")]
    [string]$Platform = "all"
)

$ErrorActionPreference = "Stop"

$RelayDir  = Join-Path $PSScriptRoot "src\Relay"
$PluginBin = Join-Path $PSScriptRoot "src\Plugin\addons\godotsharp_mcp\bin"

# Map RID → final filename in the bin folder
$Targets = [ordered]@{
    "win-x64"    = "GodotSharp.Relay.exe"
    "linux-x64"  = "GodotSharp.Relay.linux"
    "osx-x64"    = "GodotSharp.Relay.osx-x64"
    "osx-arm64"  = "GodotSharp.Relay.osx-arm64"
}

$Selected = if ($Platform -eq "all") { $Targets.Keys } else { @($Platform) }

foreach ($rid in $Selected) {
    $OutputName = $Targets[$rid]
    $TmpOut     = Join-Path $PSScriptRoot "src\Relay\_publish_$rid"

    Write-Host "==> Building $rid ($Configuration)..." -ForegroundColor Cyan

    dotnet publish "$RelayDir\GodotSharp.Relay.csproj" `
        -c $Configuration `
        -r $rid `
        --no-self-contained `
        -p:PublishSingleFile=true `
        -o $TmpOut `
        --nologo -v quiet

    if ($LASTEXITCODE -ne 0) { Write-Error "Build failed for $rid."; exit 1 }

    # The published exe always has the assembly name (GodotSharp.Relay / GodotSharp.Relay.exe).
    # Move it to the canonical platform-specific filename in the plugin bin folder.
    $builtExe = if ($rid -like "win*") {
        Join-Path $TmpOut "GodotSharp.Relay.exe"
    } else {
        Join-Path $TmpOut "GodotSharp.Relay"
    }

    $dest = Join-Path $PluginBin $OutputName
    Copy-Item -Force $builtExe $dest
    Remove-Item -Recurse -Force $TmpOut

    Write-Host "    => $dest" -ForegroundColor Green
}

Write-Host ""
Write-Host "==> Done." -ForegroundColor Green
