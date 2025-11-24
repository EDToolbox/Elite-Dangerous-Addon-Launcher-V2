# Release-Anleitung - Elite Dangerous Addon Launcher V2

Schritt-für-Schritt Anleitung zur Erstellung von Releases mit automatischem Installer.

## 📋 Inhaltsverzeichnis

1. [Lokales Bauen (Testing)](#-lokales-bauen)
2. [Automatisiertes Release](#-automatisiertes-release)
3. [Manuelles Release](#-manuelles-release)
4. [Installer-Konfiguration](#-installer-konfiguration)
5. [Troubleshooting](#-troubleshooting)

---

## 🔨 Lokales Bauen

### Voraussetzungen
```
✓ .NET 8.0 SDK
✓ NSIS 3.08+ (für Installer)
✓ Git
```

### Minimales Bauen
```powershell
.\build.ps1 -NoInstaller
```

Erzeugt:
- `bin/Release/` - Debug/Release Binaries
- `publish/` - Für Distribution

### Vollständiges Bauen mit Installer
```powershell
.\build.ps1
```

Erzeugt zusätzlich:
- `Elite-Dangerous-Addon-Launcher-Setup.exe` - Ausführbarer Installer

### Debug-Builds
```powershell
.\build.ps1 -BuildType Debug
```

### Output-Verzeichnisse
```
bin/
├── Release/
│   └── net8.0-windows/
│       ├── *.exe
│       └── *.dll
└── Debug/
    └── net8.0-windows/

publish/
├── *.exe
├── *.dll
├── *.config
└── ...

Elite-Dangerous-Addon-Launcher-Setup.exe  # NSIS Installer
```

---

## 🚀 Automatisiertes Release

### Empfohlener Weg (einfach & zuverlässig)

```powershell
# 1. Versionsnummer wählen (z.B. 2.1.0)
.\release.ps1 -Version "2.1.0" -Message "Major feature update

- Added X feature
- Fixed Y bug
- Improved Z performance"
```

Das Skript:
1. ✓ Aktualisiert `installer.nsi` Version
2. ✓ Erstellt Git Commit mit Versionsbump
3. ✓ Erstellt Git Tag (z.B. `v2.1.0`)
4. ✓ Pushed zu Remote
5. ✓ **Triggert GitHub Actions automatisch**

### Was passiert dann?

GitHub Actions startet automatisch:
```
1. Checkout Code
2. Build Release (dotnet build)
3. Publish (dotnet publish)
4. Erstelle Installer (NSIS)
5. Erstelle GitHub Release
6. Lade Dateien hoch
```

### GitHub Release enthält

Nach ~5-10 Minuten:
- `Elite-Dangerous-Addon-Launcher-Setup.exe` (Installer)
- `LICENSE.txt`
- Release Notes aus CHANGELOG

Verfügbar unter: https://github.com/EDToolbox/Elite-Dangerous-Addon-Launcher-V2/releases

---

## 🔧 Manuelles Release

### Wenn release.ps1 nicht funktioniert

```bash
# 1. Änderungen committen
git add -A
git commit -m "feat: Add new feature"

# 2. Versionsnummer aktualisieren
# → installer.nsi editieren, Version ändern

# 3. Commit für Version
git add installer/installer.nsi
git commit -m "chore(release): bump version to 2.1.0"

# 4. Tag erstellen
git tag -a v2.1.0 -m "Release 2.1.0"

# 5. Push (triggert Actions)
git push origin master
git push origin v2.1.0
```

---

## ⚙️ Installer-Konfiguration

### Anpassung vor dem Release

#### 1. Version in `installer.nsi`

```nsi
; Finde diese Zeile und aktualisiere Version
WriteRegStr HKCU "Software\Elite Dangerous Addon Launcher V2" "Version" "2.1.0"
```

#### 2. Installationspfad (Standard)

```nsi
; Benutzer können diesen Pfad anpassen
InstallDir "$PROGRAMFILES\Elite Dangerous Addon Launcher V2"
```

#### 3. Sprachen unterstützen

```nsi
; Füge weitere Sprachen hinzu
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "French"
```

#### 4. Lizenz anzeigen

```nsi
; Automatisch in Installer-Workflow
!insertmacro MUI_PAGE_LICENSE "LICENSE.txt"
```

### Custom Icons (optional)

1. Erstelle/Beschaffe Icon-Dateien:
   - `icon.ico` (256×256px, App-Icon)
   - `header.bmp` (150×57px, Header)
   - `wizard.bmp` (164×314px, Willkommens-Bild)

2. Lege sie in `installer/` ab

3. NSIS nutzt sie automatisch

---

## 📊 GitHub Actions Status

### Überprüfen ob Build läuft

```bash
# Terminal
gh run list --repo EDToolbox/Elite-Dangerous-Addon-Launcher-V2

# Oder manuell
# → Gehe zu: https://github.com/EDToolbox/Elite-Dangerous-Addon-Launcher-V2/actions
```

### Workflow Logs anschauen
```bash
gh run view <RUN_ID> --log
```

---

## 🧪 Testing des Installers

### Lokal testen

1. **Installer bauen**
```powershell
.\build.ps1
```

2. **Installer ausführen**
```powershell
.\Elite-Dangerous-Addon-Launcher-Setup.exe
```

3. **Installation testen**
   - [ ] Willkommens-Seite angezeigt
   - [ ] Lizenz angezeigt
   - [ ] Installationspfad wählbar
   - [ ] Installation erfolgreich
   - [ ] Shortcuts erstellt
   - [ ] App startet

4. **Deinstallation testen**
   - [ ] Deinstallations-Dialog angezeigt
   - [ ] Deinstallation erfolgreich
   - [ ] Dateien gelöscht
   - [ ] Registry bereinigt
   - [ ] Shortcuts entfernt

---

## 🐛 Troubleshooting

### NSIS nicht gefunden

**Fehler:**
```
Error: NSIS nicht gefunden!
```

**Lösung:**
```powershell
# Installiere NSIS
https://nsis.sourceforge.io/Download

# Oder setze Pfad manuell in build.ps1
$nsisPath = "C:\Program Files\NSIS\makensis.exe"
```

### GitHub Actions schlägt fehl

**Mögliche Ursachen:**

1. **LICENSE.txt fehlt**
   ```bash
   # Prüfen
   ls LICENSE.txt
   
   # Falls nicht vorhanden, erstellen
   echo "MIT License..." > LICENSE.txt
   ```

2. **Git Tag ungültiges Format**
   ```bash
   # Ungültig: v2.0 oder release-2.0
   # Gültig: v2.0.0
   git tag v2.0.0  # Richtig
   ```

3. **CHANGELOG.md fehlt**
   ```bash
   # Datei muss existieren
   ls CHANGELOG.md
   ```

### Installer erstellt keine Shortcuts

**Prüfe in installer.nsi:**
```nsi
; Diese Zeilen müssen vorhanden sein
CreateDirectory "$SMPROGRAMS\Elite Dangerous Addon Launcher V2"
CreateShortCut "$SMPROGRAMS\Elite Dangerous Addon Launcher V2\Elite Dangerous Addon Launcher.lnk" ...
```

### Installer-Größe unerwartet groß

**Überprüfe publish-Verzeichnis:**
```powershell
# Größe anschauen
du -h publish/

# Debug/Symbole entfernen
dotnet publish --configuration Release -p:DebugType=none
```

---

## 📝 Checkliste vor Release

Vor dem Release überprüfen:

- [ ] Alle Tests bestanden
- [ ] CHANGELOG.md aktualisiert
- [ ] Version in Code aktualisiert
- [ ] Keine Warnungen im Build
- [ ] Installer lokal getestet
- [ ] LICENSE.txt vorhanden
- [ ] Git-Änderungen committed
- [ ] Kein unstaged Changes

---

## 📚 Weiterführende Ressourcen

- [NSIS Dokumentation](https://nsis.sourceforge.io/Docs/)
- [GitHub Actions Docs](https://docs.github.com/actions)
- [Semantic Versioning](https://semver.org/)
- [Conventional Commits](https://www.conventionalcommits.org/)

---

## 💡 Best Practices

### Versionierung
- Nutze Semantic Versioning: `MAJOR.MINOR.PATCH`
- Beispiele: `2.0.0` (Major), `2.1.0` (Minor), `2.1.1` (Patch)

### Commit Messages
```
feat: Add new feature
fix: Fix specific bug
docs: Update documentation
chore(release): bump version to 2.1.0
```

### Release Notes
- Zusammenfassen der wichtigsten Änderungen
- Sicherheits-Updates hervorheben
- Breaking Changes kennzeichnen

### Testing
- Installer vor Release lokal testen
- Auf verschiedenen Windows-Versionen testen (wenn möglich)
- Deinstallation/Neuinstallation überprüfen

