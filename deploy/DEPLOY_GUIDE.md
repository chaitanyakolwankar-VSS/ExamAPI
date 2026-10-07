# GradeSphere — install on the Windows Server (IIS + SQL Server)

A fresh install: a new, empty database and nothing copied from the demo. The demo (`/ExamSoftware`, `/ExamAPI`) is not touched.

| | Value |
|---|---|
| Website | `https://vivacollege.in/gradesphere` (replaces the old solution at that address; its files are backed up) |
| API | `https://vivacollege.in/gradesphere-api` |
| Database | `GradeSphereApp` (an old `GradeSphere` database may exist — it is not touched) |
| Files on the server | `D:\GradeSphereApp\` → `api\`, `site\`, `uploads\` (photos, signatures, logos), `database\`, `backup\` |
| IIS app pools | `GradeSphereApp-API`, `GradeSphereApp-Site` |

**How the API reaches the database.** The API runs inside the IIS app pool `GradeSphereApp-API`. Windows gives every app pool its own account (`IIS AppPool\GradeSphereApp-API`). `CreateDatabase.sql` gives that account permission to read and write the `GradeSphereApp` database — so there is **no database password** to create, store or rotate. (This needs SQL Server on the same machine as IIS; otherwise see "SQL Server on another machine" at the end.)

Everything below is done on the server, over Remote Desktop.

---

## 1. Once: prerequisites

Skip what is already installed (the demo needs the same, so most likely all of it is).

1. **.NET 10 Hosting Bundle** — https://dotnet.microsoft.com/download/dotnet/10.0 → *ASP.NET Core Runtime* → *Hosting Bundle*. Install, then in an Administrator command prompt: `iisreset`.
2. **IIS URL Rewrite 2.1** — https://www.iis.net/downloads/microsoft/url-rewrite.
3. **SQL Server Management Studio (SSMS)** to run the database scripts.

The install script checks the first two and stops with a message if one is missing.

## 2. Get the two zips

**By hand (no GitHub on the server).** On the development PC, with everything committed:

```powershell
powershell -ExecutionPolicy Bypass -File D:\Projects\ReactApi\ExamAPI\deploy\Build-Package.ps1
```

It runs the tests, builds the API, the database script and the website, and writes `gradesphere-api-<date>.zip` and `gradesphere-site-<date>.zip` to `D:\Projects\ReactApi\_deploy`. Copy both to the server (Remote Desktop: copy on the PC, paste on the server's desktop or Downloads).

**Or from GitHub** (same zips, built on every push). Sign in to GitHub in the server's browser.

- `ExamAPI` repository → **Actions** → **Build API** → newest run with a green tick → at the bottom, **Artifacts** → download **gradesphere-api**.
- `ExamClient` repository → **Actions** → **Build website** → newest green run → download **gradesphere-site**.

You get `gradesphere-api.zip` and `gradesphere-site.zip` (e.g. in `C:\Users\<you>\Downloads`). Leave them zipped.

## 3. Run the install script

From `gradesphere-api.zip`, copy `server\Install-GradeSphere.ps1` to the desktop. Open **PowerShell as Administrator** (right-click → *Run as administrator*):

```powershell
Set-ExecutionPolicy -Scope Process Bypass
cd $HOME\Desktop
.\Install-GradeSphere.ps1 -ApiZip "$HOME\Downloads\gradesphere-api.zip" -SiteZip "$HOME\Downloads\gradesphere-site.zip"
```

- If the IIS site for vivacollege.in is not called *Default Web Site*, the script lists the sites — run again with `-SiteName "<name>"`.
- If SQL Server is a named instance (e.g. SQL Express), add `-SqlServer ".\SQLEXPRESS"`.
- It asks for the **platform admin** email and password — the login that adds colleges. Use an email address you can receive mail at (e.g. `edbalogin@vivacollege.in`), so "Forgot password" works later.

The script backs up the old `/gradesphere` files to `D:\GradeSphereApp\backup\`, creates the folders, the two app pools, the IIS applications `/gradesphere-api` and `/gradesphere`, the permissions, and `D:\GradeSphereApp\api\appsettings.Production.json` (with a new random login key). Email settings stay empty for now.

## 4. Create the database (SSMS, first install only)

Connect SSMS to the server's SQL Server as an administrator (e.g. `sa`).

1. Open `D:\GradeSphereApp\database\CreateDatabase.sql` → **Execute**. It creates `GradeSphereApp` and gives the API access.
2. Open `D:\GradeSphereApp\database\deploy.sql`, pick **GradeSphereApp** in the database drop-down → **Execute**. It creates all the tables.
3. In the Administrator PowerShell: `Restart-WebAppPool GradeSphereApp-API`, then open `https://vivacollege.in/gradesphere-api/api/health` in the browser — it shows `{"status":"ok"}`. This first start also creates the platform admin.
4. Back in SSMS: open `D:\GradeSphereApp\database\SeedStarterTemplates.sql`, pick **GradeSphereApp** → **Execute**. It adds the Engineering and Pharmacy starter templates (grade scales + Regular/ATKT rules).

