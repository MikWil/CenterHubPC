# CenterHub MSI Installer Build Script
# Run this script from the project root directory

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  CenterHub MSI Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the main application
Write-Host "[1/3] Building CenterHub application..." -ForegroundColor Yellow
dotnet build CenterHubNew.csproj -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build the application" -ForegroundColor Red
    exit 1
}
Write-Host "Application build completed successfully!" -ForegroundColor Green
Write-Host ""

# Step 2: Publish the application (self-contained)
Write-Host "[2/3] Publishing CenterHub application..." -ForegroundColor Yellow
dotnet publish CenterHubNew.csproj -c $Configuration -r win-x64 --self-contained false -o ".\publish\$Configuration"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to publish the application" -ForegroundColor Red
    exit 1
}
Write-Host "Application published successfully!" -ForegroundColor Green
Write-Host ""

# Step 3: Build the MSI installer
Write-Host "[3/3] Building MSI installer..." -ForegroundColor Yellow
Push-Location installer
dotnet build CenterHub.wixproj -c $Configuration
$buildResult = $LASTEXITCODE
Pop-Location

if ($buildResult -ne 0) {
    Write-Host "ERROR: Failed to build the MSI installer" -ForegroundColor Red
    Write-Host ""
    Write-Host "Make sure you have the WiX Toolset installed:" -ForegroundColor Yellow
    Write-Host "  dotnet tool install --global wix" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host ""

# Step 4: Create ZIP archive
Write-Host "[4/4] Creating ZIP archive..." -ForegroundColor Yellow
$msiPath = (Resolve-Path "installer\bin\x64\$Configuration\CenterHub.msi").Path
$zipPath = "installer\bin\x64\$Configuration\CenterHub.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path $msiPath -DestinationPath $zipPath -Force
$zipFullPath = (Resolve-Path $zipPath).Path
Write-Host "ZIP archive created successfully!" -ForegroundColor Green
Write-Host ""

Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "MSI installer location:" -ForegroundColor Cyan
Write-Host "  $msiPath" -ForegroundColor White
Write-Host ""
Write-Host "ZIP archive location:" -ForegroundColor Cyan
Write-Host "  $zipFullPath" -ForegroundColor White
Write-Host ""

