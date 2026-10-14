@echo off
rem Iron Admiral 1.1 (DIP GG Extended) - installer
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install.ps1" %*
