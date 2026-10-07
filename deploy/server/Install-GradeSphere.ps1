<#
.SYNOPSIS
  Installs or updates GradeSphere on IIS (Windows Server 2016+). Run in PowerShell **as Administrator**.

.DESCRIPTION
  Uses the two zips downloaded from GitHub Actions (see DEPLOY_GUIDE.md):
    gradesphere-api.zip   (Build API run, ExamAPI repo)
    gradesphere-site.zip  (Build website run, ExamClient repo)

  First install: creates D:\GradeSphereApp\{api,site,uploads,backup}, the app pools GradeSphereApp-API and
  GradeSphereApp-Site, the IIS applications /gradesphere-api and /gradesphere under the site, folder
  permissions, and appsettings.Production.json (asks for the platform admin login; makes a new JWT key).
  An existing /gradesphere application (the old solution) is backed up first and then pointed at the new files.

  Later runs (new version): stops the API with app_offline.htm, replaces the files, keeps
  appsettings.Production.json, the uploads and the logs, starts it again.

  The database is NOT touched here; that is done in SSMS (CreateDatabase.sql, deploy.sql) - see the guide.

.EXAMPLE
  .\Install-GradeSphere.ps1 -ApiZip C:\Downloads\gradesphere-api.zip -SiteZip C:\Downloads\gradesphere-site.zip
  .\Install-GradeSphere.ps1 -ApiZip ... -SiteZip ... -SiteName "vivacollege.in" -SqlServer ".\SQLEXPRESS"
#>
param(
    [Parameter(Mandatory = $true)] [string] $ApiZip,
    [Parameter(Mandatory = $true)] [string] $SiteZip,
    # The IIS web site that answers for vivacollege.in (the one the demo /ExamSoftware lives under).
    [string] $SiteName = "Default Web Site",
    [string] $Root = "D:\GradeSphereApp",
    [string] $ApiApp = "gradesphere-api",
    [string] $SiteApp = "gradesphere",
    # SQL Server as seen from this machine, e.g. "localhost" or ".\SQLEXPRESS".
    [string] $SqlServer = "localhost",
    [string] $Database = "GradeSphereApp"
)

$ErrorActionPreference = "Stop"
$ApiPool = "GradeSphereApp-API"
$SitePool = "GradeSphereApp-Site"

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "   $text" -ForegroundColor Green }
function Fail($text) { Write-Host "`n   $text" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------------------------- checks
Step "Checks"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Fail "Run PowerShell as Administrator (right-click > Run as administrator)." }
foreach ($zip in @($ApiZip, $SiteZip)) { if (-not (Test-Path $zip)) { Fail "Not found: $zip" } }

Import-Module WebAdministration
if (-not (Get-Website -Name $SiteName)) {
    Write-Host "   IIS sites on this server:"; Get-Website | ForEach-Object { Write-Host "     - $($_.Name)  ($($_.Bindings.Collection.bindingInformation -join ', '))" }
    Fail "IIS site '$SiteName' not found. Re-run with -SiteName ""<the site for vivacollege.in>""."
}
if (-not (Get-WebGlobalModule -Name "AspNetCoreModuleV2" -ErrorAction SilentlyContinue)) {
    Fail "ASP.NET Core Module missing. Install the .NET 10 'Hosting Bundle' (https://dotnet.microsoft.com/download/dotnet/10.0), run 'iisreset', then run this again."
}
$runtimes = & dotnet --list-runtimes 2>$null
if (-not ($runtimes -match "Microsoft\.AspNetCore\.App 10\.")) {
    Fail "ASP.NET Core 10 runtime not found. Install the .NET 10 'Hosting Bundle', run 'iisreset', then run this again."
}
if (-not (Get-WebGlobalModule -Name "RewriteModule" -ErrorAction SilentlyContinue)) {
    Fail "IIS URL Rewrite missing. Install 'URL Rewrite 2.1' (https://www.iis.net/downloads/microsoft/url-rewrite), then run this again."
}
Ok "Administrator, IIS site '$SiteName', Hosting Bundle 10, URL Rewrite: OK"

# ---------------------------------------------------------------------------------- unpack
Step "Unpacking"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$temp = Join-Path $env:TEMP "gradesphere-$stamp"
Expand-Archive -Path $ApiZip -DestinationPath "$temp\api-zip"
Expand-Archive -Path $SiteZip -DestinationPath "$temp\site-zip"
$apiSource = "$temp\api-zip\api"
if (-not (Test-Path "$apiSource\ExamAPI.dll")) { Fail "The API zip has no api\ExamAPI.dll. Download 'gradesphere-api' from the Build API run." }
if (-not (Test-Path "$temp\site-zip\index.html")) { Fail "The site zip has no index.html. Download 'gradesphere-site' from the Build website run." }
Ok "API build $(Get-Content "$temp\api-zip\COMMIT.txt" -ErrorAction SilentlyContinue), site build $(Get-Content "$temp\site-zip\COMMIT.txt" -ErrorAction SilentlyContinue)"

