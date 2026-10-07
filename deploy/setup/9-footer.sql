GO

-- ----------------------------------------------------------------------------- the website's access
USE GradeSphereApp;
GO
DECLARE @login sysname = (SELECT TOP 1 LoginName FROM ##GradeSphereAppLogin);
DROP TABLE ##GradeSphereAppLogin;

IF @login IS NULL
    PRINT 'Tables are ready. No login was given on the line marked >>>, so no access was granted (fine if the website uses sa or another admin login).';
ELSE IF SUSER_ID(@login) IS NULL
    PRINT 'Tables are ready, but the login "' + @login + '" does not exist on this server. Check the name on the line marked >>> and run again.';
ELSE IF IS_SRVROLEMEMBER('sysadmin', @login) = 1
    PRINT 'Done: GradeSphereApp is ready. "' + @login + '" is a server admin and already has full access.';
ELSE
BEGIN
    DECLARE @sql nvarchar(max) = N'';
    DECLARE @user sysname = (SELECT name FROM sys.database_principals WHERE sid = SUSER_SID(@login));
    IF @user IS NULL
    BEGIN
        SET @user = @login;
        SET @sql = N'CREATE USER ' + QUOTENAME(@user) + N' FOR LOGIN ' + QUOTENAME(@login) + N'; ';
    END
    SET @sql += N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@user) + N'; '
              + N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@user) + N';';
    EXEC (@sql);
    PRINT 'Done: GradeSphereApp is ready. "' + @login + '" can read and write it.';
END
GO
