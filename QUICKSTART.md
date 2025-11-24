# 🚀 Quick Start - Elite Dangerous Addon Launcher V2

Schnelle Referenz für häufige Aufgaben.

## 📦 Releases

### Neues Release erstellen
```powershell
.\release.ps1 -Version "2.1.0" -Message "Release notes here"
```

Das erledigt automatisch alles - einfach abwarten!

---

## 🔨 Bauen

### Lokales Bauen (mit Installer)
```powershell
.\build.ps1
```

### Nur Code kompilieren
```powershell
.\build.ps1 -NoInstaller
```

### Debug-Build
```powershell
.\build.ps1 -BuildType Debug
```

---

## 📝 Dokumentation

| Datei | Zweck |
|-------|-------|
| `RELEASE_GUIDE.md` | Schritt-für-Schritt Release-Anleitung |
| `CI_CD.md` | CI/CD-Pipeline und Workflow-Details |
| `INSTALLER_README.md` | Installer-Konfiguration |
| `SECURITY_REMEDIATION.md` | Security-Fixes Dokumentation |
| `CHANGELOG.md` | Version-History |
| `README.md` | Projekt-Übersicht |

---

## 🔧 Konfiguration

### Installer anpassen
→ `installer/installer.nsi`

Wichtigste Einstellungen:
- Versionsnummer
- Installationspfad
- Sprachen
- Icons/Bilder

### Build-Optionen
→ `.github/workflows/build-release.yml`

---

## 🐛 Häufige Probleme

### "NSIS nicht gefunden"
→ Installiere NSIS: https://nsis.sourceforge.io/

### "GitHub Actions schlägt fehl"
→ Prüfe `LICENSE.txt`, `CHANGELOG.md`, Git-Tag-Format

### "Installer funktioniert nicht"
→ Lese `INSTALLER_README.md` & teste lokal

---

## ✅ Checkliste vor Release

- [ ] CHANGELOG.md aktualisiert
- [ ] Alle Tests bestanden
- [ ] Installer lokal getestet
- [ ] Keine Warnungen beim Build
- [ ] Version aktualisiert
- [ ] Git-Commits gepushed

---

## 📊 Struktur

```
Elite-Dangerous-Addon-Launcher-V2/
├── .github/workflows/
│   └── build-release.yml          ← GitHub Actions
├── installer/
│   ├── installer.nsi              ← NSIS Installer-Skript
│   └── README.md
├── build.ps1                      ← Lokales Build-Skript
├── release.ps1                    ← Release-Management
├── RELEASE_GUIDE.md               ← Diese Anleitung
├── CI_CD.md                       ← CI/CD-Details
├── INSTALLER_README.md            ← Installer-Guide
└── README.md                      ← Projekt-Info
```

---

## 🔐 Wichtige Dateien

- **LICENSE.txt** - Für Installer & GitHub Release
- **CHANGELOG.md** - Für GitHub Release Notes
- **.github/workflows/** - GitHub Actions Automatisierung
- **installer/installer.nsi** - NSIS Installer-Config

---

## 🌐 Nützliche Links

- [GitHub Releases](https://github.com/EDToolbox/Elite-Dangerous-Addon-Launcher-V2/releases)
- [GitHub Actions](https://github.com/EDToolbox/Elite-Dangerous-Addon-Launcher-V2/actions)
- [NSIS Dokumentation](https://nsis.sourceforge.io/Docs/)

---

## 💬 Kommandos Kurzreferenz

```powershell
# Build
.\build.ps1

# Build ohne Installer
.\build.ps1 -NoInstaller

# Release erstellen
.\release.ps1 -Version "2.1.0" -Message "Notes"

# Git Push
git push origin master
git push origin v2.1.0

# GitHub CLI
gh run list
gh run view <ID> --log
```

---

**Letzte Aktualisierung:** 2025-11-24
**Status:** ✅ Production Ready
