# Iron Admiral 1.1 (DIP GG Extended) - installer
# Installs MelonLoader v0.6.6 (if needed), TweaksAndFixes and the mod data
# into Ultimate Admiral: Dreadnoughts. Existing files are backed up first.
param(
    [string]$GamePath = "",
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$ModName    = "Iron Admiral 1.1 (DIP GG Extended)"
$AppId      = "1069660"
$GameExe    = "Ultimate Admiral Dreadnoughts.exe"
$MLVersion  = "0.6.6"
$MLUrl      = "https://github.com/LavaGang/MelonLoader/releases/download/v0.6.6/MelonLoader.x64.zip"
$MLSha256   = "687b82605606e941cefdc007b880b720922cc319bb70270064590d4038c3c0db"
$Root       = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$Payload    = Join-Path $Root "payload"

function Say($pl, $en) { Write-Host "[PL] $pl"; Write-Host "[EN] $en" }
function Finish($code) {
    if (-not $NoPause) { Write-Host ""; Read-Host "Enter = koniec / exit" | Out-Null }
    exit $code
}
. (Join-Path $PSScriptRoot "Common.ps1")

Write-Host "=== $ModName ==="
if (-not (Test-Path (Join-Path $Payload "Mods\TweaksAndFixes.dll"))) {
    Say "Brak folderu payload. Rozpakuj cale archiwum ZIP przed instalacja." "Payload folder missing. Extract the whole ZIP before installing."
    Finish 1
}

if (-not (Test-GameDir $GamePath)) { $GamePath = Find-Game }
if (-not (Test-GameDir $GamePath)) {
    Say "Nie znaleziono gry. Uruchom ponownie i wskaz folder z plikiem $GameExe." "Game not found. Run again and select the folder containing $GameExe."
    Finish 1
}
Say "Folder gry: $GamePath" "Game folder: $GamePath"

if (Get-Process -Name "Ultimate Admiral Dreadnoughts" -ErrorAction SilentlyContinue) {
    Say "Gra jest uruchomiona. Zamknij ja i uruchom instalator ponownie." "The game is running. Close it and run the installer again."
    Finish 1
}

# Write access check -> relaunch elevated if needed
$probe = Join-Path $GamePath ".dipgg_write_test"
try { Set-Content -Path $probe -Value "x" -ErrorAction Stop; Remove-Item $probe -Force }
catch {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if ($isAdmin) { Say "Brak praw zapisu do folderu gry." "No write access to the game folder."; Finish 1 }
    Say "Potrzebne uprawnienia administratora - za chwile pojawi sie pytanie Windows." "Administrator rights needed - Windows will ask now."
    $args2 = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$($MyInvocation.MyCommand.Path)`"", "-GamePath", "`"$GamePath`"")
    if ($NoPause) { $args2 += "-NoPause" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args2 -Wait
    exit 0
}

$stamp     = Get-Date -Format "yyyyMMdd_HHmmss"
$backRoot  = Join-Path $GamePath "Iron_Admiral_backup"
$backup    = Join-Path $backRoot $stamp
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$meta = [ordered]@{ mod = $ModName; date = $stamp; game = $GamePath; hadMods = $false; mlInstalled = $false; hadML = $false }

# --- MelonLoader
$mlDll = Join-Path $GamePath "MelonLoader\net6\MelonLoader.dll"
$mlOk = $false
if (Test-Path $mlDll) {
    $v = (Get-Item $mlDll).VersionInfo.ProductVersion
    if ($v -and $v.StartsWith($MLVersion)) { $mlOk = $true }
}
if ($mlOk) {
    Say "MelonLoader $MLVersion juz zainstalowany - bez zmian." "MelonLoader $MLVersion already installed - kept."
} else {
    if ((Test-Path (Join-Path $GamePath "MelonLoader")) -or (Test-Path (Join-Path $GamePath "version.dll"))) {
        $meta.hadML = $true
        $mlBack = Join-Path $backup "MelonLoader_old"
        New-Item -ItemType Directory -Force -Path $mlBack | Out-Null
        foreach ($item in @("MelonLoader", "version.dll")) {
            $src = Join-Path $GamePath $item
            if (Test-Path $src) { Move-Item -Path $src -Destination $mlBack -Force }
        }
        Say "Stary MelonLoader przeniesiony do kopii zapasowej." "Old MelonLoader moved to backup."
    }
    # MelonLoader archive: bundled (full edition) or downloaded from the official
    # GitHub release (Nexus edition). Either way the SHA-256 must match the
    # official v0.6.6 MelonLoader.x64.zip before anything is extracted.
    $mlZip = Join-Path $Payload "MelonLoader.x64.zip"
    if (-not (Test-Path $mlZip)) {
        $mlZip = Join-Path $env:TEMP ("MelonLoader.x64_" + $MLVersion + ".zip")
        Say "Pobieranie MelonLoader $MLVersion z oficjalnego wydania GitHub (21 MB)..." "Downloading MelonLoader $MLVersion from the official GitHub release (21 MB)..."
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $MLUrl -OutFile $mlZip -UseBasicParsing
        } catch {
            Say "Nie udalo sie pobrac MelonLoadera: $($_.Exception.Message)" "MelonLoader download failed: $($_.Exception.Message)"
            Say "Pobierz recznie $MLUrl i poloz plik w folderze payload, potem uruchom instalator ponownie." "Download $MLUrl manually, put the file in the payload folder and run the installer again."
            Finish 1
        }
    }
    $hash = (Get-FileHash -Path $mlZip -Algorithm SHA256).Hash.ToLower()
    if ($hash -ne $MLSha256) {
        Say "Suma SHA-256 MelonLoadera nie zgadza sie z oficjalnym wydaniem ($hash). Instalacja przerwana." "MelonLoader SHA-256 does not match the official release ($hash). Installation aborted."
        Finish 1
    }
    Say "Instalacja MelonLoader $MLVersion (SHA-256 OK)..." "Installing MelonLoader $MLVersion (SHA-256 OK)..."
    Expand-Archive -Path $mlZip -DestinationPath $GamePath -Force
    $meta.mlInstalled = $true
}

# --- Mods folder
$mods = Join-Path $GamePath "Mods"
if (Test-Path $mods) {
    Move-Item -Path $mods -Destination (Join-Path $backup "Mods") -Force
    $meta.hadMods = $true
    Say "Dotychczasowy folder Mods przeniesiony do kopii zapasowej." "Existing Mods folder moved to backup."
}
Copy-Item -Path (Join-Path $Payload "Mods") -Destination $mods -Recurse -Force
Get-ChildItem -Path $mods -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
Get-ChildItem -Path (Join-Path $GamePath "MelonLoader") -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
Unblock-File -Path (Join-Path $GamePath "version.dll") -ErrorAction SilentlyContinue

# --- Uninstaller + metadata
$meta | ConvertTo-Json | Set-Content -Path (Join-Path $backup "install_info.json") -Encoding UTF8
Set-Content -Path (Join-Path $backRoot "last_install.txt") -Value $stamp -Encoding ASCII
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall.ps1") -Destination $backRoot -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Common.ps1") -Destination $backRoot -Force
Copy-Item -Path (Join-Path $Root "Uninstall.bat") -Destination $backRoot -Force -ErrorAction SilentlyContinue

Write-Host ""
Say "GOTOWE. Kopia zapasowa: $backup" "DONE. Backup: $backup"
Say "Pierwsze uruchomienie gry potrwa kilka minut: MelonLoader generuje pliki i potrzebuje internetu. To normalne." "The first game launch takes a few minutes: MelonLoader generates files and needs internet. This is normal."
Say "Zalecana nowa kampania - zmiany prowincji dzialaja tylko w nowej grze." "A new campaign is recommended - province changes apply to new games only."
Say "Odinstalowanie: $backRoot\Uninstall.bat" "Uninstall: $backRoot\Uninstall.bat"
Finish 0
