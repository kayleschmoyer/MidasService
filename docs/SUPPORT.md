# Midas Transfer Worker — Support & Troubleshooting Guide

**Audience:** Support / IT staff installing and supporting the Midas Transfer Worker service.
**Last updated:** 2026-06-22

---

## 1. What this service does

Midas Transfer Worker is a Windows background service. **Once per day, at a configured time, it
uploads the `*.xml` files from a local folder to a remote SFTP server.**

- Files it successfully uploads are **moved** out of the source folder into a `Processed` folder.
- Files that fail are **moved** to a `Quarantine` folder for inspection.
- It logs everything to its own Windows Event Log channel.

It does **not** run continuously — it sends once per day at the scheduled time (and catches up on
startup if that time was missed while the machine was off).

---

## 2. Key locations (memorize these)

| Item | Path |
|------|------|
| Service name | `MidasTransferWorker` (display name "Midas Transfer Worker") |
| Install folder | `C:\Program Files (x86)\MAM Software\MidasTransferService\` |
| **Config file (the one that matters)** | `C:\Program Files (x86)\MAM Software\MidasTransferService\appsettings.json` |
| Source folder (default) | `C:\HighwayData` |
| Processed files | `C:\Program Files (x86)\MAM Software\MidasTransferService\Processed` |
| Quarantine (failed files) | `C:\Program Files (x86)\MAM Software\MidasTransferService\Quarantine` |
| Last-success marker | `C:\ProgramData\MidasTransferService\state.json` |
| **Logs** | Event Viewer → **Applications and Services Logs → MidasTransferWorker** |

---

## 3. System requirements

- Windows 10/11 or Windows Server 2016+.
- **No .NET install required** if you use the self-contained MSI (the runtime is bundled).
- Administrator rights to install and to edit the config file.
- Outbound network access to the SFTP host on its port (default TCP 22).

---

## 4. Installation

The supported installer is the **MSI** (`MidasTransferWorker.msi`).

1. Copy `MidasTransferWorker.msi` to the target machine.
2. **Double-click it** and approve the Windows (UAC) prompt — or, from an elevated prompt:
   ```
   msiexec /i MidasTransferWorker.msi
   ```
   Silent install:
   ```
   msiexec /i MidasTransferWorker.msi /qn
   ```
3. The installer registers and **auto-starts** the `MidasTransferWorker` service and creates its
   Event Log channel.

### Run under a specific service account (optional)
By default the service runs as **LocalSystem**. To run as a domain/service account:
```
msiexec /i MidasTransferWorker.msi /qn SERVICEACCOUNT="DOMAIN\svc-midas" SERVICEPASSWORD="********"
```

---

## 5. Configuration

Edit **`C:\Program Files (x86)\MAM Software\MidasTransferService\appsettings.json`**.

> ⚠️ **TWO MOST COMMON MISTAKES — read this.**
> 1. **Edit the file in the install folder above — not a copy on the Desktop/Downloads.** The
>    service only reads the file next to its program. Editing a copy elsewhere does nothing.
> 2. **Open your editor "as administrator" before opening the file.** That folder is protected. If
>    you edit it without elevation, Windows may silently save your change to a hidden per-user copy
>    (VirtualStore) and the service keeps using the original — looks like "my change had no effect."
>    *How to do it right:* right-click Notepad → **Run as administrator** → File → Open → paste the
>    path. When you Save, it should save in place with no "Save As" prompt.

**After any config change, restart the service:**
```powershell
Restart-Service MidasTransferWorker
```

### Settings reference

| Setting | Meaning | Example |
|---------|---------|---------|
| `XmlFolder` | Local folder the `.xml` files are picked up from | `C:\\HighwayData` |
| `Host` | SFTP server hostname | `ftp5.mavistrie.net` |
| `Port` | SFTP port | `22` |
| `Username` | SFTP username | `vast_pos` |
| `Password` | SFTP password (see note below) | `""` |
| `RemoteFolder` | Destination folder on the server | `/MidasVAST_Data_Highway/Production/IN` |
| `NightlyTime` | Daily run time, 24-hour `HH:mm:ss` | `22:00:00` (= 10:00 PM) |
| `SaveRetentionDays` | Days to keep files in `Processed` before auto-delete | `30` |
| `QuarantineRetentionDays` | Days to keep files in `Quarantine` before auto-delete | `30` |
| `OverwriteRemoteFiles` | `true` = overwrite if the file already exists on the server; `false` = skip it | `false` |
| `HostKeyFingerprints` | Optional list of trusted server fingerprints (see §9) | `[]` |

> **`NightlyTime` is 24-hour.** `13:35:00` = 1:35 PM, `22:00:00` = 10:00 PM.

### Password handling
Two options — pick one:
- **In the file:** put it in the `Password` field. Special characters (`* ! # $`) are safe inside the
  JSON file — no escaping needed.
