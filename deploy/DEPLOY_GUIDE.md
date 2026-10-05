# GradeSphere — install on IIS (Windows Server 2016 + SQL Server)

A fresh install: a new, empty database and nothing copied from the demo. The demo (`/ExamSoftware`, `/ExamAPI`) is not touched.

| | Value used in this guide |
|---|---|
| Website | `https://vivacollege.in/gradesphere` (replaces the old solution in that folder) |
| API | `https://vivacollege.in/gradesphere-api` |
| Database | `GradeSphereApp` (a `GradeSphere` database from the old solution may exist — do not touch it) |
| Uploads (photos, signatures, logos) | `D:\GradeSphereApp\uploads` — outside the website folders, so a redeploy never deletes them |
| Site files | `D:\GradeSphereApp\site` · API files `D:\GradeSphereApp\api` |

Change the paths if the server uses other drives; keep the same values in every step.

---

## 1. Prerequisites on the server (once)

- **ASP.NET Core 10 Hosting Bundle** (gives IIS the ASP.NET Core Module). Install, then run `iisreset`.
- **IIS URL Rewrite 2.1** (the website's `web.config` uses it to send every page address to `index.html`).
- **SQL Server Management Studio** (to run the database scripts).
- An **https certificate** for `vivacollege.in` bound to the site (the demo already uses it).

## 2. Get the build

**From GitHub (normal way).** Every push to `master` (ExamAPI) and `main` (ExamClient) is built and tested by GitHub Actions. On the server, sign in to GitHub in the browser:

- `ExamAPI` repo → **Actions** → latest green **Build API** run → download **gradesphere-api** (zip): `api/` (the published API), `database/deploy.sql`, `database/SeedStarterTemplates.sql`, this guide, `appsettings.Production.example.json`, `COMMIT.txt`.
- `ExamClient` repo → **Actions** → latest green **Build website** run → download **gradesphere-site** (zip): the website for `/gradesphere`.

Unzip: `api/` → `D:\GradeSphereApp\api`, the site zip → `D:\GradeSphereApp\site`. Artifacts are kept 30 days; re-run the workflow (**Run workflow**) for a fresh one. Both builds should come from the same day's pushes.

**By hand (alternative),** from a clean checkout of `ExamAPI` (`master`) and `ExamClient` (`main`), with no local changes:

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

Copy to the server: `out/api` → `D:\GradeSphereApp\api`, `dist` → `D:\GradeSphereApp\site`, plus `deploy/deploy.sql`, `deploy/SeedStarterTemplates.sql` and `deploy/appsettings.Production.example.json`.

## 3. Database

In SSMS, connected as an administrator:

1. `CREATE DATABASE GradeSphereApp;` — first check `SELECT name FROM sys.databases;` and leave any existing `GradeSphere` database (the old solution) untouched
2. Open `deploy.sql`, select database **GradeSphereApp**, run it. (SSMS runs with `QUOTED_IDENTIFIER ON`, which the filtered unique indexes need. With `sqlcmd`, add **`-I`**.)
3. Create the login the app uses (not `sa`):
   ```sql
   CREATE LOGIN gradesphereapp_user WITH PASSWORD = '<strong password>';
   USE GradeSphereApp;
   CREATE USER gradesphereapp_user FOR LOGIN gradesphereapp_user;
   ALTER ROLE db_datareader ADD MEMBER gradesphereapp_user;
   ALTER ROLE db_datawriter ADD MEMBER gradesphereapp_user;
   ```
   The app only reads and writes data; schema changes are always run by an administrator with a reviewed script.

`SeedStarterTemplates.sql` runs in step 6, after the first start.

## 4. API settings

Copy `appsettings.Production.example.json` to `D:\GradeSphereApp\api\appsettings.Production.json` and fill in:

| Setting | What to put |
|---|---|
| `ConnectionStrings:DefaultConnection` | `Server=<sql server>;Database=GradeSphereApp;User Id=gradesphereapp_user;Password=<…>;TrustServerCertificate=True;` |
| `Jwt:Key` | A new random value (never the demo's). PowerShell: `$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)` |
| `Storage:UploadsRoot` | `D:\\GradeSphereApp\\uploads` |
| `EmailSettings` | The SMTP account that sends password-reset codes (server, port, sender address, password). |
| `Bootstrap:PlatformAdminEmail` / `PlatformAdminPassword` | The platform admin login, created on the first start (password at least 8 characters). |

The API refuses to start without a connection string or with a missing/short `Jwt:Key`. This file is not part of the publish output, so redeploys leave it alone. Keep it out of git.

## 5. IIS

1. Create `D:\GradeSphereApp\uploads` and `D:\GradeSphereApp\api\logs`.
2. **Application pools:** `GradeSphereApp-API` and `GradeSphereApp-Site` (new names, so nothing of the old solution is reused), both *.NET CLR version: No Managed Code*, pipeline *Integrated*.
3. Give `IIS AppPool\GradeSphereApp-API` **Modify** on `D:\GradeSphereApp\uploads` and `D:\GradeSphereApp\api\logs`.
4. Under the `vivacollege.in` site:
   - **Back up and stop the old `/gradesphere` application first** (copy its folder somewhere safe).
   - Application **`gradesphere-api`** → `D:\GradeSphereApp\api`, pool `GradeSphereApp-API`.
   - Application **`gradesphere`** → `D:\GradeSphereApp\site`, pool `GradeSphereApp-Site` (replaces the old application; leave the old pool and folder alone).
5. First start only: in `D:\GradeSphereApp\api\web.config` set `stdoutLogEnabled="true"` and `stdoutLogFile=".\logs\stdout"`.

## 6. First start

1. Browse `https://vivacollege.in/gradesphere-api/api/Auth/login` once (a 405 or 400 answer is fine — it means the API is up). The first start creates the screen list and the platform admin; `logs\stdout*.log` shows `Startup: created the platform admin …`.
2. Run `SeedStarterTemplates.sql` against **GradeSphereApp** in SSMS. It adds two starter templates (`TPL-ENG` engineering, `TPL-PHM` pharmacy). They are not colleges: hidden from the college list, no logins. Safe to run again.
3. Open `https://vivacollege.in/gradesphere`, sign in as the platform admin.
4. **Remove `Bootstrap:PlatformAdminPassword`** from `appsettings.Production.json`, set `stdoutLogEnabled="false"`, recycle the `GradeSphereApp-API` pool.

## 7. Adding a college

Platform → **New college**. On the Setup step keep the pattern **NEP** and choose **Copy from: Starter: Engineering…** or **Starter: Pharmacy…** — the college gets its own copy of the grade scale and the Regular + ATKT rule sets. Add one or two college admins; they sign in and set up subjects, students and exams.

## 8. Smoke test

Per college: admin sign-in; data of one college not visible from the other; subject + student; regular exam, seat numbers, marks, resolution dialog, process results; gazette, marksheet and hall ticket (banner and signatures); ATKT exam; Statistical Report; dashboard; declare result / release hall ticket; password reset by email; upload a student photo, redeploy, the photo is still there.

## Redeploying a new version

1. Build as in step 2. If the database changed, generate a new idempotent script (`dotnet ef migrations script --idempotent`), review it and run it in SSMS **before** copying the files.
2. Put an `app_offline.htm` in `D:\GradeSphereApp\api` (IIS stops the app), copy the new `out/api` files over (settings file and uploads are not touched), delete `app_offline.htm`.
3. Replace the contents of `D:\GradeSphereApp\site` with the new `dist`.

## Notes

- 10 sign-in/OTP attempts per minute and 5 reset mails per 15 minutes are allowed per IP; more get "Too many attempts".
- Unexpected errors show a generic message to the user; the details go to the log.
- `Cors:AllowedOrigins` stays empty: the website and the API are on the same host.
