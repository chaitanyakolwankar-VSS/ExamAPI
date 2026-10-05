# GradeSphere — install on IIS (Windows Server 2016 + SQL Server)

A fresh install: a new, empty database and nothing copied from the demo. The demo (`/ExamSoftware`, `/ExamAPI`) is not touched.

| | Value used in this guide |
|---|---|
| Website | `https://vivacollege.in/gradesphere` (replaces the old solution in that folder) |
| API | `https://vivacollege.in/gradesphere-api` |
| Database | `GradeSphere` |
| Uploads (photos, signatures, logos) | `D:\GradeSphere\uploads` — outside the website folders, so a redeploy never deletes them |
| Site files | `D:\GradeSphere\site` · API files `D:\GradeSphere\api` |

Change the paths if the server uses other drives; keep the same values in every step.

---

## 1. Prerequisites on the server (once)

- **ASP.NET Core 10 Hosting Bundle** (gives IIS the ASP.NET Core Module). Install, then run `iisreset`.
- **IIS URL Rewrite 2.1** (the website's `web.config` uses it to send every page address to `index.html`).
- **SQL Server Management Studio** (to run the database scripts).
- An **https certificate** for `vivacollege.in` bound to the site (the demo already uses it).

## 2. Build (on a development machine)

From a clean checkout of `ExamAPI` (`master`) and `ExamClient` (`main`), with no local changes:

```bash
cd ExamAPI
dotnet ef migrations has-pending-model-changes --project ExamAPI
dotnet ef migrations script --idempotent --project ExamAPI -o deploy/deploy.sql
dotnet publish ExamAPI/ExamAPI.csproj -c Release -o out/api
```

`has-pending-model-changes` must say *No changes*. The publish output never contains `appsettings.json` or `appsettings.Development.json` (they hold the demo connection); the server has its own settings file (step 4).

```bash
cd ExamClient
npm ci
npm run build:gradesphere
```

The website is in `ExamClient/dist` (built for `/gradesphere/` and `/gradesphere-api`, see `.env.gradesphere`; its `web.config` is generated for that path). Plain `npm run build` is the demo build — do not use it here.

Copy to the server: `out/api` → `D:\GradeSphere\api`, `dist` → `D:\GradeSphere\site`, plus `deploy/deploy.sql`, `deploy/SeedStarterTemplates.sql` and `deploy/appsettings.Production.example.json`.

## 3. Database

In SSMS, connected as an administrator:

1. `CREATE DATABASE GradeSphere;`
2. Open `deploy.sql`, select database **GradeSphere**, run it. (SSMS runs with `QUOTED_IDENTIFIER ON`, which the filtered unique indexes need. With `sqlcmd`, add **`-I`**.)
3. Create the login the app uses (not `sa`):
   ```sql
   CREATE LOGIN gradesphere_app WITH PASSWORD = '<strong password>';
   USE GradeSphere;
   CREATE USER gradesphere_app FOR LOGIN gradesphere_app;
   ALTER ROLE db_datareader ADD MEMBER gradesphere_app;
   ALTER ROLE db_datawriter ADD MEMBER gradesphere_app;
   ```
   The app only reads and writes data; schema changes are always run by an administrator with a reviewed script.

`SeedStarterTemplates.sql` runs in step 6, after the first start.

## 4. API settings

Copy `appsettings.Production.example.json` to `D:\GradeSphere\api\appsettings.Production.json` and fill in:

| Setting | What to put |
|---|---|
| `ConnectionStrings:DefaultConnection` | `Server=<sql server>;Database=GradeSphere;User Id=gradesphere_app;Password=<…>;TrustServerCertificate=True;` |
| `Jwt:Key` | A new random value (never the demo's). PowerShell: `$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)` |
| `Storage:UploadsRoot` | `D:\\GradeSphere\\uploads` |
| `EmailSettings` | The SMTP account that sends password-reset codes (server, port, sender address, password). |
| `Bootstrap:PlatformAdminEmail` / `PlatformAdminPassword` | The platform admin login, created on the first start (password at least 8 characters). |

The API refuses to start without a connection string or with a missing/short `Jwt:Key`. This file is not part of the publish output, so redeploys leave it alone. Keep it out of git.

## 5. IIS

1. Create `D:\GradeSphere\uploads` and `D:\GradeSphere\api\logs`.
2. **Application pools:** `GradeSphereAPI` and `GradeSphere`, both *.NET CLR version: No Managed Code*, pipeline *Integrated*.
3. Give `IIS AppPool\GradeSphereAPI` **Modify** on `D:\GradeSphere\uploads` and `D:\GradeSphere\api\logs`.
4. Under the `vivacollege.in` site:
   - **Back up and stop the old `/gradesphere` application first** (copy its folder somewhere safe).
   - Application **`gradesphere-api`** → `D:\GradeSphere\api`, pool `GradeSphereAPI`.
   - Application **`gradesphere`** → `D:\GradeSphere\site`, pool `GradeSphere` (replaces the old one).
5. First start only: in `D:\GradeSphere\api\web.config` set `stdoutLogEnabled="true"` and `stdoutLogFile=".\logs\stdout"`.

## 6. First start

1. Browse `https://vivacollege.in/gradesphere-api/api/Auth/login` once (a 405 or 400 answer is fine — it means the API is up). The first start creates the screen list and the platform admin; `logs\stdout*.log` shows `Startup: created the platform admin …`.
2. Run `SeedStarterTemplates.sql` against **GradeSphere** in SSMS. It adds two starter templates (`TPL-ENG` engineering, `TPL-PHM` pharmacy). They are not colleges: hidden from the college list, no logins. Safe to run again.
3. Open `https://vivacollege.in/gradesphere`, sign in as the platform admin.
4. **Remove `Bootstrap:PlatformAdminPassword`** from `appsettings.Production.json`, set `stdoutLogEnabled="false"`, recycle the `GradeSphereAPI` pool.

## 7. Adding a college

Platform → **New college**. On the Setup step keep the pattern **NEP** and choose **Copy from: Starter: Engineering…** or **Starter: Pharmacy…** — the college gets its own copy of the grade scale and the Regular + ATKT rule sets. Add one or two college admins; they sign in and set up subjects, students and exams.

## 8. Smoke test

Per college: admin sign-in; data of one college not visible from the other; subject + student; regular exam, seat numbers, marks, resolution dialog, process results; gazette, marksheet and hall ticket (banner and signatures); ATKT exam; Statistical Report; dashboard; declare result / release hall ticket; password reset by email; upload a student photo, redeploy, the photo is still there.

## Redeploying a new version

1. Build as in step 2. If the database changed, generate a new idempotent script (`dotnet ef migrations script --idempotent`), review it and run it in SSMS **before** copying the files.
2. Put an `app_offline.htm` in `D:\GradeSphere\api` (IIS stops the app), copy the new `out/api` files over (settings file and uploads are not touched), delete `app_offline.htm`.
3. Replace the contents of `D:\GradeSphere\site` with the new `dist`.

## Notes

- 10 sign-in/OTP attempts per minute and 5 reset mails per 15 minutes are allowed per IP; more get "Too many attempts".
- Unexpected errors show a generic message to the user; the details go to the log.
- `Cors:AllowedOrigins` stays empty: the website and the API are on the same host.
