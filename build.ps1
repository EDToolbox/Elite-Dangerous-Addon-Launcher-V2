#!/usr/bin/env pwsh
<#
.SYNOPSIS
Build und Installer-Generierung für Elite Dangerous Addon Launcher V2

.DESCRIPTION
Kompiliert das Projekt und erstellt einen NSIS Installer

.PARAMETER BuildType
Typ des Builds: Debug oder Release (Default: Release)

.PARAMETER NoPublish
Überspringt die Publish-Phase

.PARAMETER NoInstaller
Überspringt die NSIS Installer-Erstellung

.EXAMPLE
.\build.ps1 -BuildType Release
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$BuildType = "Release",
    
    [switch]$NoPublish,
    [switch]$NoInstaller,
    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Farben für Ausgabe
$InfoColor = "Cyan"
$SuccessColor = "Green"
$ErrorColor = "Red"
$WarningColor = "Yellow"

function Write-Info {
    Write-Host "[INFO] $args" -ForegroundColor $InfoColor
}

function Write-Success {
    Write-Host "[✓] $args" -ForegroundColor $SuccessColor
}

function Write-Warning {
    Write-Host "[⚠] $args" -ForegroundColor $WarningColor
}

function Write-Error {
    Write-Host "[✗] $args" -ForegroundColor $ErrorColor
}

function Show-Help {
    Write-Host @"
Elite Dangerous Addon Launcher V2 - Build Script

SYNTAX:
    .\build.ps1 [Options]

OPTIONS:
    -BuildType <string>     Typ des Builds: Debug oder Release (Default: Release)
    -NoPublish              Überspringt die Publish-Phase
    -NoInstaller            Überspringt die NSIS Installer-Erstellung
    -Help                   Zeigt diese Hilfe an

BEISPIELE:
    .\build.ps1
    .\build.ps1 -BuildType Debug
    .\build.ps1 -NoInstaller
"@
}

if ($Help) {
    Show-Help
    exit 0
}

Write-Info "Elite Dangerous Addon Launcher V2 - Build Process"
Write-Info "BuildType: $BuildType"
Write-Info "Skip Publish: $NoPublish"
Write-Info "Skip Installer: $NoInstaller"

# ==================== SCHRITT 1: Cleanup ====================

Write-Info "Cleanup alte Build-Artefakte..."
if (Test-Path "bin") {
    Remove-Item "bin" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "bin/ gelöscht"
}

if (Test-Path "obj") {
    Remove-Item "obj" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "obj/ gelöscht"
}

if (Test-Path "publish" -and -not $NoPublish) {
    Remove-Item "publish" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Success "publish/ gelöscht"
}

# ==================== SCHRITT 2: Restore ====================

Write-Info "NuGet Packages wiederherstellen..."
& dotnet restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet restore fehlgeschlagen!"
    exit 1
}
Write-Success "Packages wiederhergestellt"

# ==================== SCHRITT 3: Build ====================

Write-Info "Projekt kompilieren ($BuildType)..."
& dotnet build --configuration $BuildType
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet build fehlgeschlagen!"
    exit 1
}
Write-Success "Projekt erfolgreich kompiliert"

# ==================== SCHRITT 4: Publish ====================

if (-not $NoPublish) {
    Write-Info "Projekt veröffentlichen..."
    & dotnet publish --configuration $BuildType --output "./publish" --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish fehlgeschlagen!"
        exit 1
    }
    Write-Success "Projekt veröffentlicht zu: ./publish"
}

# ==================== SCHRITT 5: NSIS Installer ====================

if (-not $NoInstaller) {
    Write-Info "NSIS Installer wird erzeugt..."
    
    # Prüfe ob NSIS installiert ist
    $nsisPath = "C:\Program Files (x86)\NSIS\makensis.exe"
    if (-not (Test-Path $nsisPath)) {
        $nsisPath = "C:\Program Files\NSIS\makensis.exe"
    }
    
    if (-not (Test-Path $nsisPath)) {
        Write-Error "NSIS nicht gefunden! Installiere NSIS von: https://nsis.sourceforge.io/"
        Write-Warning "Prüfe auch manuell: C:\Program Files (x86)\NSIS oder C:\Program Files\NSIS"
        exit 1
    }
    
    if (-not (Test-Path "installer/installer.nsi")) {
        Write-Error "installer/installer.nsi nicht gefunden!"
        exit 1
    }
    
    Write-Info "Starte NSIS: $nsisPath"
    & $nsisPath "installer/installer.nsi"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "NSIS Installer-Erstellung fehlgeschlagen!"
        exit 1
    }
    
    if (Test-Path "Elite-Dangerous-Addon-Launcher-Setup.exe") {
        Write-Success "Installer erstellt: Elite-Dangerous-Addon-Launcher-Setup.exe"
    } else {
        Write-Error "Installer-Datei nicht gefunden nach NSIS-Lauf!"
        exit 1
    }
}

# ==================== Zusammenfassung ====================

Write-Host ""
Write-Success "Build erfolgreich abgeschlossen!"
Write-Host ""
Write-Host "Nächste Schritte:" -ForegroundColor $InfoColor
Write-Host "  1. Testen: .\bin\$BuildType\net10.0-windows\Elite Dangerous Addon Launcher V2.exe"

if (-not $NoPublish) {
    Write-Host "  2. Publish-Verzeichnis: .\publish\"
}

if (-not $NoInstaller) {
    Write-Host "  3. Installer-Verzeichnis: Elite-Dangerous-Addon-Launcher-Setup.exe"
}

Write-Host ""
Write-Host "Release auf GitHub:" -ForegroundColor $InfoColor
Write-Host "  git tag v2.0.0"
Write-Host "  git push origin v2.0.0"
Write-Host ""