# ---------------------------------------------------------------------------------- folders
Step "Folders under $Root"
foreach ($d in @("api", "api\logs", "site", "uploads", "backup", "database")) { New-Item -ItemType Directory -Force -Path (Join-Path $Root $d) | Out-Null }
Copy-Item "$temp\api-zip\database\*" (Join-Path $Root "database") -Force
Copy-Item "$temp\api-zip\DEPLOY_GUIDE.md", "$temp\api-zip\server\*" $Root -Force -ErrorAction SilentlyContinue
Ok "api, site, uploads, backup, database (SQL scripts copied there)"

# ---------------------------------------------------------------------------------- old /gradesphere
$existingSite = Get-WebApplication -Site $SiteName -Name $SiteApp
if ($existingSite -and ($existingSite.PhysicalPath -ne (Join-Path $Root "site"))) {
    Step "Backing up the old /$SiteApp application"
    $oldPath = [Environment]::ExpandEnvironmentVariables($existingSite.PhysicalPath)
    $backup = Join-Path $Root "backup\old-$SiteApp-$stamp"
    Copy-Item $oldPath $backup -Recurse -Force
    Ok "Copied $oldPath to $backup (the old folder itself is left in place)"
}

# ---------------------------------------------------------------------------------- app pools
Step "Application pools"
foreach ($pool in @($ApiPool, $SitePool)) {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ""
    Set-ItemProperty "IIS:\AppPools\$pool" -Name managedPipelineMode -Value "Integrated"
}
Ok "$ApiPool, $SitePool (No Managed Code)"

# ---------------------------------------------------------------------------------- files
Step "Copying files"
$apiDir = Join-Path $Root "api"
$offline = Join-Path $apiDir "app_offline.htm"
$firstInstall = -not (Test-Path (Join-Path $apiDir "ExamAPI.dll"))
if (-not $firstInstall) { Set-Content $offline "<h3>GradeSphere is being updated. Please try again in a minute.</h3>"; Start-Sleep -Seconds 3 }

# /MIR replaces the files; the settings file, app_offline and the logs are excluded, so they stay.
robocopy $apiSource $apiDir /MIR /XF appsettings.Production.json app_offline.htm /XD logs /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fail "Copying the API failed (robocopy $LASTEXITCODE)." }
robocopy "$temp\site-zip" (Join-Path $Root "site") /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Fail "Copying the website failed (robocopy $LASTEXITCODE)." }
Ok "API -> $apiDir, website -> $(Join-Path $Root 'site')"

# ---------------------------------------------------------------------------------- settings
$settings = Join-Path $apiDir "appsettings.Production.json"
if (-not (Test-Path $settings)) {
    Step "Settings (first install)"
    Write-Host "   The platform admin is the login that adds colleges. It must look like an email address."
    $adminEmail = Read-Host "   Platform admin email (e.g. edbalogin@vivacollege.in)"
    do {
        $p1 = Read-Host "   Platform admin password (min 8 characters)" -AsSecureString
        $p2 = Read-Host "   Repeat the password" -AsSecureString
        $plain1 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p1))
        $plain2 = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p2))
        if ($plain1 -ne $plain2) { Write-Host "   The passwords differ, try again." -ForegroundColor Yellow }
        elseif ($plain1.Length -lt 8) { Write-Host "   At least 8 characters, try again." -ForegroundColor Yellow }
    } while ($plain1 -ne $plain2 -or $plain1.Length -lt 8)

    $keyBytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)

    $config = [ordered]@{
        Logging           = [ordered]@{ LogLevel = [ordered]@{ Default = "Information"; "Microsoft.AspNetCore" = "Warning"; "Microsoft.EntityFrameworkCore" = "Warning" } }
        AllowedHosts      = "*"
        # Windows login of the API's app pool: no database password to keep (CreateDatabase.sql grants it access).
        ConnectionStrings = [ordered]@{ DefaultConnection = "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=True;" }
        Jwt               = [ordered]@{ Key = [Convert]::ToBase64String($keyBytes); Issuer = "GradeSphere"; Audience = "GradeSphere" }
        Storage           = [ordered]@{ UploadsRoot = (Join-Path $Root "uploads") }
        # Filled in later for the password-reset mails (Gmail: smtp.gmail.com, 587, App Password).
        EmailSettings     = [ordered]@{ SmtpServer = ""; Port = 587; SenderEmail = ""; Password = ""; SenderName = "GradeSphere" }
        Cors              = [ordered]@{ AllowedOrigins = @() }
        Bootstrap         = [ordered]@{ PlatformAdminEmail = $adminEmail; PlatformAdminPassword = $plain1 }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $settings -Encoding UTF8
    $plain1 = $null; $plain2 = $null

    # Only the administrators and the API's app pool may read it (it holds the JWT key and, until the
    # first sign-in, the platform admin password).
    icacls $settings /inheritance:r /grant:r "Administrators:F" "SYSTEM:F" "IIS AppPool\${ApiPool}:R" | Out-Null
    Ok "Wrote $settings (new JWT key; database via the app pool's Windows login)"
}
else {
    Ok "Kept the existing appsettings.Production.json"
}

