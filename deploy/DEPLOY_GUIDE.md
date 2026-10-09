# GradeSphere — install on the Windows Server (by hand)

A fresh install with a new, empty database. The demo (`/ExamSoftware`, `/ExamAPI`) and every other database on the server stay untouched.

| | Value |
|---|---|
| Website | `https://www.vivacollege.in/gradesphereapp` → folder `D:\website\GradeSphereApp\GradeSphereClient` |
| API | `https://www.vivacollege.in/gradesphereapi` → folder `D:\website\GradeSphereApp\GradeSphereApi` |
| Uploads (photos, signatures, logos) | `D:\website\GradeSphereApp\GradeSphereUplodes` — not an IIS application; only the API reads and writes it |
| Database | `GradeSphereApp`, used through your **existing** SQL login |
| IIS app pools | `GradeSphereApi`, `GradeSphereClient` (both *No Managed Code*) |

The website and the API are under the same host, so the browser calls the API at `/gradesphereapi` directly; CORS is not involved (the settings still allow `vivacollege.in` and `www.vivacollege.in`).

## 0. On the development PC: build

```powershell
powershell -ExecutionPolicy Bypass -File D:\Projects\ReactApi\ExamAPI\deploy\Build-Package.ps1
```

`D:\Projects\ReactApi\_deploy` then holds:

- `GradeSphereApi-<date>.zip` — the API; extract it **straight into** `GradeSphereApi` (`ExamAPI.dll` and `web.config` end up directly in that folder)
- `GradeSphereClient-<date>.zip` — the website; extract it straight into `GradeSphereClient` (`index.html`, `assets`, `web.config`)
- `GradeSphereApp-Setup.sql` — the whole database, for SSMS
- `appsettings.Production.json` — the API's settings, with a new random login key (made once; keep it)

Copy them to the server (Remote Desktop: copy on the PC, paste on the server).

## 1. Once: prerequisites on the server

Skip what the demo already needs (most likely all of it):

1. **.NET 10 Hosting Bundle** — https://dotnet.microsoft.com/download/dotnet/10.0 → *ASP.NET Core Runtime* → *Hosting Bundle*. Install, then run `iisreset` in an Administrator command prompt.
2. **IIS URL Rewrite 2.1** — https://www.iis.net/downloads/microsoft/url-rewrite.

## 2. Database (SSMS)

1. Open `GradeSphereApp-Setup.sql` in SSMS, connected to the server's SQL Server as an admin.
2. On the line marked `>>>`, type the name of the **existing SQL login** the website should use, e.g. `DECLARE @AppLogin sysname = N'your_login';` (leave it empty if that login is `sa`).
3. **Execute.** The last message says *Done: GradeSphereApp is ready*.

It creates only the database `GradeSphereApp`, its tables and the Engineering + Pharmacy starter templates, and lets that login read and write it. No login is created, no password changes, no other database is touched. Running it again is safe (it skips what exists).

## 3. Files

1. Extract `GradeSphereApi-<date>.zip` into `D:\website\GradeSphereApp\GradeSphereApi`, and `GradeSphereClient-<date>.zip` into `D:\website\GradeSphereApp\GradeSphereClient`. (If Windows creates an extra inner folder, move its contents up one level.)
2. Put `appsettings.Production.json` into `GradeSphereApi` and edit it in Notepad:
   - `ConnectionStrings` → `Server=` the SQL Server as the web server sees it (its LAN address, add `,port` or `\instance` if it is not the default), `User Id=` the login from step 2, `Password=` its password.
   - `Bootstrap` → `PlatformAdminEmail` and `PlatformAdminPassword`: the platform admin, the login that adds colleges.
   - `Storage:UploadsRoot` must be the uploads folder's exact path (written as `D:\\website\\GradeSphereApp\\GradeSphereUplodes` — double backslashes in this file).
   - Leave `Jwt` as it is; `EmailSettings` stays empty until email is set up (section 6).
3. Optional, recommended for the first start: create `GradeSphereApi\logs` and in `GradeSphereApi\web.config` set `stdoutLogEnabled="true"` — any start-up error is then written there.

## 4. IIS

PowerShell **as Administrator** on the server (change `$site` if the vivacollege.in site has another name — `Get-Website` lists them):

