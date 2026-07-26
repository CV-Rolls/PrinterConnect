# Deploying PrinterConnect with Microsoft Intune

## 1. Prepare
- Sign the exe first: `.\sign.ps1 -ExePath .\PrinterConnect.exe -Thumbprint <thumbprint>`
- Folder for packaging:
  ```
  PrinterConnect\
    PrinterConnect.exe
    PrinterConnect.exe.config
    install.ps1
    uninstall.ps1
  ```

`install.ps1`:
```powershell
$dest = "$env:ProgramFiles\PrinterConnect"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$PSScriptRoot\PrinterConnect.exe*" $dest -Force
# Start-menu shortcut for all users
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut("$env:ProgramData\Microsoft\Windows\Start Menu\Programs\PrinterConnect.lnk")
$lnk.TargetPath = "$dest\PrinterConnect.exe"
$lnk.Save()
```

`uninstall.ps1`:
```powershell
Remove-Item "$env:ProgramFiles\PrinterConnect" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\PrinterConnect.lnk" -ErrorAction SilentlyContinue
```

## 2. Wrap
Use the [Microsoft Win32 Content Prep Tool](https://learn.microsoft.com/en-us/mem/intune/apps/apps-win32-prepare):
```
IntuneWinAppUtil.exe -c .\PrinterConnect -s install.ps1 -o .\out
```

## 3. Create the Win32 app in Intune
- Install:   `powershell.exe -ExecutionPolicy Bypass -File install.ps1`
- Uninstall: `powershell.exe -ExecutionPolicy Bypass -File uninstall.ps1`
- Install behavior: **System**
- Detection rule: file exists `%ProgramFiles%\PrinterConnect\PrinterConnect.exe`
  (or version ≥ x.y for upgrades)
- Requirements: Windows 11 64-bit (works on Windows 10 as well)

## 4. Trust
Add a publisher-based allow rule for your signing certificate in
your security tools so every future signed release is trusted without
per-file exceptions.

## Optional: pre-seed defaults
Deploy `%APPDATA%\PrinterConnect\settings.json` per user (e.g. via a
proactive remediation) to preset the print server list and column layout.
