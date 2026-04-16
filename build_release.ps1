$ErrorActionPreference = "Stop"

# 1. Get the current version from installer.iss
$issContent = Get-Content installer.iss
$versionLine = $issContent | Where-Object { $_ -match '#define MyAppVersion "(.*)"' }
if (-not $versionLine) { throw "Could not find MyAppVersion in installer.iss" }

$currentVersion = $matches[1]

# Auto-increment the patch number to suggest
$parts = $currentVersion.Split('.')
$suggestedVersion = ""
if ($parts.Length -eq 3) {
    $parts[2] = [int]$parts[2] + 1
    $suggestedVersion = $parts -join "."
}

# Prompt user for the new version
$newVersion = Read-Host "Current version is $currentVersion. Enter new version (Press Enter for $suggestedVersion)"
if ([string]::IsNullOrWhiteSpace($newVersion)) {
    $newVersion = $suggestedVersion
}

$tagName = "v$newVersion"
Write-Host "`n🚀 Preparing release for: $tagName" -ForegroundColor Cyan

# 2. Update installer.iss with the new version
(Get-Content installer.iss) -replace '#define MyAppVersion ".*"', "#define MyAppVersion ""$newVersion""" | Set-Content installer.iss

# 3. Create a git commit for the version bump
Write-Host "`n[1/4] Committing version bump to installer.iss..." -ForegroundColor Yellow
git add installer.iss
git commit -m "Bump version to $tagName"

# 4. Push the master branch first
Write-Host "`n[2/4] Pushing master branch to GitHub..." -ForegroundColor Yellow
git push origin master

# 5. Create the git tag locally
Write-Host "`n[3/4] Creating local tag: $tagName..." -ForegroundColor Yellow
git tag $tagName 2>$null

# 6. Push the tag to GitHub
Write-Host "`n[4/4] Pushing tag to GitHub to trigger the release workflow..." -ForegroundColor Yellow
git push origin $tagName

Write-Host "`n✅ Done! Head over to the 'Actions' tab on GitHub to watch it build!" -ForegroundColor Green
