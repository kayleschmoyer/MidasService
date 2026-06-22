# MidasTransferWorker MSI Installer (WiX v5)

This folder builds a Windows Installer (MSI) that installs the worker as a Windows Service,
registers the EventLog source, and handles clean upgrades and uninstall. The MSI is the single
supported installation mechanism for this service.

## Prerequisites
- .NET 8 SDK (provides `dotnet`).
- The WiX v5 tool, installed automatically by the build script. To install it manually:
  ```
  dotnet tool install --global wix
  wix extension add -g WixToolset.Util.wixext
  ```
  WiX v5 is a .NET tool — there is no separate WiX Toolset MSI to install, and `heat.exe` is not
  needed (the directory tree is harvested at build time by the `<Files>` element).

## Build the MSI
From any shell with `dotnet` on PATH:
```
.\MidasTransferWorker\Installer\Build-Installer.ps1
```
This publishes the worker (framework-dependent) and produces `MidasTransferWorker.msi`.

Options:
- `-SelfContained` — bundle the .NET runtime so target machines need no .NET install.
- `-PublishedFolder <path>` — skip publishing and package an existing publish folder.
- `-OutputMsi <path>` — output path for the MSI.

CI also builds the MSI on `windows-latest` (see `.github/workflows/build-installer.yml`) and
uploads it as a build artifact.

## Install / upgrade / uninstall
Run elevated (Administrator):
```
# Install (interactive progress)
msiexec /i MidasTransferWorker.msi

# Silent install as a domain service account
msiexec /i MidasTransferWorker.msi /qn SERVICEACCOUNT="DOMAIN\svc-midas" SERVICEPASSWORD="********"

# Upgrade: install a newer-versioned MSI - the previous version is removed automatically
msiexec /i MidasTransferWorker_<newversion>.msi /qn

# Uninstall
msiexec /x MidasTransferWorker.msi /qn
```

## What the MSI does
- Installs to `%ProgramFiles(x86)%\MAM Software\MidasTransferService` (matches the service's own
  Processed/Quarantine pathing).
- Registers and starts the `MidasTransferWorker` Windows Service (`ServiceInstall`/`ServiceControl`),
  running as `LocalSystem` by default (override via `SERVICEACCOUNT`/`SERVICEPASSWORD`).
- Creates the `MidasTransferWorker` EventLog source so the service can write to the Application log.
- Preserves `appsettings.json` across upgrades (`NeverOverwrite`), so operator edits and any
  `MAVISSFTP__*` configuration are not clobbered. Keep the SFTP password out of `appsettings.json`
  in production — set it via the `MAVISSFTP__PASSWORD` environment variable.

## Notes
- The service account defaults to `LocalSystem`. For least privilege, install under a dedicated
  domain or virtual service account and grant it Modify rights to the install folder and the
  `%ProgramData%\MidasTransferService` log/state folder.
- `UpgradeCode` is fixed (`B8E1F2A4-3C5D-4E6F-9A0B-1C2D3E4F5A6B`); keep it stable across releases and
  bump `Version` in `Product.wxs` for each new build so upgrades are detected.
