MidasTransferWorker - Windows Service for automated Mavis XML SFTP transfer

Overview
- Background worker service that uploads XML files from a configured local folder to an SFTP server once per day at a scheduled time.
- No UI. Configurable via appsettings.json or environment variables.

Default configuration location
- appsettings.json next to the executable (see MidasTransferWorker/appsettings.json included).
- Values can be overridden using environment variables, e.g. MAVISSFTP__HOST for Host.

State file
- Last successful run is stored at: %ProgramData%\MidasTransferService\state.json

Processed / Quarantine folders
- Successful uploads are moved to: %ProgramFiles(x86)%\MAM Software\MidasTransferService\Processed
- Failed or errored files are moved to: %ProgramFiles(x86)%\MAM Software\MidasTransferService\Quarantine

How to publish
1. From the solution folder run: dotnet publish MidasTransferWorker -c Release -r win-x64 --self-contained false
   (Adjust runtime identifier as needed. If you want framework-dependent deployment, remove -r.)

How to install as Windows Service (PowerShell):
1. Copy published files to a folder, e.g. C:\Services\MidasTransferWorker
2. Open an elevated PowerShell and run:
   sc.exe create MidasTransferWorker binPath= "C:\\Services\\MidasTransferWorker\\MidasTransferWorker.exe" start= auto
3. Start the service:
   sc.exe start MidasTransferWorker
4. Stop:
   sc.exe stop MidasTransferWorker
5. Delete service:
   sc.exe delete MidasTransferWorker

Configuration notes
- appsettings.json contains a MavisSftp section with the following keys:
	XmlFolder, Host, Port, Username, Password, RemoteFolder, NightlyTime (HH:mm:ss), SaveRetentionDays, OverwriteRemoteFiles
- To keep passwords out of source control: set the password via environment variable MAVISSFTP__PASSWORD or set it in the Windows service configuration using sc.exe "binPath= ..." with a config file, or use a secrets store.

Behavior
- The service wakes every minute and triggers the nightly transfer once per scheduled day at the configured time.
- If the service starts after the scheduled time and the run for that day has not completed, it will run once immediately.
- Uploads use a temporary .part remote filename and are renamed after successful upload. Existing remote files are skipped by default.
- Retention deletes local XML files older than SaveRetentionDays after a successful run.

Notes
- This project uses Renci.SshNet for SFTP. Ensure the package is restored prior to build.
- Review and adjust logging and retention behavior to suit your operations.

Security: how to set password safely
- Do not put the password in appsettings.json in production. Use one of these approaches:
  - Set environment variable: setx MAVISSFTP__PASSWORD "yourpassword"
  - Use a vault or secrets manager and inject the secret at deploy time
  - Store a protected config file accessible only to service account

Contact
- This is a non-UI background service only. For changes, edit the code under MidasTransferWorker/ and republish.
