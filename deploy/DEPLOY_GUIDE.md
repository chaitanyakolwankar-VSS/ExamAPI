# GradeSphere — install on the Windows Server (by hand)

A fresh install with a new, empty database. The demo (`/ExamSoftware`, `/ExamAPI`) and every other database on the server stay untouched.

| | Value |
|---|---|
| Website | `https://vivacollege.in/gradesphere` → folder `C:\inetpub\wwwroot\gradesphere` |
| API | `https://vivacollege.in/gradesphere-api` → folder `C:\inetpub\wwwroot\gradesphere-api` |
| Uploads (photos, signatures, logos) | `C:\inetpub\gradesphere-uploads` (outside the website folders, so an update never deletes them) |
| Database | `GradeSphereApp`, used through your **existing** SQL login |
| IIS app pools | `GradeSphereApp-Site`, `GradeSphereApp-API` |

## 0. On the development PC: build

```powershell
powershell -ExecutionPolicy Bypass -File D:\Projects\ReactApi\ExamAPI\deploy\Build-Package.ps1
```

`D:\Projects\ReactApi\_deploy` then holds:

- `gradesphere-api-<date>.zip` — the API (folder `api\`) and the database files (folder `database\`)
- `gradesphere-site-<date>.zip` — the website
- `GradeSphereApp-Setup.sql` — the one database file (also inside the API zip)
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

1. Create the folders `C:\inetpub\wwwroot\gradesphere`, `C:\inetpub\wwwroot\gradesphere-api` and `C:\inetpub\gradesphere-uploads`.
2. If `/gradesphere` holds the old solution: copy its folder somewhere safe first.
3. Unzip **gradesphere-site** → put its contents (`index.html`, `assets`, `web.config`, …) into `C:\inetpub\wwwroot\gradesphere`.
4. Unzip **gradesphere-api** → put the contents of its **`api`** folder (`ExamAPI.dll`, `web.config`, …) into `C:\inetpub\wwwroot\gradesphere-api`.
5. Put `appsettings.Production.json` into `C:\inetpub\wwwroot\gradesphere-api` and edit it in Notepad:
   - `ConnectionStrings` → `Server=` your SQL Server (e.g. `localhost` or `.\SQLEXPRESS`), `User Id=` the login from step 2, `Password=` its password.
   - `Bootstrap` → `PlatformAdminEmail` and `PlatformAdminPassword`: the platform admin, the login that adds colleges (an email address you can receive mail at).
   - Leave `Jwt` and `Storage` as they are; `EmailSettings` stays empty until email is set up (section 6).
6. Optional, recommended for the first start: in `C:\inetpub\wwwroot\gradesphere-api\web.config` set `stdoutLogEnabled="true"` and create the folder `C:\inetpub\wwwroot\gradesphere-api\logs` — any start-up error is then written there.

## 4. IIS Manager

1. **Application Pools → Add Application Pool**, twice: `GradeSphereApp-API` and `GradeSphereApp-Site`, both *.NET CLR version: No Managed Code*, *Integrated*.
2. Under the site for vivacollege.in (the one with `/ExamSoftware`): **Add Application**
   - Alias `gradesphere-api`, pool `GradeSphereApp-API`, path `C:\inetpub\wwwroot\gradesphere-api`
   - Alias `gradesphere`, pool `GradeSphereApp-Site`, path `C:\inetpub\wwwroot\gradesphere`
   (If `gradesphere` already exists from the old solution: select it → *Basic Settings* → change the path and pool.)
3. Folder rights (right-click the folder → Properties → Security → Edit → Add → type the name → OK):
   - `C:\inetpub\gradesphere-uploads`: `IIS AppPool\GradeSphereApp-API` → **Modify**
   - `C:\inetpub\wwwroot\gradesphere-api\logs` (if created): `IIS AppPool\GradeSphereApp-API` → **Modify**

## 5. First start

1. Open `https://vivacollege.in/gradesphere-api/api/health` → `{"status":"ok"}`. This first start also creates the platform admin.
2. Open `https://vivacollege.in/gradesphere` and sign in as the platform admin.
3. In `appsettings.Production.json` set `"PlatformAdminPassword": ""` and save (the admin already exists; the setting is only read on a start with no platform admin).
4. Platform → **New college**: pattern **NEP**, *Copy from: Starter: Engineering…* (or Pharmacy), branches, academic year, college admin(s).

**If the health page shows an error (500.x):** read the newest file in `C:\inetpub\wwwroot\gradesphere-api\logs`. Most common: wrong `Server=`, login name or password in `appsettings.Production.json`, or the login was not given on the `>>>` line.

## 6. Email for password reset (later)

Gmail: turn on 2-Step Verification on the sending account, create an **App Password** (Google Account → Security → App passwords), then in `appsettings.Production.json`:

```json
"EmailSettings": { "SmtpServer": "smtp.gmail.com", "Port": 587, "SenderEmail": "<account>@gmail.com", "Password": "<16-character app password>", "SenderName": "GradeSphere" }
```

## Installing a new version

1. Build again on the PC (step 0).
2. On the server, put a file named `app_offline.htm` into `C:\inetpub\wwwroot\gradesphere-api` (IIS stops the API).
3. Replace the files of both folders with the new zips' contents — **keep** `appsettings.Production.json` (and `logs`).
4. Delete `app_offline.htm`.
5. Run the new `GradeSphereApp-Setup.sql` in SSMS (same login name on the `>>>` line) — it only adds what changed.

The photos and signatures in `C:\inetpub\gradesphere-uploads` are not affected by updates.

---

*Alternative:* `server\Install-GradeSphere.ps1` (inside the API zip) does steps 3–4 automatically into `D:\GradeSphereApp\…`; see its header.
