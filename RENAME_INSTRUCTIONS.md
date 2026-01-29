# GhostBullet Rename Instructions

This document outlines the renaming process from OpenBullet2 to GhostBullet.

## Completed Tasks

1. ✅ Updated solution file references
2. ✅ Updated .csproj project references
3. ✅ Updated Dockerfile
4. ✅ Updated docker-compose.yml
5. ✅ Updated GitHub workflow files
6. ✅ Updated README.md
7. ✅ Updated .gitignore

## Remaining Tasks

### 1. Directory Renaming (Manual)

The following directories need to be renamed:
- `OpenBullet2.Core` → `GhostBullet.Core`
- `OpenBullet2.Console` → `GhostBullet.Console`
- `OpenBullet2.Web` → `GhostBullet.Web`
- `OpenBullet2.Native` → `GhostBullet.Native`
- `OpenBullet2.Web.Updater` → `GhostBullet.Web.Updater`
- `OpenBullet2.Native.Updater` → `GhostBullet.Native.Updater`
- `OpenBullet2.Web.Tests` → `GhostBullet.Web.Tests`
- `openbullet2-web-client` → `ghostbullet-web-client`
- `OpenBullet2.sln` → `GhostBullet.sln` (already updated in content)

### 2. Namespace Updates

All C# files need namespace updates:
- `namespace OpenBullet2.Core` → `namespace GhostBullet.Core`
- `namespace OpenBullet2.Web` → `namespace GhostBullet.Web`
- `namespace OpenBullet2.Native` → `namespace GhostBullet.Native`
- `namespace OpenBullet2.Console` → `namespace GhostBullet.Console`
- `namespace OpenBullet2.Web.Updater` → `namespace GhostBullet.Web.Updater`
- `namespace OpenBullet2.Native.Updater` → `namespace GhostBullet.Native.Updater`
- `namespace OpenBullet2.Web.Tests` → `namespace GhostBullet.Web.Tests`

### 3. Using Statement Updates

All `using` statements need updates:
- `using OpenBullet2.Core` → `using GhostBullet.Core`
- `using OpenBullet2.Web` → `using GhostBullet.Web`
- `using OpenBullet2.Native` → `using GhostBullet.Native`
- etc.

### 4. UI String Updates

All UI strings mentioning "OpenBullet 2" or "OpenBullet2" should be changed to "GhostBullet":
- In XAML files
- In HTML files
- In TypeScript/JavaScript files
- In C# code comments and strings

### 5. Build File Naming

- DLL names: `OpenBullet2.Web.dll` → `GhostBullet.Web.dll`
- Executable names: `OpenBullet2.Native.exe` → `GhostBullet.Native.exe`
- Assembly names in .csproj files

## Automated Script

A PowerShell script (`rename_script.ps1`) has been created to automate most of these changes. Run it after renaming directories:

```powershell
powershell.exe -ExecutionPolicy Bypass -File "rename_script.ps1"
```

## Notes

- Some files may need manual review, especially:
  - Configuration files
  - Documentation files
  - Build scripts
  - Package.json files in the web client
