# Timeswatch Bio-27 Attendance Integration

ASP.NET Core (.NET 8) service that connects to a **Timeswatch Bio-27** biometric terminal and
captures **attendance punches into MySQL**.

> **Device protocol:** the Bio-27 is a **ZKTeco-protocol** device (its network screen shows the
> ZK comm port — here set to **5005** — and a ZK "server port" 4370). It does **not** speak the
> Dahua protocol. The app talks to it with the pure-managed **`ZkTeco.Attendance.API`** library
> (no COM, no native DLLs) and **polls** for new punches every few seconds.

## Projects

| Project | What it is |
|---|---|
| `TimeswatchAttendance.Web` | The web app: ZK polling service, status + attendance APIs, EF Core/MySQL. `net8.0`. |
| `DahuaNetSdk` | **Unused** — kept from the initial (wrong) Dahua attempt for history. Not referenced by the web app. |

## Prerequisites

1. **.NET 8 SDK** (`winget install Microsoft.DotNet.SDK.8`), or Visual Studio 2022 with the
   "ASP.NET and web development" workload.
2. **MySQL** running and reachable. The app auto-creates the `attendance` database/table on first run.
3. The **Bio-27** powered and on the same network, with its **comm port** reachable
   (`Test-NetConnection <device-ip> -Port 5005` → `TcpTestSucceeded: True`).

## Configure

`TimeswatchAttendance.Web/appsettings.json`:
- `BiometricDevice.Ip` — device IP (e.g. `192.168.18.179`).
- `BiometricDevice.Port` — ZK comm port (this device: **5005**; ZK default is 4370).
- `BiometricDevice.Password` — ZK comm key (usually `0`).
- `BiometricDevice.PollSeconds` — how often to pull new punches (default `5`).
- `ConnectionStrings.AttendanceDb` — MySQL connection.

## Build & run

```powershell
cd D:\TimeswatchAttendance
dotnet restore
dotnet run --project TimeswatchAttendance.Web
```
(or open `TimeswatchAttendance.sln` in Visual Studio and run the **TimeswatchAttendance.Web** profile).
Listens on `http://localhost:5080` (or `:5000`). NuGet restore needs internet the first time.

## Verify

- Console logs `ZK attendance poller started …`, then on each punch `Imported N new punch(es) …`.
- `GET http://localhost:5080/api/device/status` → `{ online, serialNo, firmware, punchesCaptured, … }`.
- Present an enrolled finger/card on the Bio-27, then `GET /api/attendance` (optionally
  `?date=yyyy-MM-dd&take=100`) shows the rows.

## How it works

- `ZkAttendanceService` (a `BackgroundService`, singleton) connects to the device on a timer,
  calls `GetAttendance()`, and imports records newer than a high-water mark into MySQL via a scoped
  `AttendanceDbContext`. The device buffers its own logs, so short polling never misses a punch.
- Each `AttendanceRecord` stores `UserId`, the punch time (`EventTimeLocal` / `EventTimeUtc`),
  the punch direction (`DoorNo`), and the verify mode (`OpenMethod`).
- A unique index `(UserId, EventTimeUtc, DoorNo)` plus an existence check de-duplicate re-reads.

## Notes / gotchas

- **Why polling, not push:** the pure-managed ZK library is poll-only. True event-push would require
  the official `zkemkeeper` COM SDK (32-bit, must be registered, needs an STA message pump in a
  service). For attendance, polling every few seconds is the standard, robust approach.
- **Schema bootstrap** uses `EnsureCreated()`. For migrations later:
  `dotnet ef migrations add Initial --project TimeswatchAttendance.Web && dotnet ef database update --project TimeswatchAttendance.Web`.
- **Keep the process alive** (Kestrel via `dotnet run`, or a Windows Service). Under IIS, disable idle
  timeout / set AlwaysRunning so polling keeps running.
- **`DahuaNetSdk` project** remains only for history; it is not referenced and can be deleted later.

## Next steps

- Map device `UserId` to a people/employees table.
- Attendance reports (daily first-in/last-out, CSV export).
- Manage users/fingerprints from the web app (the ZK library exposes `GetUsers`/`CreateUser`/…).
