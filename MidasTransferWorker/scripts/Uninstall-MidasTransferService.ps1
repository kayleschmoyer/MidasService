<#
Uninstall-MidasTransferService.ps1

Removes the Windows Service, EventLog source, and optionally installation folders created by Install-MidasTransferService.ps1

Run elevated (Administrator). Usage examples:
  .\Uninstall-MidasTransferService.ps1
  .\Uninstall-MidasTransferService.ps1 -ServiceName 'MidasTransferWorker' -RemoveFiles -Force
#>

param(
	[string]$ServiceName = 'MidasTransferWorker',
	[switch]$RemoveFiles,
	[switch]$Force
)

function Ensure-RunAsAdmin {
	$current = [Security.Principal.WindowsIdentity]::GetCurrent()
	$principal = New-Object Security.Principal.WindowsPrincipal($current)
	if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
		Write-Error "This script must be run as Administrator."
		exit 1
	}
}

Ensure-RunAsAdmin

Write-Output "Uninstalling service: $ServiceName"

function Service-Exists($name) {
	$svc = Get-Service -Name $name -ErrorAction SilentlyContinue
	return $null -ne $svc
}

if (Service-Exists $ServiceName) {
	try {
		$svc = Get-Service -Name $ServiceName -ErrorAction Stop
		if ($svc.Status -ne 'Stopped') {
			Write-Output "Stopping service $ServiceName..."
			Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
			# wait for stop
			$wait = 0
			while ((Get-Service -Name $ServiceName).Status -ne 'Stopped' -and $wait -lt 30) {
				Start-Sleep -Seconds 1
				$wait++
			}
		}
	}
	catch {
		Write-Warning "Failed to stop service or it was already stopped: $_"
	}

	try {
		Write-Output "Deleting service $ServiceName..."
		sc.exe delete $ServiceName | Out-Null
		Start-Sleep -Seconds 1
	}
	catch {
		Write-Warning "Failed to delete service: $_"
	}
}
else {
	Write-Output "Service $ServiceName not found."
}

# Remove EventLog source if present
$eventSource = 'MidasTransferWorker'
try {
	if ([System.Diagnostics.EventLog]::SourceExists($eventSource)) {
		Write-Output "Deleting EventLog source: $eventSource"
		[System.Diagnostics.EventLog]::DeleteEventSource($eventSource)
	}
	else {
		Write-Output "EventLog source not found: $eventSource"
	}
}
catch {
	Write-Warning "Failed to delete EventLog source: $_"
}

if ($RemoveFiles) {
	$programFilesX86 = ${env:ProgramFiles(x86)}
	$installBase = Join-Path $programFilesX86 'MAM Software\MidasTransferService'
	$logDir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'MidasTransferService'

	if (-not $Force) {
		$confirm = Read-Host "Remove folders '$installBase' and '$logDir' and all contents? Type YES to confirm"
		if ($confirm -ne 'YES') {
			Write-Output "Aborting folder removal. Use -Force to skip confirmation."
			exit 0
		}
	}

	try {
		if (Test-Path $installBase) {
			Write-Output "Removing $installBase..."
			Remove-Item -Path $installBase -Recurse -Force -ErrorAction SilentlyContinue
		}
		if (Test-Path $logDir) {
			Write-Output "Removing $logDir..."
			Remove-Item -Path $logDir -Recurse -Force -ErrorAction SilentlyContinue
		}
	}
	catch {
		Write-Warning "Failed to remove folders: $_"
	}
}

Write-Output "Uninstall script completed."
