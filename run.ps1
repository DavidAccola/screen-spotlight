# Kill any running instance
$existing = Get-Process -Name "SpotlightOverlay" -ErrorAction SilentlyContinue
if ($existing) { Stop-Process -Id $existing.Id -Force; Start-Sleep -Seconds 1 }

# Clean rebuild (avoids stale BAML cache issues after XAML changes)
Write-Host "Clean rebuilding..."
Remove-Item -Recurse -Force SpotlightOverlay\obj, SpotlightOverlay\bin -ErrorAction SilentlyContinue
dotnet build SpotlightOverlay
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed!"; exit 1 }

Write-Host "Launching SpotlightOverlay..."
dotnet run --project SpotlightOverlay
