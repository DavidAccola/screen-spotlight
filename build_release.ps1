$ErrorActionPreference = "Stop"

# 1. Get version from installer.iss
$issContent = Get-Content installer.iss
$versionLine = $issContent | Where-Object { $_ -match '#define MyAppVersion "(.*)"' }
if (-not $versionLine) { throw "Could not find MyAppVersion in installer.iss" }
$version = $matches[1]

Write-Host "Building Release v$version..." -ForegroundColor Cyan

# 2. Dotnet publish
Write-Host "`n[1/3] Publishing .NET project..." -ForegroundColor Yellow
$existing = Get-Process -Name "Screen Spotlight" -ErrorAction SilentlyContinue
if ($existing) { Stop-Process -Id $existing.Id -Force; Start-Sleep -Seconds 1 }
dotnet publish SpotlightOverlay\SpotlightOverlay.csproj -c Release

# 3. Create ZIP portable release
Write-Host "`n[2/3] Creating Portable ZIP..." -ForegroundColor Yellow
$publishDir = "SpotlightOverlay\bin\Release\net8.0-windows\win-x64\publish"
$zipOutDir = "installer-output"
if (-not (Test-Path $zipOutDir)) { New-Item -ItemType Directory -Path $zipOutDir | Out-Null }

$tempDir = Join-Path $env:TEMP "Screen Spotlight"
if (Test-Path $tempDir) { Remove-Item -Recurse -Force $tempDir }
New-Item -ItemType Directory -Path $tempDir | Out-Null

Copy-Item -Path "$publishDir\*" -Destination $tempDir -Recurse
$zipPath = Join-Path $zipOutDir "Screen Spotlight v$version.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

Compress-Archive -Path $tempDir -DestinationPath $zipPath
Remove-Item -Recurse -Force $tempDir

Write-Host "Created $zipPath" -ForegroundColor Green

# 4. Compile Inno Setup installer
Write-Host "`n[3/3] Compiling Inno Setup Installer..." -ForegroundColor Yellow
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

Write-Host "`nDone! Release artifacts are ready in the '$zipOutDir' folder." -ForegroundColor Green