- **As an environment variable (keeps it out of the file):** leave `Password` empty and set a
  machine variable:
  ```
  setx MAVISSFTP__PASSWORD "thepassword" /M
  ```
  then restart the service. *Note:* if you use this method and the password has special characters,
  wrap it carefully (in PowerShell use single quotes; in cmd a `!` can cause problems). The file
  method avoids those quoting issues.

---

## 6. Verifying the install works

1. **Confirm the service is running:**
   ```powershell
   Get-Service MidasTransferWorker
   ```
   Status should be **Running**.
2. **Force an immediate test run** (don't wait for the nightly time):
   - Put a sample `.xml` in `C:\HighwayData`.
   - Edit `appsettings.json` (elevated) → set `NightlyTime` to **2–3 minutes ahead** of the current
     clock → save.
   - `Restart-Service MidasTransferWorker`.
   - Watch the log (see §7). You want to see `Starting nightly transfer run` followed by
     `Nightly transfer summary: ... Uploaded=1, Failed=0`.
   - Set `NightlyTime` back to the real schedule and restart again.
3. A successful run creates `C:\ProgramData\MidasTransferService\state.json` and moves the uploaded
   file into the `Processed` folder.

---

## 7. Where the logs are

**Event Viewer → Applications and Services Logs → `MidasTransferWorker`.**
(There are **no log files on disk**.)

Useful lines to look for:
- `MidasTransferWorker started` / `stopping` — service lifecycle (app's own messages).
- `Starting nightly transfer run at ...` — a run began.
- `Nightly transfer summary: ... Found=X, Uploaded=Y, Skipped=Z, Failed=W` — the result of a run.
- `Upload failed for ...` — a file failed (the real error follows; "(password not logged)" is just a
  note that we don't log the password — it is **not** an error).
- `SFTP connection verification failed: ...` — could not connect/log in.

> Service **start/stop/crash** events from Windows itself live in **Windows Logs → System** (source
> "Service Control Manager"), separate from the app's own channel.

---

## 8. How it behaves (so expectations are right)

- **Timing:** runs once per day at `NightlyTime`. Dropping a file in mid-day does nothing until then.
  If the machine was off at that time, it runs once on the next startup.
- **Only top-level `*.xml`** in `XmlFolder` are sent (subfolders are ignored).
- **Successful files are moved to `Processed`** (so the source folder clears).
- **Failed files are moved to `Quarantine`** — they are **not** delivered; investigate and, after
  fixing, move them back to `XmlFolder` to resend.
- **Skipped:** if a file already exists on the server and `OverwriteRemoteFiles` is `false`, it is
  skipped and left in the source folder.
- Old files in `Processed`/`Quarantine` are auto-deleted after their retention days.

---

## 9. Host key verification (optional but recommended)

To protect against man-in-the-middle, pin the server's key. With `HostKeyFingerprints` empty, the
service connects but logs a warning containing the server's current fingerprint, e.g.
`...presented key SHA256:abcd1234...`. Copy that value into the config:
```json
"HostKeyFingerprints": [ "SHA256:abcd1234...the-logged-value..." ]
```
Restart the service. After this, a server presenting a different key is rejected.

---

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| **Edited config but nothing changed** | Edited a copy, or edited without admin (saved to VirtualStore) | Edit the file in the install folder, with the editor run **as administrator**; then `Restart-Service` |
| **Service set for a time but "did nothing"** | `NightlyTime` not reached yet; or already ran today; or service not running | Verify it's Running; check `state.json` timestamp; remember `NightlyTime` is 24-hour |
| **No `state.json` created** | No run has *succeeded* yet (or service never properly started) | Check the log for errors; confirm `C:\ProgramData\MidasTransferService` exists |
| **Run shows `Uploaded=0, Failed=N`** | Uploads are failing — see the per-file error | Open the `Upload failed for ...` event to get the real reason (rows below) |
| **`SftpPermissionDeniedException`** | (a) Running an **old build** that uploads a temp `.part` file the server rejects; (b) account lacks write permission; (c) wrong remote path | Ensure build **v1.0.1+** (uploads directly, no `.part`). If still failing, test a manual upload (§11) — if that also fails it's a server permission/path issue for the vendor |
| **Authentication failed / login denied** | Wrong username/password; special char mangled via env var | Re-check `Username`/`Password`; prefer the password in the file to avoid shell quoting issues |
| **Host key verification FAILED** | Pinned fingerprint doesn't match the server | Confirm the server key is legitimate, then update `HostKeyFingerprints` with the new value |
| **Connection timeout / refused** | Wrong `Host`/`Port`, firewall, server down | Verify host/port; test connectivity (§11); check firewall allows outbound to the SFTP port |
| **Configured XML folder does not exist** | `XmlFolder` path wrong or missing | Create the folder or fix the path in config; restart |
| **Uploads very slow (minutes for a few files)** | Old build retrying a permanent error with backoff | Upgrade to **v1.0.1+** (permanent errors now fail fast) |
| **Files keep going to Quarantine** | Uploads failing for one of the reasons above | Fix the cause, then move the files from `Quarantine` back to `XmlFolder` to resend |
| **No events in the MidasTransferWorker channel** | Old build logged to Windows Logs → Application; or you're looking in the wrong place | Look under **Applications and Services Logs**, not Windows Logs; if still empty, reinstall current build |

---

## 11. Quick external test (isolate service vs server)

To tell whether a problem is the service or the SFTP server, connect manually with the **same**
host/username/password using **WinSCP** or **FileZilla** and try to upload one file directly into the
configured `RemoteFolder`.

- **Manual upload works, service fails** → the issue is on the service/host side (build version,
  config, the wrong-file/elevation traps above).
- **Manual upload also fails** (e.g. permission denied, or "no such path") → the issue is server-side:
  the account's write permission or the remote path is wrong. This is a conversation with the SFTP
  provider — note that some servers require a **relative** path (e.g. `Production/IN`) rather than an
  absolute one (`/MidasVAST_Data_Highway/Production/IN`) because the account is locked to a home
  directory.

---

## 12. Upgrading to a new version

1. Uninstall the current version: **Settings → Apps → Midas Transfer Worker → Uninstall**, or
   `msiexec /x MidasTransferWorker.msi`.
2. Install the new MSI (§4).
3. Your `appsettings.json` is preserved across upgrades, but always re-check it after upgrading.

---

## 13. Uninstall / manual service removal

**Normal uninstall:** Settings → Apps → Uninstall, or `msiexec /x MidasTransferWorker.msi`.

**Manual removal** (if the service was hand-created or the uninstall didn't remove it), elevated:
```powershell
Stop-Service MidasTransferWorker -Force
sc.exe delete MidasTransferWorker
```
Optional cleanup:
```powershell
Remove-Item "C:\Program Files (x86)\MAM Software\MidasTransferService" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "C:\ProgramData\MidasTransferService" -Recurse -Force -ErrorAction SilentlyContinue
Remove-EventLog -Source "MidasTransferWorker" -ErrorAction SilentlyContinue
```
Deleting the service does **not** touch files in `C:\HighwayData`, `Processed`, or `Quarantine`.

---

## 14. Information to collect before escalating

When raising an issue to the dev team, include:
1. **Export of the `MidasTransferWorker` event log** around the failure (Event Viewer → right-click
   the log → Save All Events As → `.evtx`), or copy the failing event text.
2. The `appsettings.json` contents **with the password redacted**.
3. Output of:
   ```powershell
   Get-Service MidasTransferWorker
   sc.exe qc MidasTransferWorker        # shows the installed binary path / version location
   ```
4. The installed **version** (MSI file name / version).
5. Result of the manual WinSCP/FileZilla upload test (§11) — this is the single most useful data
   point, as it isolates service vs server.
