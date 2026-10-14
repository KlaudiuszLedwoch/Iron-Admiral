@echo off
rem Iron Admiral 1.1 (DIP GG Extended) - uninstaller
cd /d "%~dp0"
if exist "%~dp0Uninstall.ps1" (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
) else (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Uninstall.ps1" %*
)
