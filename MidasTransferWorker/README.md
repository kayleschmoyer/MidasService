MidasTransferWorker - Windows Service for automated Mavis XML SFTP transfer

Overview
- Background worker service that uploads XML files from a configured local folder to an SFTP server once per day at a scheduled time.
- No UI. Configurable via appsettings.json or environment variables.

Default configuration location
- appsettings.json next to the executable (see MidasTransferWorker/appsettings.json included).
- Values can be overridden using environment variables, e.g. MAVISSFTP__HOST for Host.

Logging
- The service logs only to the Windows Event Log, in a dedicated channel named "MidasTransferWorker".
- View it in Event Viewer under: Applications and Services Logs > MidasTransferWorker
- There are no log files on disk (the previous %ProgramData%\...\logs folder is no longer used).

State file
- Last successful run is stored at: %ProgramData%\MidasTransferService\state.json

Processed / Quarantine folders
- Successful uploads are moved to: %ProgramFiles(x86)%\MAM Software\MidasTransferService\Processed
- Failed or errored files are moved to: %ProgramFiles(x86)%\MAM Software\MidasTransferService\Quarantine

How to install (MSI - recommended)
- The supported way to install is the WiX v5 MSI, which registers the Windows Service, creates the
  EventLog source, and handles upgrades/uninstall. Build it with:
    .\MidasTransferWorker\Installer\Build-Installer.ps1
  then install elevated:
    msiexec /i MidasTransferWorker.msi
  See MidasTransferWorker/Installer/Readme-Installer.md for self-contained builds, silent install,
  custom service accounts, and uninstall.
- CI builds the MSI on windows-latest and uploads it as an artifact
  (.github/workflows/build-installer.yml).

How to publish manually (without the MSI)
1. From the solution folder run: dotnet publish MidasTransferWorker -c Release -r win-x64 --self-contained false
   (Adjust runtime identifier as needed. If you want framework-dependent deployment, remove -r.)

Configuration notes
- appsettings.json contains a MavisSftp section with the following keys:
	XmlFolder, Host, Port, Username, Password, RemoteFolder, NightlyTime (HH:mm:ss),
	SaveRetentionDays, QuarantineRetentionDays, OverwriteRemoteFiles, HostKeyFingerprints
- To keep passwords out of source control: set the password via environment variable MAVISSFTP__PASSWORD or set it in the Windows service configuration using sc.exe "binPath= ..." with a config file, or use a secrets store.

Host key verification (recommended)
- To defend against man-in-the-middle attacks, pin the SFTP server's host key fingerprint via
  the HostKeyFingerprints array. Accepted formats are "SHA256:<base64>" (preferred) or
  colon-separated MD5 hex.
- If HostKeyFingerprints is empty, the host key is NOT verified and a warning is logged on every
  connection that includes the server's current SHA256 fingerprint, which you can copy into config.
- You can also obtain the fingerprint with: ssh-keyscan -t rsa,ecdsa,ed25519 ftp5.mavistrie.net
  then hash it, or read it from the warning log line on first run.
- Example:
	"HostKeyFingerprints": [ "SHA256:abcd1234...base64..." ]

Behavior
- The service wakes every minute and triggers the nightly transfer once per scheduled day at the configured time.
- If the service starts after the scheduled time and the run for that day has not completed, it will run once immediately.
- Uploads use a temporary .part remote filename and are renamed after successful upload; an orphaned
  .part file is removed if the rename fails. Existing remote files are skipped by default.
- Retention deletes processed XML files older than SaveRetentionDays after a successful run, and
  quarantined XML files older than QuarantineRetentionDays on every run so failures do not accumulate.

Notes
- This project targets net8.0-windows and uses Renci.SshNet for SFTP. Ensure packages are restored prior to build.
- Review and adjust logging and retention behavior to suit your operations.

Security: how to set password safely
- Do not put the password in appsettings.json in production. Use one of these approaches:
  - Set environment variable: setx MAVISSFTP__PASSWORD "yourpassword"
  - Use a vault or secrets manager and inject the secret at deploy time
  - Store a protected config file accessible only to service account

Contact
- This is a non-UI background service only. For changes, edit the code under MidasTransferWorker/ and republish.
