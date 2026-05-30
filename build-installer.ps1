# CenterHub MSI Installer Build Script
# Run this script from the project root directory

param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$CertThumbprint = "7126F987C1D659A1D6366459DACF265DD80A16F7"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  CenterHub MSI Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the main application
Write-Host "[1/6] Building CenterHub application..." -ForegroundColor Yellow
dotnet build CenterHubNew.csproj -c $Configuration -p:Platform=$Platform
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to build the application" -ForegroundColor Red
    exit 1
}
Write-Host "Application build completed successfully!" -ForegroundColor Green
Write-Host ""

# Step 2: Publish the application (framework-dependent)
Write-Host "[2/6] Publishing CenterHub application..." -ForegroundColor Yellow

# Clean stale publish output so the WiX <Files> harvest doesn't pick up old dependencies
$publishDir = ".\publish\$Configuration"
if (Test-Path $publishDir) {
    Write-Host "  Cleaning stale publish directory..." -ForegroundColor Gray
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish CenterHubNew.csproj -c $Configuration -r win-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to publish the application" -ForegroundColor Red
    exit 1
}
Write-Host "Application published successfully!" -ForegroundColor Green
Write-Host ""

# Step 3: Sign the main executable
Write-Host "[3/6] Signing CenterHub executable..." -ForegroundColor Yellow
$signtool = $null
$signtoolPaths = @(
    "C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool\signtool.exe"
    "C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
    "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe"
)
foreach ($path in $signtoolPaths) {
    if (Test-Path $path) {
        $signtool = $path
        break
    }
}
# Fallback: search NuGet packages
if (-not $signtool) {
    $found = Get-ChildItem -Path "C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages" -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like "*x64*" } | Select-Object -First 1
    if ($found) { $signtool = $found.FullName }
}

if (-not $signtool) {
    Write-Host "WARNING: signtool.exe not found. Skipping code signing." -ForegroundColor DarkYellow
    Write-Host "  Install Windows SDK to enable signing." -ForegroundColor White
} else {
    Write-Host "  Using signtool: $signtool" -ForegroundColor Gray

    $exePath = ".\publish\$Configuration\CenterHubNew.exe"
    & $signtool sign /sha1 $CertThumbprint /fd SHA256 /t http://timestamp.digicert.com /v $exePath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Failed to sign executable. Continuing without signing." -ForegroundColor DarkYellow
    } else {
        Write-Host "Executable signed successfully!" -ForegroundColor Green
    }
}
Write-Host ""

# Step 4: Build the MSI installer
# Sync the WiX manifest version with csproj
$projectContent = Get-Content "CenterHubNew.csproj" -Raw
$versionMatch = [regex]::Match($projectContent, '<Version>(.*?)</Version>')
$msiVersion = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "1.0.0" }

$wxsPath = "installer\Package.wxs"
$wxsContent = Get-Content $wxsPath -Raw
$newWxsContent = [regex]::Replace($wxsContent, '<\?define Version = "[^"]*" \?>', "<?define Version = `"$msiVersion`" ?>")
if ($newWxsContent -ne $wxsContent) {
    Set-Content -Path $wxsPath -Value $newWxsContent -Encoding UTF8 -NoNewline
    Write-Host "  Stamped Package.wxs with version $msiVersion" -ForegroundColor Gray
}

Write-Host "[4/6] Building MSI installer (v$msiVersion)..." -ForegroundColor Yellow
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

# Sign the MSI
if ($signtool) {
    Write-Host "Signing MSI installer..." -ForegroundColor Yellow
    $msiSignPath = "installer\bin\x64\$Configuration\CenterHub.msi"
    & $signtool sign /sha1 $CertThumbprint /fd SHA256 /t http://timestamp.digicert.com /v $msiSignPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: Failed to sign MSI. Continuing without signing." -ForegroundColor DarkYellow
    } else {
        Write-Host "MSI signed successfully!" -ForegroundColor Green
    }
}
Write-Host ""

# Step 5: Extract version number from project file
Write-Host "[5/6] Extracting version number..." -ForegroundColor Yellow
$projectFile = "CenterHubNew.csproj"
if (-not (Test-Path $projectFile)) {
    Write-Host "ERROR: Project file not found: $projectFile" -ForegroundColor Red
    exit 1
}

$projectContent = Get-Content $projectFile -Raw
$versionMatch = [regex]::Match($projectContent, '<Version>(.*?)</Version>')
if (-not $versionMatch.Success) {
    Write-Host "ERROR: Could not find version in project file" -ForegroundColor Red
    exit 1
}

$version = $versionMatch.Groups[1].Value
Write-Host "Version found: $version" -ForegroundColor Green
Write-Host ""

# Step 6: Create ZIP archives with version number
Write-Host "[6/6] Creating ZIP archives..." -ForegroundColor Yellow

# MSI ZIP
$msiPath = (Resolve-Path "installer\bin\x64\$Configuration\CenterHub.msi").Path
$zipPath = "installer\bin\x64\$Configuration\CenterHub-v$version.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path $msiPath -DestinationPath $zipPath -Force
$zipFullPath = (Resolve-Path $zipPath).Path

# Portable ZIP
$portableZipPath = "installer\bin\x64\$Configuration\CenterHub-v$version-Portable.zip"
if (Test-Path $portableZipPath) {
    Remove-Item $portableZipPath -Force
}
Compress-Archive -Path "publish\$Configuration\*" -DestinationPath $portableZipPath -Force
$portableZipFullPath = (Resolve-Path $portableZipPath).Path

Write-Host "ZIP archives created successfully!" -ForegroundColor Green
Write-Host ""

Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "MSI installer location:" -ForegroundColor Cyan
Write-Host "  $msiPath" -ForegroundColor White
Write-Host ""
Write-Host "MSI ZIP archive location:" -ForegroundColor Cyan
Write-Host "  $zipFullPath" -ForegroundColor White
Write-Host ""
Write-Host "Portable ZIP location:" -ForegroundColor Cyan
Write-Host "  $portableZipFullPath" -ForegroundColor White
Write-Host ""
