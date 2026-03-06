# Teltonika Backup Builder

Windows desktop tool (C# + WPF, MVVM) for working with Teltonika backup files (`.tar.gz`), focused on TRB/RUT style devices.

It provides two modes:

1. **Backup duplicate mode**  
   Create multiple backup copies from one source backup and set `devicename` + `hostname` in `/etc/config/system` to custom values.
2. **REST API rebuild mode**  
   Analyze a source backup, generate a list of REST API calls, run them step-by-step or all at once, then generate and compare a resulting router backup.

---

## Features

### 1) Backup duplicate mode

- Select one source backup (`.tar.gz`).
- Enter multiple hostnames (one per line, or comma/semicolon separated).
- For each hostname, create a new backup file:
  - File name is adjusted to include the target hostname.
  - In `/etc/config/system`, `option devicename` and `option hostname` are set to that hostname.
- Optional verification after generation.

### 2) REST API rebuild mode

- Analyze selected backup and extract `/etc/config/*` files.
- Build a planned API call list (visible in UI).
- Show full request preview for the selected call.
- Execute:
  - single selected call, or
  - all calls sequentially.
- After execution:
  - generate a new backup on the router via API,
  - download it,
  - compare `/etc/config` between original and API-result backup.
- Connection fields:
  - Router IP/URL (default `192.168.2.1`),
  - username (default `admin`),
  - password.

---

## Requirements

- Windows
- .NET SDK 10.0+

---

## Build and run

```powershell
dotnet build .\TeltonikaBackupBuilder.slnx
dotnet run --project .\TeltonikaBackupBuilder.App
```

---

## API behavior (important)

- The app uses **REST endpoints** for rebuild mode (no shell command execution).
- Planned calls include config updates and backup actions (generate/download).
- Router HTTPS certificates are currently accepted without strict validation in the client (development convenience).

---

## Limitations / notes

- API coverage depends on firmware/device endpoint compatibility.
- Some complex config areas may require endpoint-specific handling depending on router model/firmware.
- Always test on non-production devices first.

---

## Project

- Solution: `TeltonikaBackupBuilder.slnx`
- App: `TeltonikaBackupBuilder.App`
