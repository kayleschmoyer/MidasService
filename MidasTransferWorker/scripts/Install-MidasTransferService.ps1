<#
Install-MidasTransferService.ps1

Creates installation folders, grants ACLs, creates EventLog source, and registers the Windows Service.

Run elevated (Administrator). Usage:
.
  .\Install-MidasTransferService.ps1 -ExePath 'C:\Services\MidasTransferWorker\MidasTransferWorker.exe' 

Optional parameters: -ServiceName, -DisplayName, -ServiceAccount (LocalSystem|LocalService|NetworkService|"DOMAIN\\User"), -ServicePassword
#>

param(
	[Parameter(Mandatory=$true)]
	[string]$ExePath,

	[string]$ServiceName = 'MidasTransferWorker',
	[string]$DisplayName = 'Midas Transfer Worker',
	[string]$ServiceAccount = 'LocalSystem',
	[string]$ServicePassword = ''
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

if (-not (Test-Path $ExePath)) {
	Write-Error "Executable not found: $ExePath"
	exit 1
}

$programFilesX86 = ${env:ProgramFiles(x86)}
$installBase = Join-Path $programFilesX86 'MAM Software\MidasTransferService'
$processed = Join-Path $installBase 'Processed'
$quarantine = Join-Path $installBase 'Quarantine'
$logDir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'MidasTransferService\logs'

# create directories
New-Item -ItemType Directory -Path $installBase -Force | Out-Null
New-Item -ItemType Directory -Path $processed -Force | Out-Null
New-Item -ItemType Directory -Path $quarantine -Force | Out-Null
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

Write-Output "Created folders under: $installBase and logs at $logDir"

# Grant Modify rights to the service account on install folders
function Grant-Modify($path, $account) {
	try {
		$acl = Get-Acl -Path $path
		$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($account, "Modify", "ContainerInherit, ObjectInherit", "None", "Allow")
		$acl.SetAccessRule($rule)
		Set-Acl -Path $path -AclObject $acl
		Write-Output "Granted Modify to $account on $path"
	}
	catch {
		Write-Warning "Failed to set ACL for $account on $path: $_"
	}
}

# map service account friendly names
switch ($ServiceAccount.ToLower()) {
	'localsystem' { $aclAccount = 'NT AUTHORITY\SYSTEM' }
	'localservice' { $aclAccount = 'NT AUTHORITY\LOCAL SERVICE' }
	'networkservice' { $aclAccount = 'NT AUTHORITY\NETWORK SERVICE' }
	default { $aclAccount = $ServiceAccount }
}

Grant-Modify -path $installBase -account $aclAccount
Grant-Modify -path $logDir -account $aclAccount

# Create EventLog source if missing
$eventSource = 'MidasTransferWorker'
try {
	if (-not [System.Diagnostics.EventLog]::SourceExists($eventSource)) {
		[System.Diagnostics.EventLog]::CreateEventSource($eventSource, 'Application')
		Write-Output "Created EventLog source: $eventSource"
	}
	else {
		Write-Output "EventLog source already exists: $eventSource"
	}
}
catch {
	Write-Warning "Failed to create EventLog source (requires admin and may already exist): $_"
}

# Create Windows Service using sc.exe (idempotent)
function Service-Exists($name) {
	$svc = Get-Service -Name $name -ErrorAction SilentlyContinue
	return $null -ne $svc
}

if (Service-Exists $ServiceName) {
	Write-Output "Service $ServiceName already exists."
}
else {
	$binPath = '"' + $ExePath + '"'
	if ($ServiceAccount -in @('LocalSystem')) {
		$cmd = "sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= \"$DisplayName\""
	}
	elseif ($ServiceAccount -in @('LocalService','NetworkService')) {
		# sc.exe expects obj= "NT AUTHORITY\\LocalService" etc.
		$cmd = "sc.exe create $ServiceName binPath= $binPath start= auto obj= \"NT AUTHORITY\\$($ServiceAccount.Substring(0,$ServiceAccount.Length))\""
		# Note: using LocalService/NetworkService typically does not require a password
		# but sc.exe syntax accepts obj without a password for these special accounts.
	}
	else {
		# domain or custom account - include password if provided
		$escAccount = $ServiceAccount
		if ([string]::IsNullOrEmpty($ServicePassword)) {
			Write-Warning "Installing service for account $ServiceAccount without password. If this is a domain account, provide -ServicePassword."
			$cmd = "sc.exe create $ServiceName binPath= $binPath start= auto obj= \"$escAccount\""
		}
		else {
			$cmd = "sc.exe create $ServiceName binPath= $binPath start= auto obj= \"$escAccount\" password= \"$ServicePassword\""
		}
	}

	Write-Output "Running: $cmd"
	iex $cmd
	Write-Output "Service $ServiceName created (if sc.exe reported success)."
}

Write-Output "Install script completed. Start the service with: Start-Service -Name $ServiceName"
