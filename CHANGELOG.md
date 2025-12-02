# Changelog

All notable changes to the EDData Addon Helper project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.0.0-beta3] - 2025-12-02

### 🌍 Internationalization

#### Localization Infrastructure (Weblate-Ready)
- Added RESX-based localization system for multi-language support
- **English (en)** - Default language with ~60 localization keys
- **German (de)** - Complete German translation
- New `LocalizeExtension` for easy XAML binding: `{loc:Localize Key=MainWindow_AddApp}`
- `Strings` helper class for code-behind access: `Strings.Get("key")`, `Strings.Format("key", args)`
- Culture switching support via `Strings.SetCulture("de")`
- Localization test suite in `Tests/LocalizationTests.cs`
- Ready for Weblate integration (monolingual RESX format)

### 🐛 Bug Fixes

- Fixed `steam://` URL scheme being rejected as "invalid or unsafe URL"
- Steam game launches now work correctly with proper protocol handler support
- Fixed Elite Dangerous edit dialog not allowing save when changing options (OK button stayed disabled)

---

## [2.0.0-beta2] - 2025-12-01

### 🎉 Highlights
- **Application Renamed**: "Elite Dangerous Addon Launcher V2" → **"EDData Addon Helper"**
- **Framework Upgrade**: .NET 8.0 → **.NET 10 LTS** (supported until November 2028)
- **Professional Installer**: WiX Toolset MSI installer with full customization
- **Elite Dangerous Auto-Detection**: Automatic detection for Steam, Epic Games, and Frontier Standalone

---

### 🚀 Added

#### Elite Dangerous Launch Dialog
- New `EliteLaunchDialog` for selecting the launch method:
  - **Standard Installation** - Launch Frontier Launcher directly
  - **Steam Version** - Launch via Steam (`steam://rungameid/359320`)
  - **Epic Games** - Launch via Epic Games Store
  - **Legendary** - Launch via Open-Source Legendary Launcher
  - **Browse Manually** - Select custom path
- Three additional launch options:
  - **Auto Run** (`/autorun`) - Start game directly without launcher dialog
  - **Auto Quit** (`/autoquit`) - Close launcher after game starts
  - **VR Mode** (`/vr`) - Start Elite Dangerous in VR mode
- Dialog appears when adding AND editing Elite Dangerous entries

#### Auto-Detection for Elite Dangerous
- **Steam Library Detection**:
  - Automatic detection via Windows Registry (`HKCU:\Software\Valve\Steam`)
  - Parsing of `libraryfolders.vdf` for all Steam libraries
  - Search for `EDLaunch.exe` in all Steam folders
- **Epic Games Store Detection**:
  - Scanning all `.item` manifest files in Epic folder
  - Detection via `AppName: "Eel"` (Epic ID for Elite Dangerous)
  - Extraction of installation path from manifests
- **Frontier Standalone Detection**:
  - Check known installation paths:
    - `C:\Program Files (x86)\Frontier\EDLaunch\EDLaunch.exe`
    - `C:\Program Files\Frontier\Products\elite-dangerous-64\EDLaunch.exe`
  - Registry lookup: `HKLM:\SOFTWARE\WOW6432Node\Frontier Developments\Elite Dangerous`
- Fallback to full computer scan if auto-detection fails

#### WiX Toolset MSI Installer
- Professional Windows Installer (`.msi`) with WiX Toolset v6.0.2
- Features:
  - License display during installation
  - Custom installation directory selection
  - Start menu and desktop shortcuts
  - Automatic uninstaller registration
  - Upgrade detection for clean updates
- Replaces NSIS/MSIX for better Windows integration

#### CI/CD Pipeline
- GitHub Actions workflow for automated builds
- Automatic release creation on tag push (`v*`)
- MSI installer automatically uploaded as release asset
- Uses `softprops/action-gh-release@v2`

---

### 🔧 Changed

#### Framework & Dependencies
- **Framework Upgrade**: .NET 8.0 → .NET 10 LTS (`net10.0-windows10.0.22621.0`)
- **Dependencies updated**:
  - MaterialDesignColors: 3.1.0 → 5.3.0
  - MaterialDesignThemes: 5.3.0
  - Newtonsoft.Json: 13.0.4
  - Serilog: 4.3.0
  - Serilog.Sinks.Console: 6.1.1
  - Serilog.Sinks.File: 7.0.0
  - gong-wpf-dragdrop: 4.0.0

#### UI/UX Improvements
- **DataGrid Columns**: Button columns set to `Auto` width for flexible sizing
- **Application Title**: New application name "EDData Addon Helper" updated everywhere
- **Window Titles**: All window titles updated to new name

#### Project Rename
- Project file: `Elite Dangerous Addon Launcher V2.csproj` → `EDData Addon Helper.csproj`
- Assembly Name: `EDData Addon Helper`
- Root Namespace: `EDData_Addon_Helper`
- All XAML files updated with new namespace

---

### 🔒 Security

- **8 critical security fixes implemented**:
  1. Process Execution Hardening (`UseShellExecute=false`)
  2. JSON Deserialization Security (`TypeNameHandling.None`)
  3. Path Traversal Protection with validation methods
  4. URL Protocol Whitelist Validation
  5. File Size Validation (1MB manifest limit)
  6. Comprehensive Exception Handling
  7. Profile Import Validation
  8. Settings Import Validation

---

### 🐛 Fixed

#### Compiler Warnings
- **130 compiler warnings eliminated** in Debug configuration
- Field Initialization Warnings (string fields → `string.Empty`)
- Nullable Event Handler Warnings (`PropertyChangedEventHandler?`)
- Null Reference Analysis Warnings in UI components

#### Bug Fixes
- Added exception logging in profile loading
- Fixed double loop bug in process cleanup
- Fixed NullReferenceException in `Btn_Launch_Click`
- Fixed thread-unsafe `processList` access
- Fixed memory leak in `Profile.Apps` CollectionChanged event
- Removed deprecated `.csproj` file with typo

---

### ⚡ Performance

- **Build Time**: Reduced from 4.42s to 1.86s (-57%)
- **Thread.Sleep() replaced with async Task.Delay()** - No more UI blocking
- **Epic Games Manifest Caching** - ~70-80% fewer I/O operations
- **Directory.GetFiles() with filter** - Reduced memory footprint
- **Process Handle Management** - Using statements for proper cleanup

---

### 📦 Technical Details

#### New Files
- `EliteLaunchDialog.xaml` / `EliteLaunchDialog.xaml.cs` - Launch method dialog
- `Constants.cs` - 35+ centralized application constants
- `Package.wxs` - WiX installer definition
- `.github/workflows/build-release.yml` - CI/CD pipeline

#### Build Output
- **Installer**: `EDData-Addon-Helper-2.0.0-beta2.msi`
- **Size**: ~55 MiB (self-contained with .NET Runtime)
- **Target**: Windows 10/11 x64

---

### 📋 Dependencies

| Package | Version |
|---------|---------|
| .NET | 10.0 LTS |
| MaterialDesignColors | 5.3.0 |
| MaterialDesignThemes | 5.3.0 |
| Newtonsoft.Json | 13.0.4 |
| Serilog | 4.3.0 |
| Serilog.Sinks.Console | 6.1.1 |
| Serilog.Sinks.File | 7.0.0 |
| gong-wpf-dragdrop | 4.0.0 |
| WiX Toolset | 6.0.2 |

---

For more information, see the [Contributing Guidelines](CONTRIBUTING.md).
