# Iron Admiral 1.1 (DIP GG Extended) - uninstaller
# Restores the Mods folder (and MelonLoader, if the installer replaced it)
# from the most recent backup made by the installer.
param([string]$GamePath = "", [switch]$NoPause)

$ErrorActionPreference = "Stop"
$GameExe = "Ultimate Admiral Dreadnoughts.exe"
function Say($pl, $en) { Write-Host "[PL] $pl"; Write-Host "[EN] $en" }
function Finish($code) { if (-not $NoPause) { Write-Host ""; Read-Host "Enter = koniec / exit" | Out-Null }; exit $code }

# The uninstaller lives in <game>\Iron_Admiral_backup\ after installation
. (Join-Path $PSScriptRoot "Common.ps1")
if (-not $GamePath) { $GamePath = Split-Path -Parent $PSScriptRoot }
if (-not (Test-GameDir $GamePath)) { $GamePath = Find-Game }
if (-not (Test-GameDir $GamePath)) {
    Say "Nie znaleziono gry obok tego skryptu. Uruchom Uninstall.bat z folderu Iron_Admiral_backup w folderze gry." "Game not found next to this script. Run Uninstall.bat from the Iron_Admiral_backup folder inside the game folder."
    Finish 1
}
if (Get-Process -Name "Ultimate Admiral Dreadnoughts" -ErrorAction SilentlyContinue) {
    Say "Gra jest uruchomiona. Zamknij ja i sprobuj ponownie." "The game is running. Close it and try again."
    Finish 1
}

$backRoot = Join-Path $GamePath "Iron_Admiral_backup"
$lastFile = Join-Path $backRoot "last_install.txt"
if (-not (Test-Path $lastFile)) { Say "Brak informacji o instalacji." "No installation record found."; Finish 1 }
$backup = Join-Path $backRoot ((Get-Content $lastFile -Raw).Trim())
$meta = Get-Content (Join-Path $backup "install_info.json") -Raw | ConvertFrom-Json

# Write access check -> relaunch elevated if needed
$probe = Join-Path $GamePath ".dipgg_write_test"
try { Set-Content -Path $probe -Value "x" -ErrorAction Stop; Remove-Item $probe -Force }
catch {
    Say "Potrzebne uprawnienia administratora." "Administrator rights needed."
    $a = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$($MyInvocation.MyCommand.Path)`"", "-GamePath", "`"$GamePath`"")
    Start-Process powershell.exe -Verb RunAs -ArgumentList $a -Wait
    exit 0
}

# Mods: remove the mod's folder, restore the previous one
$mods = Join-Path $GamePath "Mods"
if (Test-Path $mods) { Remove-Item -Path $mods -Recurse -Force }
if ($meta.hadMods -and (Test-Path (Join-Path $backup "Mods"))) {
    Move-Item -Path (Join-Path $backup "Mods") -Destination $mods -Force
    Say "Przywrocono poprzedni folder Mods." "Previous Mods folder restored."
} else {
    New-Item -ItemType Directory -Force -Path $mods | Out-Null
}

# MelonLoader: only touched if this installer installed it
if ($meta.mlInstalled) {
    foreach ($item in @("MelonLoader", "version.dll")) {
        $p = Join-Path $GamePath $item
        if (Test-Path $p) { Remove-Item -Path $p -Recurse -Force }
    }
    $mlBack = Join-Path $backup "MelonLoader_old"
    if ($meta.hadML -and (Test-Path $mlBack)) {
        Get-ChildItem -Path $mlBack | ForEach-Object { Move-Item -Path $_.FullName -Destination $GamePath -Force }
        Say "Przywrocono poprzedni MelonLoader." "Previous MelonLoader restored."
    } else {
        Say "Usunieto MelonLoader zainstalowany przez mod." "MelonLoader installed by the mod was removed."
    }
}

Remove-Item -Path $lastFile -Force
Say "GOTOWE. Gra przywrocona do stanu sprzed instalacji." "DONE. The game is back to its pre-install state."
Say "Folder kopii zapasowej mozna usunac recznie: $backRoot" "You may delete the backup folder manually: $backRoot"
Finish 0
