<#
.SYNOPSIS
  Builds everything the server needs, on the development PC, into D:\Projects\ReactApi\_deploy:
    GradeSphereApi-<date>.zip     the published API     -> extract into D:\website\GradeSphereApp\GradeSphereApi
    GradeSphereClient-<date>.zip  the website           -> extract into D:\website\GradeSphereApp\GradeSphereClient
    GradeSphereApp-Setup.sql      the whole database     -> run once in SSMS
    appsettings.Production.json   the API's settings     -> into the GradeSphereApi folder (made once, with a
                                                            new random login key; kept on later builds)
    DEPLOY_GUIDE.md
  The zips hold the files directly (no inner folder), so each extracts straight into its IIS folder.
  The website is built for /gradesphereapp with the API at /gradesphereapi (ExamClient/.env.gradesphere).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File D:\Projects\ReactApi\ExamAPI\deploy\Build-Package.ps1
#>
param(
    [string] $ClientRepo = (Join-Path $PSScriptRoot "..\..\ExamClient"),
    [string] $Out = (Join-Path $PSScriptRoot "..\..\_deploy"),
    # Only used when appsettings.Production.json is made for the first time.
    [string] $SqlServer = "localhost",
    [string] $UploadsRoot = "D:\website\GradeSphereApp\GradeSphereUplodes"
)

$ErrorActionPreference = "Stop"
$apiRepo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ClientRepo = (Resolve-Path $ClientRepo).Path
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path

function Run($what, [scriptblock] $block) {
    Write-Host "== $what" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# Only committed code goes to the server: refuse a dirty working tree.
foreach ($repo in @($apiRepo, $ClientRepo)) {
    $dirty = git -C $repo status --porcelain --untracked-files=no
    if ($dirty) { throw "Uncommitted changes in $repo - commit them first:`n$dirty" }
}
$apiCommit = git -C $apiRepo log -1 --format="%h %s"
$siteCommit = git -C $ClientRepo log -1 --format="%h %s"

$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$work = Join-Path $env:TEMP "gradesphere-package-$stamp"
New-Item -ItemType Directory -Force -Path $work | Out-Null

Push-Location $apiRepo
try {
    Run "Test API" { dotnet test ExamAPI.Tests/ExamAPI.Tests.csproj -c Release --nologo -v q }
    Run "Model matches the migrations" { dotnet ef migrations has-pending-model-changes --project ExamAPI --configuration Release }
    Run "Database script" { dotnet ef migrations script --idempotent --project ExamAPI --configuration Release --no-build -o "$work\deploy.sql" }
    Run "Publish API" { dotnet publish ExamAPI/ExamAPI.csproj -c Release --nologo -v q -o "$work\api" }
}
finally { Pop-Location }

if (Test-Path "$work\api\appsettings.json") { throw "appsettings.json must not be in the publish output" }
Set-Content "$work\api\COMMIT.txt" $apiCommit

# One file for SSMS that builds the whole database: database, tables, starter templates, the login's access.
$setup = @(
    (Get-Content "$apiRepo\deploy\setup\1-header.sql" -Raw),
    (Get-Content "$work\deploy.sql" -Raw),
    "GO`r`n`r`n-- ----------------------------------------------------------------------------- starter templates`r`n",
    (Get-Content "$apiRepo\deploy\SeedStarterTemplates.sql" -Raw),
    (Get-Content "$apiRepo\deploy\setup\9-footer.sql" -Raw)
) -join "`r`n"
[IO.File]::WriteAllText((Join-Path $Out "GradeSphereApp-Setup.sql"), $setup, (New-Object Text.UTF8Encoding $true))

# The API's settings (first install only; never inside the zip, so an update cannot overwrite the
# server's copy). New random JWT key; the SQL login and the platform admin are filled in on the server.
$settingsOut = Join-Path $Out "appsettings.Production.json"
if (-not (Test-Path $settingsOut)) {
    $keyBytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)
    $config = [ordered]@{
        Logging           = [ordered]@{ LogLevel = [ordered]@{ Default = "Information"; "Microsoft.AspNetCore" = "Warning"; "Microsoft.EntityFrameworkCore" = "Warning" } }
        AllowedHosts      = "*"
        ConnectionStrings = [ordered]@{ DefaultConnection = "Server=$SqlServer;Database=GradeSphereApp;User Id=YOUR_EXISTING_SQL_LOGIN;Password=ITS_PASSWORD;TrustServerCertificate=True;" }
        Jwt               = [ordered]@{ Key = [Convert]::ToBase64String($keyBytes); Issuer = "GradeSphere"; Audience = "GradeSphere" }
        Storage           = [ordered]@{ UploadsRoot = $UploadsRoot }
        EmailSettings     = [ordered]@{ SmtpServer = ""; Port = 587; SenderEmail = ""; Password = ""; SenderName = "GradeSphere" }
        Cors              = [ordered]@{ AllowedOrigins = @("https://vivacollege.in", "https://www.vivacollege.in") }
        Bootstrap         = [ordered]@{ PlatformAdminEmail = ""; PlatformAdminPassword = "" }
    }
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsOut -Encoding UTF8
}
Copy-Item "$apiRepo\deploy\DEPLOY_GUIDE.md" $Out -Force

Push-Location $ClientRepo
try { Run "Build website for /gradesphereapp" { npm run build:gradesphere --silent } }
finally { Pop-Location }
Set-Content "$ClientRepo\dist\COMMIT.txt" $siteCommit

$apiZip = Join-Path $Out "GradeSphereApi-$stamp.zip"
$siteZip = Join-Path $Out "GradeSphereClient-$stamp.zip"
Compress-Archive -Path "$work\api\*" -DestinationPath $apiZip -Force
Compress-Archive -Path "$ClientRepo\dist\*" -DestinationPath $siteZip -Force
Remove-Item $work -Recurse -Force

Write-Host "`nReady in ${Out}:" -ForegroundColor Green
Write-Host "  $(Split-Path $apiZip -Leaf)    ($apiCommit)"
Write-Host "  $(Split-Path $siteZip -Leaf)  ($siteCommit)"
Write-Host "  GradeSphereApp-Setup.sql, appsettings.Production.json, DEPLOY_GUIDE.md"
