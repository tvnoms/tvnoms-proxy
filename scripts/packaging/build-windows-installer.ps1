#!/usr/bin/env pwsh
# Run from the repository root (all paths below are relative to it).
param(
    [string]$Version = "",
    [string]$OutputDir = "publish/packages"
)

# Get version from .csproj if not provided
if ([string]::IsNullOrEmpty($Version)) {
    [xml]$csproj = Get-Content "proxy/tvnoms-proxy.csproj"
    $Version = $csproj.Project.PropertyGroup.Version
}

# Remove 'v' prefix if present and trim whitespace
$Version = ($Version -replace '^v', '').Trim()

Write-Host "Building Windows installer for version $Version" -ForegroundColor Green

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Build the application
Write-Host "Building application..." -ForegroundColor Yellow
dotnet publish proxy/tvnoms-proxy.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:DefineConstants=WINDOWS `
    -o publish/win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Remove development configuration
Remove-Item -Path "publish/win-x64/appsettings.Development.json" -ErrorAction SilentlyContinue

# Build the tray icon into the same output folder, so Velopack bundles both into one installer.
# The proxy service and the tray are separate processes at runtime (the service has no desktop) —
# see UpdateManager's first-run hook, which launches tvnoms-tray.exe once after installing the
# service. The tray itself follows the service's lifecycle (it exits when the service stops).
Write-Host "Building tray icon..." -ForegroundColor Yellow
dotnet publish tray/tvnoms-tray.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o publish/win-x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "Tray build failed!"
    exit 1
}

# Install Velopack if not present
if (-not (Test-Path "tools/vpk")) {
    Write-Host "Installing Velopack..." -ForegroundColor Yellow
    dotnet tool install --tool-path tools vpk
}

Write-Host "Creating Velopack installer..." -ForegroundColor Yellow

# Use the pre-converted ICO file
$iconPath = "scripts/packaging/branding/app-icon.ico"

# Build installer with Velopack
$vpkArgs = @(
    "pack",
    "--packId", "TvnomsProxy",
    "--packVersion", $Version,
    "--packDir", "publish/win-x64",
    "--mainExe", "tvnoms-proxy.exe",
    "--outputDir", "publish/releases",
    "--packTitle", "TV Noms Proxy",
    "--packAuthors", "TV Noms"
    # Note: Velopack doesn't create desktop shortcuts by default
    # Shortcut creation is controlled via the installer options, not vpk pack flags
)

# Add icon if available
if ($iconPath -and (Test-Path $iconPath)) {
    $vpkArgs += "--icon"
    $vpkArgs += $iconPath
}

& "tools/vpk" @vpkArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Velopack pack failed!"
    exit 1
}

# Copy installer to output directory
$installerName = "TvnomsProxy-Setup-$Version.exe"
$sourceInstaller = Get-ChildItem -Path "publish/releases" -Filter "*Setup.exe" | Select-Object -First 1

if ($sourceInstaller) {
    Copy-Item $sourceInstaller.FullName "$OutputDir/$installerName" -Force
    Write-Host "`n✓ Windows installer created successfully!" -ForegroundColor Green
    Write-Host "  Location: $OutputDir/$installerName" -ForegroundColor Cyan
} else {
    Write-Error "Could not find generated installer in publish/releases"
    exit 1
}

Write-Host "`nInstaller features:" -ForegroundColor Yellow
Write-Host "  - Auto-updates from GitHub releases" -ForegroundColor White
Write-Host "  - Silent installation support" -ForegroundColor White
Write-Host "  - No admin rights required" -ForegroundColor White
Write-Host "  - Delta updates (minimal download size)" -ForegroundColor White
Write-Host "  - Modern, actively maintained (Velopack)" -ForegroundColor White