```powershell
$site = "Default Web Site"
$root = "D:\website\GradeSphereApp"
Import-Module WebAdministration

foreach ($p in "GradeSphereApi", "GradeSphereClient") {
    if (-not (Test-Path "IIS:\AppPools\$p")) { New-WebAppPool $p | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$p" managedRuntimeVersion ""
}
New-WebApplication -Site $site -Name gradesphereapi -PhysicalPath "$root\GradeSphereApi" -ApplicationPool GradeSphereApi
New-WebApplication -Site $site -Name gradesphereapp -PhysicalPath "$root\GradeSphereClient" -ApplicationPool GradeSphereClient

# IIS may read the two application folders; only the API may write uploads (and its logs).
icacls "$root\GradeSphereApi" /grant "IIS_IUSRS:(OI)(CI)RX" "IUSR:(OI)(CI)RX"
icacls "$root\GradeSphereClient" /grant "IIS_IUSRS:(OI)(CI)RX" "IUSR:(OI)(CI)RX"
icacls "$root\GradeSphereUplodes" /grant "IIS AppPool\GradeSphereApi:(OI)(CI)M"
if (Test-Path "$root\GradeSphereApi\logs") { icacls "$root\GradeSphereApi\logs" /grant "IIS AppPool\GradeSphereApi:(OI)(CI)M" }
```

**Keep the API awake** (otherwise IIS stops it after 20 idle minutes and restarts it every 29 hours, and the next person to sign in waits several seconds for it to start):

```powershell
Install-WindowsFeature Web-AppInit                       # "Application Initialization": starts the API without waiting for a visitor
$pool = "IIS:\AppPools\GradeSphereApi"
Set-ItemProperty $pool startMode AlwaysRunning
Set-ItemProperty $pool processModel.idleTimeout ([TimeSpan]::Zero)          # never stop when idle
Set-ItemProperty $pool recycling.periodicRestart.time ([TimeSpan]::Zero)    # no restart every 29 hours ...
Set-ItemProperty $pool -Name recycling.periodicRestart.schedule -Value @{value = "03:00:00"}   # ... one at 3 am instead
Set-ItemProperty "IIS:\Sites\$site\gradesphereapi" preloadEnabled True
```

The API then warms its sign-in path by itself right after every start.

**Without IIS admin rights** (files only), two things do most of it:
- `appsettings.Production.json` → `"KeepAlive": { "Url": "https://www.vivacollege.in/gradesphereapi/api/health" }`: the API calls its own health check every 10 minutes, so IIS never sees it idle. If the API log shows "Keep-alive: … could not be reached" (some servers cannot reach their own public address), try `http://localhost/gradesphereapi/api/health`.
- The sign-in page wakes the API as soon as it opens, so after a nightly IIS restart the API starts while the first person is still typing.

Or by hand in IIS Manager: two app pools (*No Managed Code*), *Add Application* twice under the site (alias `gradesphereapi` → `GradeSphereApi` folder and pool; alias `gradesphereapp` → `GradeSphereClient` folder and pool), and on `GradeSphereUplodes` → Properties → Security → Edit → Add `IIS AppPool\GradeSphereApi` → **Modify**.

**How uploads work:** the API saves every photo, signature and logo under `Storage:UploadsRoot` and creates the subfolders itself. The folder does not need to be inside the API or under any website — the browser never opens it; files are only handed out by the API (`/gradesphereapi/api/Files/…`) to signed-in users. Being outside `GradeSphereApi`, it survives every update of the API files.

## 5. First start

1. Open `https://www.vivacollege.in/gradesphereapi/api/health` → `{"status":"ok"}`. This first start also creates the platform admin.
2. Open `https://www.vivacollege.in/gradesphereapp` and sign in as the platform admin.
3. In `appsettings.Production.json` set `"PlatformAdminPassword": ""` and save (the admin already exists; the setting is only read on a start with no platform admin).
4. Platform → **New college**: pattern **NEP**, *Copy from: Starter: Engineering…* (or Pharmacy), branches, academic year, college admin(s).
5. Upload a college logo or a student photo once, and check a file appeared in `GradeSphereUplodes`.

**If the health page shows an error (500.x):** read the newest file in `GradeSphereApi\logs`. Most common: wrong `Server=`, login name or password in `appsettings.Production.json`, or the login was not given on the `>>>` line.

## 6. Email for password reset (later)

Gmail: turn on 2-Step Verification on the sending account, create an **App Password** (Google Account → Security → App passwords), then in `appsettings.Production.json`:

```json
"EmailSettings": { "SmtpServer": "smtp.gmail.com", "Port": 587, "SenderEmail": "<account>@gmail.com", "Password": "<16-character app password>", "SenderName": "GradeSphere" }
```

## Installing a new version

1. Build again on the PC (step 0).
2. On the server, put a file named `app_offline.htm` into `GradeSphereApi` (IIS stops the API).
3. Replace the files of both folders with the new zips' contents — **keep** `appsettings.Production.json` (and `logs`).
4. Delete `app_offline.htm`.
5. Run the new `GradeSphereApp-Setup.sql` in SSMS (same login name on the `>>>` line) — it only adds what changed.

`GradeSphereUplodes` is not touched by updates.
