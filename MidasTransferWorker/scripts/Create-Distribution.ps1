<#
.SYNOPSIS
	Create a distributable ZIP for MidasTransferWorker including publish output and installer scripts.

USAGE
	Run from repository root in an elevated PowerShell (not strictly required):
	  .\MidasTransferWorker\scripts\Create-Distribution.ps1

	Options:
	  -Configuration Release
	  -Runtime win-x64
	  -SelfContained $false

This script will:
  - dotnet publish the worker project
  - copy publish output plus appsettings.json, install/uninstall scripts and README into a timestamped folder under Releases\MidasTransferWorker
  - create a zip file ready to hand to an operator for install
#>

param(
	[string]$ProjectPath = "MidasTransferWorker",
	[string]$Configuration = "Release",
	[string]$Runtime = "win-x64",
	[bool]$SelfContained = $false
)

Set-StrictMode -Version Latest

Write-Output "Creating distribution for project: $ProjectPath"

$pubArgs = "-c $Configuration"
if ($Runtime) { $pubArgs += " -r $Runtime" }
if ($SelfContained) { $pubArgs += " --self-contained true" } else { $pubArgs += " --self-contained false" }

$publishDir = Join-Path -Path (Resolve-Path $ProjectPath).Path -ChildPath "bin\$Configuration\publish"

Write-Output "Publishing project..."
dotnet publish $ProjectPath -c $Configuration -r $Runtime --self-contained:$SelfContained -o $publishDir

if (-not (Test-Path $publishDir)) {
	Write-Error "Publish failed or output not found: $publishDir"
	exit 1
}

$timestamp = (Get-Date).ToString('yyyyMMdd_HHmmss')
$distRoot = Join-Path -Path (Get-Location) -ChildPath "Releases\MidasTransferWorker\$timestamp"
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

Write-Output "Copying publish output to distribution folder..."
Copy-Item -Path (Join-Path $publishDir '*') -Destination $distRoot -Recurse -Force

# Copy configuration and helper scripts
$extras = @(
	'MidasTransferWorker\appsettings.json',
	'MidasTransferWorker\README.md',
	'MidasTransferWorker\scripts\Install-MidasTransferService.ps1',
	'MidasTransferWorker\scripts\Uninstall-MidasTransferService.ps1'
)

foreach ($f in $extras) {
	if (Test-Path $f) {
		Copy-Item -Path $f -Destination $distRoot -Force
	}
}

# Create a small install-instructions file in the distribution folder
$instructions = @(
	"MidasTransferWorker Distribution",
	"Published: $timestamp",
	'',
	'To install:',
	' 1. Extract this archive to a folder, e.g. C:\Services\MidasTransferWorker',
	' 2. Run the included Install-MidasTransferService.ps1 as Administrator and point -ExePath to the EXE in this folder',
	' 3. Start the service: Start-Service -Name MidasTransferWorker',
	'',
	'To uninstall:',
	'  - Run Uninstall-MidasTransferService.ps1 as Administrator (optionally with -RemoveFiles -Force)'
)
$instructions | Out-File -FilePath (Join-Path $distRoot 'INSTALLATION.txt') -Encoding UTF8

# Create ZIP
$zipName = "MidasTransferWorker_$timestamp.zip"
$zipPath = Join-Path -Path (Join-Path (Get-Location) 'Releases\MidasTransferWorker') -ChildPath $zipName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Output "Creating zip: $zipPath"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($distRoot, $zipPath)

Write-Output "Distribution created: $zipPath"
Write-Output "Distribution folder: $distRoot"

exit 0
