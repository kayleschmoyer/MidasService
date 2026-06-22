<#
Build-Installer.ps1

Builds the MidasTransferWorker MSI using the WiX v5 .NET tool (no machine-wide WiX install
and no heat.exe required). Publishes the worker first unless -PublishedFolder is supplied.

Prerequisites:
  - .NET 8 SDK on PATH (provides `dotnet`). The WiX v5 tool is installed automatically if missing.

Usage:
  # Publish + build in one step (framework-dependent):
  .\Build-Installer.ps1

  # Self-contained (bundles the .NET runtime, no runtime prereq on the target):
  .\Build-Installer.ps1 -SelfContained

  # Build from an already-published folder:
  .\Build-Installer.ps1 -PublishedFolder 'C:\Services\MidasTransferWorker' -OutputMsi 'C:\Temp\MidasTransferWorker.msi'
#>

param(
    [string]$PublishedFolder,
    [string]$OutputMsi = "MidasTransferWorker.msi",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..")
$projectPath = Join-Path $repoRoot "MidasTransferWorker"

# Publish the worker if a published folder was not supplied.
if (-not $PublishedFolder) {
    $PublishedFolder = Join-Path $projectPath "bin\$Configuration\publish"
    $sc = if ($SelfContained) { "true" } else { "false" }
    Write-Host "Publishing worker to $PublishedFolder (self-contained=$sc) ..."
    dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained $sc -o $PublishedFolder
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

if (-not (Test-Path (Join-Path $PublishedFolder "MidasTransferWorker.exe"))) {
    throw "Published executable not found in: $PublishedFolder"
}

# Ensure the WiX v5 tool is available. WiX v5 is the last release under the fully open license;
# v6/v7 require accepting the Open Source Maintenance Fee (OSMF) EULA, so we pin v5.
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    Write-Host "Installing WiX v5 .NET tool (global) ..."
    dotnet tool install --global wix --version 5.0.2
    if ($LASTEXITCODE -ne 0) { throw "Failed to install the WiX tool" }
}

# Register the Util extension (idempotent) - provides util:EventSource. Pin to match the v5 engine.
wix extension add -g WixToolset.Util.wixext/5.0.2 | Out-Null

Write-Host "Building MSI ..."
wix build (Join-Path $scriptDir "Product.wxs") `
    -d PublishedFolder="$PublishedFolder" `
    -ext WixToolset.Util.wixext `
    -o $OutputMsi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Write-Host "MSI created: $OutputMsi"
