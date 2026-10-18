# Iron Admiral - one-file setup: .NET 6 Desktop Runtime, VC++ Redistributable, MelonLoader 0.6.6, latest Iron Admiral Mods.
# Usage: save as Install-IronAdmiral.ps1, right-click > Run with PowerShell
#    or: powershell -ExecutionPolicy Bypass -File .\Install-IronAdmiral.ps1 [-GamePath "D:\Games\Ultimate Admiral Dreadnoughts"]
param([string]$GamePath = "")
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Exe   = "Ultimate Admiral Dreadnoughts.exe"
$MLUrl = "https://github.com/LavaGang/MelonLoader/releases/download/v0.6.6/MelonLoader.x64.zip"
$MLSha = "687b82605606e941cefdc007b880b720922cc319bb70270064590d4038c3c0db"
$VerUrl = "https://raw.githubusercontent.com/KlaudiuszLedwoch/Iron-Admiral/main/VERSION"

# 1. Run as administrator (the Steam folder in Program Files needs it)
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -GamePath `"$GamePath`""
    exit
}

# 2. Find the game
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath $Exe))) {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    $libs = @("$env:ProgramFiles (x86)\Steam")
    if ($steam) {
        $libs += ($steam -replace "/", "\")
        $vdf = Join-Path $libs[-1] "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) { foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace "\\\\", "\") } }
    }
    $GamePath = $libs | ForEach-Object { Join-Path $_ "steamapps\common\Ultimate Admiral Dreadnoughts" } | Where-Object { Test-Path (Join-Path $_ $Exe) } | Select-Object -First 1
}
if (-not $GamePath) { $GamePath = Read-Host "Game folder not found. Paste the folder that contains $Exe" }
if (-not (Test-Path (Join-Path $GamePath $Exe))) { Write-Host "No $Exe in $GamePath - stopping."; Read-Host "Enter to exit"; exit 1 }
if (Get-Process "Ultimate Admiral Dreadnoughts" -ErrorAction SilentlyContinue) { Write-Host "Close the game first."; Read-Host "Enter to exit"; exit 1 }
Write-Host "Game folder: $GamePath"
$tmp = Join-Path $env:TEMP "IronAdmiralSetup"; New-Item -ItemType Directory -Force $tmp | Out-Null

# 3. .NET 6 Desktop Runtime and VC++ Redistributable (via winget when available)
$haveNet6 = @(Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory -Filter "6.*" -ErrorAction SilentlyContinue).Count -gt 0
$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $haveNet6) {
    if ($winget) { Write-Host "Installing .NET 6 Desktop Runtime..."; winget install --id Microsoft.DotNet.DesktopRuntime.6 --architecture x64 -e --accept-source-agreements }
    else { Write-Host "Install .NET Desktop Runtime 6.0 (x64) from https://dotnet.microsoft.com/download/dotnet/6.0 and run this script again."; Read-Host "Enter to exit"; exit 1 }
} else { Write-Host ".NET 6 Desktop Runtime: present" }
if ($winget) { Write-Host "Checking Visual C++ Redistributable..."; winget install --id Microsoft.VCRedist.2015+.x64 -e --accept-source-agreements | Out-Null }
else { Write-Host "If the game fails to start, install the VC++ Redistributable x64: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist" }

# 4. MelonLoader 0.6.6 (official release, SHA-256 checked)
$mlDll = Join-Path $GamePath "MelonLoader\net6\MelonLoader.dll"
if ((Test-Path $mlDll) -and ((Get-Item $mlDll).VersionInfo.ProductVersion -like "0.6.6*")) { Write-Host "MelonLoader 0.6.6: present" }
else {
    $z = Join-Path $tmp "MelonLoader.x64.zip"
    Write-Host "Downloading MelonLoader 0.6.6..."; Invoke-WebRequest $MLUrl -OutFile $z -UseBasicParsing
    if ((Get-FileHash $z -Algorithm SHA256).Hash.ToLower() -ne $MLSha) { Write-Host "MelonLoader checksum mismatch - stopping."; Read-Host "Enter to exit"; exit 1 }
    Expand-Archive $z -DestinationPath $GamePath -Force
    Write-Host "MelonLoader 0.6.6 installed."
}

# 5. Latest Iron Admiral Mods folder from GitHub (checksum from the VERSION file); old Mods folder kept as a backup
$ver = (Invoke-WebRequest $VerUrl -UseBasicParsing).Content -split "`n"
$url = (($ver | Where-Object { $_ -like "url_mods_only *" }) -split " ")[1].Trim()
$sha = (($ver | Where-Object { $_ -like "sha256_mods_only *" }) -split " ")[1].Trim()
$z = Join-Path $tmp "Iron_Admiral_Mods_only.zip"
Write-Host "Downloading Iron Admiral $($ver[0].Trim())..."; Invoke-WebRequest $url -OutFile $z -UseBasicParsing
if ((Get-FileHash $z -Algorithm SHA256).Hash.ToLower() -ne $sha) { Write-Host "Iron Admiral checksum mismatch - stopping."; Read-Host "Enter to exit"; exit 1 }
$mods = Join-Path $GamePath "Mods"
if (Test-Path $mods) { $bak = "$mods`_backup_$(Get-Date -Format yyyyMMdd_HHmmss)"; Move-Item $mods $bak; Write-Host "Old Mods folder moved to $bak" }
$x = Join-Path $tmp "mods_only"; if (Test-Path $x) { Remove-Item $x -Recurse -Force }
Expand-Archive $z -DestinationPath $x -Force
Copy-Item (Join-Path $x "Mods") $GamePath -Recurse -Force
foreach ($d in "Mods", "MelonLoader") { Get-ChildItem (Join-Path $GamePath $d) -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue }
foreach ($f in "version.dll", "dobby.dll") { Unblock-File (Join-Path $GamePath $f) -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "Done. Start the game from Steam - the first launch takes a few minutes (MelonLoader console window is normal). Start a new campaign."
Read-Host "Enter to exit"