## 5. Sign in and finish

1. Open `https://vivacollege.in/gradesphere` and sign in as the platform admin.
2. Open `D:\GradeSphereApp\api\appsettings.Production.json` in Notepad (as Administrator), set `"PlatformAdminPassword": ""`, save, and run `Restart-WebAppPool GradeSphereApp-API`.
3. Optional: in `D:\GradeSphereApp\api\web.config` set `stdoutLogEnabled="false"` (the first start had it on; logs are in `D:\GradeSphereApp\api\logs`).

## 6. Add a college

Platform → **New college**. On the Setup step keep the pattern **NEP** and choose **Copy from: Starter: Engineering…** or **Starter: Pharmacy…**. Add one or two college admins; they sign in and set up subjects, students and exams.

## 7. Email for password reset (later)

Gmail: turn on 2-Step Verification on the sending account, create an **App Password** (Google Account → Security → App passwords). Then in `appsettings.Production.json`:

```json
"EmailSettings": { "SmtpServer": "smtp.gmail.com", "Port": 587, "SenderEmail": "<account>@gmail.com", "Password": "<16-character app password>", "SenderName": "GradeSphere" }
```

Save and `Restart-WebAppPool GradeSphereApp-API`.

## 8. Smoke test

Per college: admin sign-in; one college's data not visible from another; subject + student; regular exam, seat numbers, marks, resolution dialog, process results; gazette, marksheet and hall ticket (banner and signatures); ATKT exam; Statistical Report; dashboard; declare result / release hall ticket; upload a student photo, install an update (below), the photo is still there.

## Installing a new version

1. Download the two newest artifacts (step 2).
2. Run the same command as in step 3. It takes the API offline for a moment, replaces the files and starts it again; `appsettings.Production.json`, uploads and logs are kept.
3. If the new version changed the database, run the new `D:\GradeSphereApp\database\deploy.sql` on **GradeSphereApp** in SSMS — it only adds what is missing.

## If something goes wrong

- **Site shows "500.30" or "HTTP Error 500"**: read the newest file in `D:\GradeSphereApp\api\logs`. Typical: the database is not created yet (step 4), or SQL Server is a named instance (fix `Server=` in `appsettings.Production.json`).
- **"Login failed for user 'IIS APPPOOL\GradeSphereApp-API'"**: run `CreateDatabase.sql` again.
- **Old site needed back**: its files are in `D:\GradeSphereApp\backup\old-gradesphere-<date>`; in IIS Manager point the `gradesphere` application back to its old folder and pool.

## SQL Server on another machine

Then the app pool's account cannot be used. In SSMS create a SQL login instead:

```sql
CREATE LOGIN gradesphereapp_user WITH PASSWORD = '<strong password>';
USE GradeSphereApp;
CREATE USER gradesphereapp_user FOR LOGIN gradesphereapp_user;
ALTER ROLE db_datareader ADD MEMBER gradesphereapp_user;
ALTER ROLE db_datawriter ADD MEMBER gradesphereapp_user;
```

and in `appsettings.Production.json` use `Server=<sql server>;Database=GradeSphereApp;User Id=gradesphereapp_user;Password=<password>;TrustServerCertificate=True;` (SQL Server must allow SQL Server authentication).

## Building by hand (without GitHub)

```bash
cd ExamAPI
dotnet ef migrations script --idempotent --project ExamAPI -o deploy/deploy.sql
dotnet publish ExamAPI/ExamAPI.csproj -c Release -o out/api
cd ../ExamClient
npm ci
npm run build:gradesphere
```

Zip so the layout matches the GitHub artifacts: API zip = `api\` (publish output), `database\` (`deploy.sql`, `CreateDatabase.sql`, `SeedStarterTemplates.sql`), `server\`; site zip = the contents of `dist`.
