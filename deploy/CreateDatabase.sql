-- =======================================================================================
-- CreateDatabase.sql — run ONCE in SSMS, connected as an administrator (e.g. sa), on the server.
--
-- 1. Creates the empty database GradeSphereApp (leaves an existing one alone).
-- 2. Lets the website's API read and write it. The API runs in the IIS app pool
--    "GradeSphereApp-API"; Windows gives that pool its own account, "IIS AppPool\GradeSphereApp-API".
--    That account is given a SQL Server login here, so the connection needs NO password
--    (appsettings.Production.json says "Integrated Security=True").
--    Run Install-GradeSphere.ps1 first: the app pool must exist before its login can be created.
--
-- It only reads and writes data (db_datareader + db_datawriter); tables are created by deploy.sql,
-- which you run yourself as administrator.
--
-- Any other database on the server (e.g. an old "GradeSphere" from the previous solution) is not touched.
--
-- Only if SQL Server runs on ANOTHER machine than IIS: use a SQL login instead — see DEPLOY_GUIDE.md.
-- =======================================================================================
SET NOCOUNT ON;

IF DB_ID(N'GradeSphereApp') IS NULL
BEGIN
    CREATE DATABASE GradeSphereApp;
    PRINT 'Created database GradeSphereApp.';
END
ELSE
    PRINT 'Database GradeSphereApp already exists; left as it is.';
GO

IF SUSER_ID(N'IIS AppPool\GradeSphereApp-API') IS NULL
BEGIN
    CREATE LOGIN [IIS AppPool\GradeSphereApp-API] FROM WINDOWS WITH DEFAULT_DATABASE = GradeSphereApp;
    PRINT 'Created login IIS AppPool\GradeSphereApp-API.';
END
GO

USE GradeSphereApp;
GO

IF USER_ID(N'IIS AppPool\GradeSphereApp-API') IS NULL
    CREATE USER [IIS AppPool\GradeSphereApp-API] FOR LOGIN [IIS AppPool\GradeSphereApp-API];
ALTER ROLE db_datareader ADD MEMBER [IIS AppPool\GradeSphereApp-API];
ALTER ROLE db_datawriter ADD MEMBER [IIS AppPool\GradeSphereApp-API];
PRINT 'The API (IIS AppPool\GradeSphereApp-API) can read and write GradeSphereApp. Next: run deploy.sql on GradeSphereApp.';
GO
