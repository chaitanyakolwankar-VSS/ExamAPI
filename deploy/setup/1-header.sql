-- =======================================================================================
-- GradeSphereApp-Setup.sql  -  creates the whole GradeSphere database in one go.
--
-- How: open in SSMS (connected to the live SQL Server as an admin), type the name of the
-- existing SQL login the website will use on the line marked >>>, press Execute. A few seconds.
--
-- It creates ONLY the database  GradeSphereApp  (an existing one is reused, never dropped), all its
-- tables and the Engineering + Pharmacy starter templates, and lets the existing login read and
-- write it. No login is created, no password is changed, no other database is touched.
-- Safe to run again: everything that already exists is skipped.
--
-- The website connects with that login: put its name and password into appsettings.Production.json
-- (ConnectionStrings) in the API folder.
-- =======================================================================================

-- >>> The existing SQL login the website will use (e.g. the one you already use for other sites):
DECLARE @AppLogin sysname = N'';

SET NOCOUNT ON;

IF DB_ID(N'GradeSphereApp') IS NULL
BEGIN
    CREATE DATABASE GradeSphereApp;
    PRINT 'Created database GradeSphereApp.';
END
ELSE PRINT 'Database GradeSphereApp exists; adding only what is missing.';

-- Remembered for the last step (the variable does not survive GO).
IF OBJECT_ID('tempdb..##GradeSphereAppLogin') IS NOT NULL DROP TABLE ##GradeSphereAppLogin;
CREATE TABLE ##GradeSphereAppLogin (LoginName sysname NULL);
INSERT INTO ##GradeSphereAppLogin VALUES (NULLIF(LTRIM(RTRIM(@AppLogin)), N''));
GO

USE GradeSphereApp;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- ----------------------------------------------------------------------------- tables
