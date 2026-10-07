<#
.SYNOPSIS
  Builds the two install zips on the development PC (instead of downloading them from GitHub Actions):
    gradesphere-api-<date>.zip   api\ (published API), database\ (deploy.sql, CreateDatabase.sql,
                                 SeedStarterTemplates.sql), server\ (Install-GradeSphere.ps1), guide
    gradesphere-site-<date>.zip  the website built for /gradesphere
  Copy both to the server (Remote Desktop copy/paste works) and run server\Install-GradeSphere.ps1 there.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File D:\Projects\ReactApi\ExamAPI\deploy\Build-Package.ps1
#>
param(
    [string] $ClientRepo = (Join-Path $PSScriptRoot "..\..\ExamClient"),
    [string] $Out = (Join-Path $PSScriptRoot "..\..\_deploy")
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
$apiCommit = (git -C $apiRepo log -1 --format="%h %s").Substring(0, [Math]::Min(80, (git -C $apiRepo log -1 --format="%h %s").Length))
$siteCommit = (git -C $ClientRepo log -1 --format="%h %s").Substring(0, [Math]::Min(80, (git -C $ClientRepo log -1 --format="%h %s").Length))

$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$work = Join-Path $env:TEMP "gradesphere-package-$stamp"
$apiPkg = Join-Path $work "api-pkg"
New-Item -ItemType Directory -Force -Path "$apiPkg\database", "$apiPkg\server" | Out-Null

Push-Location $apiRepo
try {
    Run "Test API" { dotnet test ExamAPI.Tests/ExamAPI.Tests.csproj -c Release --nologo -v q }
    Run "Model matches the migrations" { dotnet ef migrations has-pending-model-changes --project ExamAPI --configuration Release }
    Run "Database script" { dotnet ef migrations script --idempotent --project ExamAPI --configuration Release --no-build -o "$apiPkg\database\deploy.sql" }
    Run "Publish API" { dotnet publish ExamAPI/ExamAPI.csproj -c Release --nologo -v q -o "$apiPkg\api" }
}
finally { Pop-Location }

if (Test-Path "$apiPkg\api\appsettings.json") { throw "appsettings.json must not be in the publish output" }
Copy-Item "$apiRepo\deploy\CreateDatabase.sql", "$apiRepo\deploy\SeedStarterTemplates.sql" "$apiPkg\database\"
Copy-Item "$apiRepo\deploy\server\*" "$apiPkg\server\"
Copy-Item "$apiRepo\deploy\DEPLOY_GUIDE.md", "$apiRepo\deploy\appsettings.Production.example.json" $apiPkg
Set-Content "$apiPkg\COMMIT.txt" $apiCommit

Push-Location $ClientRepo
try { Run "Build website for /gradesphere" { npm run build:gradesphere --silent } }
finally { Pop-Location }
Set-Content "$ClientRepo\dist\COMMIT.txt" $siteCommit

$apiZip = Join-Path $Out "gradesphere-api-$stamp.zip"
$siteZip = Join-Path $Out "gradesphere-site-$stamp.zip"
Compress-Archive -Path "$apiPkg\*" -DestinationPath $apiZip -Force
Compress-Archive -Path "$ClientRepo\dist\*" -DestinationPath $siteZip -Force
Remove-Item $work -Recurse -Force

Write-Host "`nReady:" -ForegroundColor Green
Write-Host "  $apiZip   ($apiCommit)"
Write-Host "  $siteZip  ($siteCommit)"
Write-Host "Copy both to the server and follow DEPLOY_GUIDE.md from step 3."
