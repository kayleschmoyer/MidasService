<#
Build-Installer.ps1
Builds an MSI using WiX Toolset v3.x (requires WiX installed).

This script expects the project to be published first. It uses heat.exe to harvest the published output
and then compiles the WiX sources to an MSI.

Usage:
  .\Build-Installer.ps1 -PublishedFolder "C:\Services\MidasTransferWorker"

Parameters:
  -PublishedFolder: the folder containing the published app (contains MidasTransferWorker.exe and DLLs)
  -OutputMsi: optional path for the resulting MSI
#>

param(
	[Parameter(Mandatory=$true)]
	[string]$PublishedFolder,
	[string]$OutputMsi = "MidasTransferWorker.msi"
)

Set-StrictMode -Version Latest

# Locate WiX bin - try typical install locations or PATH
$possible = @(
	"$env:ProgramFiles(x86)\WiX Toolset v3.14\bin",
	"$env:ProgramFiles(x86)\WiX Toolset v3.11\bin",
	"$env:ProgramFiles\WiX Toolset v3.14\bin",
	"$env:ProgramFiles\WiX Toolset v3.11\bin"
)

$wixBin = $null
foreach ($p in $possible) {
	if (Test-Path $p) { $wixBin = $p; break }
}

if (-not $wixBin) {
	# try to find heat.exe on PATH
	$heatCmd = Get-Command heat.exe -ErrorAction SilentlyContinue
	if ($heatCmd) {
		$wixBin = Split-Path $heatCmd.Path
	}
}

if (-not $wixBin) {
	Write-Error "WiX bin not found. Ensure WiX Toolset is installed and heat.exe is on PATH."
	exit 1
}

$heat = Join-Path $wixBin "heat.exe"
$candle = Join-Path $wixBin "candle.exe"
$light = Join-Path $wixBin "light.exe"

if (-not (Test-Path $heat)) { Write-Error "heat.exe not found in $wixBin"; exit 1 }
if (-not (Test-Path $candle)) { Write-Error "candle.exe not found in $wixBin"; exit 1 }
if (-not (Test-Path $light)) { Write-Error "light.exe not found in $wixBin"; exit 1 }

# prepare working folder
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
Push-Location $scriptDir

$harvestOut = "Components.wxs"
if (Test-Path $harvestOut) { Remove-Item $harvestOut -Force }

# Exclude the main exe (we include it explicitly in Product.wxs)
$exclude = "MidasTransferWorker.exe"

Write-Output "Harvesting published folder: $PublishedFolder"
& $heat dir `"$PublishedFolder`" -gg -sfrag -scom -sreg -dr MIDASFOLDER -cg AppComponents -var var.PublishedFolder -out $harvestOut -xf $exclude

if ($LASTEXITCODE -ne 0) { Write-Error "heat failed"; Pop-Location; exit 1 }

# Compile WiX sources
Write-Output "Compiling WiX sources..."
& $candle Product.wxs $harvestOut -dPublishedFolder=`"$PublishedFolder`"
if ($LASTEXITCODE -ne 0) { Write-Error "candle failed"; Pop-Location; exit 1 }

# Link to MSI
Write-Output "Linking MSI..."
& $light -ext WixUtilExtension -ext WixUIExtension Product.wixobj Components.wixobj -out $OutputMsi
if ($LASTEXITCODE -ne 0) { Write-Error "light failed"; Pop-Location; exit 1 }

Write-Output "MSI created: $OutputMsi"
Pop-Location

*** End Patch