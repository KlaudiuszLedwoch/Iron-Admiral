# Iron Admiral 1.2.75.3.2.1 (DIP GG Extended) - shared helpers (game folder detection)
$AppId   = "1069660"
$GameExe = "Ultimate Admiral Dreadnoughts.exe"
function Test-GameDir($p) { return ($p -and (Test-Path (Join-Path $p $GameExe))) }

function Find-Game {
    # 1) Steam uninstall entry
    $keys = @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App $AppId",
              "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App $AppId")
    foreach ($k in $keys) {
        try { $loc = (Get-ItemProperty -Path $k -ErrorAction Stop).InstallLocation; if (Test-GameDir $loc) { return $loc } } catch {}
    }
    # 2) Steam libraries
    $steam = $null
    foreach ($k in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try { $p = Get-ItemProperty -Path $k -ErrorAction Stop; if ($p.SteamPath) { $steam = $p.SteamPath } elseif ($p.InstallPath) { $steam = $p.InstallPath } } catch {}
        if ($steam) { break }
    }
    $libs = @()
    if ($steam) {
        $steam = $steam -replace "/", "\"
        $libs += $steam
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace "\\\\", "\") }
        }
    }
    foreach ($l in $libs) {
        $cand = Join-Path $l "steamapps\common\Ultimate Admiral Dreadnoughts"
        if (Test-GameDir $cand) { return $cand }
    }
    # 3) Ask the user
    Add-Type -AssemblyName System.Windows.Forms
    Write-Host "[PL] Nie znaleziono gry przez Steam - wskaz folder gry w oknie wyboru (moze byc za innymi oknami: pasek zadan / Alt+Tab)."
    Write-Host "[EN] Game not found through Steam - pick the game folder in the folder window (it may be behind other windows: taskbar / Alt+Tab)."
    $owner = New-Object System.Windows.Forms.Form; $owner.TopMost = $true; $owner.ShowInTaskbar = $false; $owner.Opacity = 0; $owner.Show(); $owner.Activate()
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Wskaz folder gry / Select the game folder (Ultimate Admiral Dreadnoughts)"
    if ($dlg.ShowDialog($owner) -eq "OK" -and (Test-GameDir $dlg.SelectedPath)) { return $dlg.SelectedPath }
    return $null
}

