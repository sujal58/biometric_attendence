# Timeswatch Bio-27 Attendance Integration

ASP.NET Core (.NET 8) service that connects to a **Timeswatch Bio-27** biometric terminal over the
**Dahua NetSDK**, proves connectivity (login + serial/version), and captures **attendance punches in
real time** into **MySQL**.

> The Bio-27 speaks the Dahua protocol. This solution compiles the vendor C# interop layer
> (`NetSDKCS`) from source and ships the native x64 SDK DLLs alongside the app.

## Projects

| Project | What it is |
|---|---|
| `DahuaNetSdk` | Class library — the vendor interop wrapper (`NetSDK.cs`, `NetSDKStruct.cs`, `OriginalSDK.cs`, namespace `NetSDKCS`). `net8.0-windows`, **x64**. |
| `TimeswatchAttendance.Web` | The web app: hosted device service, status + attendance APIs, EF Core/MySQL. `net8.0-windows`, **x64**. Native DLLs live in `NativeLibs\`. |

## Prerequisites (one-time)

1. **.NET 8 SDK** (this machine currently has only the runtime). Install it, e.g.:
   ```powershell
   winget install Microsoft.DotNet.SDK.8
   ```
   Open a new terminal afterwards and confirm: `dotnet --version`.
2. **MySQL** running locally (the same server you used for `WebApplication2`). The app will create
   the `attendance` database/table automatically on first run.
3. The **Bio-27 device** must be powered, on the LAN, reachable, and already **initialised**
   (admin password set) — an uninitialised device cannot be logged into.

## Configure

Edit `TimeswatchAttendance.Web/appsettings.json`:

- `BiometricDevice.Ip` — the device IP (default placeholder `192.168.1.108`).
- `BiometricDevice.Port` — Dahua SDK port, usually **37777**.
- `BiometricDevice.Username` / `Password` — device admin credentials.
- `ConnectionStrings.AttendanceDb` — MySQL connection (defaults to `localhost` / `root` / `admin`,
  database `attendance`).

## Build & run

```powershell
cd D:\TimeswatchAttendance
dotnet restore
dotnet run --project TimeswatchAttendance.Web
```
(or open `TimeswatchAttendance.sln` in Visual Studio and press F5).

The app listens on `http://localhost:5080`. The build automatically copies the native DLLs from
`NativeLibs\` next to the app.

## Verify

**Milestone 1 — connectivity.** Watch the console for:
```
Device ONLINE. Serial=... Version=...
```
Then call:
```
GET http://localhost:5080/api/device/status
```
Expect `{ "online": true, "serialNo": "...", "softwareVersion": "...", "doorState": "..." }`.
If `online` is false, the JSON `lastError` and the console log show the NetSDK reason — check IP/port/
credentials and that the device is initialised and reachable.

**Milestone 2 — attendance capture.** With the app running, present an enrolled **finger/card** on the
Bio-27. The console logs `Punch saved: User=... Door=... ...` and a row appears via:
```
GET http://localhost:5080/api/attendance
GET http://localhost:5080/api/attendance?date=2026-06-22&take=100
```

## How it works

- `DahuaDeviceService` (a `BackgroundService`, registered as a singleton) owns the single NetSDK
  session: `Init` → `SetAutoReconnect` → `SetDVRMessCallBack` → `LoginWithHighLevelSecurity` →
  `QueryDevState(SOFTWARE)` (serial/version) → `StartListen`. It retries login until the device is
  reachable and auto-reconnects on drops.
- Punches arrive on the native alarm callback (`ALARM_ACCESS_CTL_EVENT` →
  `NET_ALARM_ACCESS_CTL_EVENT_INFO`). The callback only marshals the struct and enqueues an
  `AttendancePunch` on a `Channel` (it must return fast — no DB work inside the callback).
- A consumer loop reads the channel, de-dupes, and writes `AttendanceRecord` rows via a scoped
  `AttendanceDbContext`.
- Device timestamps are **UTC**; we store both `EventTimeUtc` and `EventTimeLocal`.

## Important notes / gotchas

- **x64 only.** Both projects force `<PlatformTarget>x64</PlatformTarget>` because the native DLLs are
  64-bit. A 32-bit/AnyCPU(32) process throws `BadImageFormatException` when loading `dhnetsdk.dll`.
- **Native DLLs are committed** in `TimeswatchAttendance.Web/NativeLibs\` (dhnetsdk, dhconfigsdk,
  dhplay, avnetsdk, IvsDrawer, StreamConvertor, Infra, ImageAlg, RenderEngine) and copied to output by
  MSBuild targets. P/Invoke resolves them from the app folder.
- **Keep the process alive.** This is a persistent listener. Run via `dotnet run` / Kestrel or host as a
  Windows Service. Under IIS, disable idle timeout and set the app pool to AlwaysRunning, or the device
  connection is dropped on recycle.
- **Schema bootstrap.** The app calls `EnsureCreated()` for simplicity. Once the .NET SDK is installed
  you can switch to migrations:
  ```powershell
  dotnet ef migrations add Initial --project TimeswatchAttendance.Web
  dotnet ef database update --project TimeswatchAttendance.Web
  ```
- The SDK manual lives at `C:\Users\Asus\Downloads\TrueFace_SDK\NetSDK Programming Manual (Intelligent Building).pdf`;
  the reference sample is `...\TrueFace_SDK\AccessDemo2s` (`AccessForm.cs`, `Setting\DeviceInfoForm.cs`).

## Next steps (later milestones)

- Map device `szUserID` to a people/employees table.
- Enroll/manage users + fingerprints from the web app (manual §2.3.7 / `AccessDemo2s` UserManager).
- Attendance reports (daily first-in/last-out, CSV export).