# ---------------------------------------------------------------------------------- IIS applications
Step "IIS applications under '$SiteName'"
foreach ($app in @(@{ Name = $ApiApp; Path = $apiDir; Pool = $ApiPool }, @{ Name = $SiteApp; Path = (Join-Path $Root "site"); Pool = $SitePool })) {
    if (Get-WebApplication -Site $SiteName -Name $app.Name) {
        Set-ItemProperty "IIS:\Sites\$SiteName\$($app.Name)" -Name physicalPath -Value $app.Path
        Set-ItemProperty "IIS:\Sites\$SiteName\$($app.Name)" -Name applicationPool -Value $app.Pool
    }
    else {
        New-WebApplication -Site $SiteName -Name $app.Name -PhysicalPath $app.Path -ApplicationPool $app.Pool | Out-Null
    }
    Ok "/$($app.Name) -> $($app.Path)  (pool $($app.Pool))"
}

# ---------------------------------------------------------------------------------- permissions
Step "Folder permissions"
icacls (Join-Path $Root "site") /grant "IIS AppPool\${SitePool}:(OI)(CI)RX" "IIS_IUSRS:(OI)(CI)RX" | Out-Null
icacls $apiDir /grant "IIS AppPool\${ApiPool}:(OI)(CI)RX" | Out-Null
icacls (Join-Path $Root "uploads") /grant "IIS AppPool\${ApiPool}:(OI)(CI)M" | Out-Null
icacls (Join-Path $apiDir "logs") /grant "IIS AppPool\${ApiPool}:(OI)(CI)M" | Out-Null
Ok "API pool: read the app, change uploads + logs; site pool: read the website"

# ---------------------------------------------------------------------------------- start
Step "Starting"
if ($firstInstall) {
    # Startup messages (and any startup error) go to logs\stdout_*.log on the first start.
    $webConfig = Join-Path $apiDir "web.config"
    [xml]$xml = Get-Content $webConfig
    $aspNetCore = $xml.SelectSingleNode("//aspNetCore")
    $aspNetCore.SetAttribute("stdoutLogEnabled", "true")
    $aspNetCore.SetAttribute("stdoutLogFile", ".\logs\stdout")
    $xml.Save($webConfig)
}
Remove-Item $offline -ErrorAction SilentlyContinue
Restart-WebAppPool -Name $ApiPool -ErrorAction SilentlyContinue
Restart-WebAppPool -Name $SitePool -ErrorAction SilentlyContinue
Remove-Item $temp -Recurse -Force
Ok "Done."

if ($firstInstall) {
    Write-Host "`nNEXT (first install), in SQL Server Management Studio - see DEPLOY_GUIDE.md section 4:" -ForegroundColor Yellow
    Write-Host "  1. Open $Root\database\CreateDatabase.sql and run it (creates '$Database', gives IIS AppPool\$ApiPool access)."
    Write-Host "  2. Open $Root\database\deploy.sql, choose database '$Database', run it."
    Write-Host "  3. Back here: Restart-WebAppPool $ApiPool  -- then open https://<your site>/$ApiApp/api/health (shows status ok;"
    Write-Host "     this first start creates the platform admin). Then run $Root\database\SeedStarterTemplates.sql on '$Database'."
    Write-Host "  4. Sign in at https://<your site>/$SiteApp with the platform admin, then remove the password from"
    Write-Host "     Bootstrap in $settings and run: Restart-WebAppPool $ApiPool"
}
else {
    Write-Host "`nUpdated. If this version changed the database, run the new $Root\database\deploy.sql in SSMS (it skips what is already there)." -ForegroundColor Yellow
}
