# Iron Admiral — DIP The Great Game Extended

_Description as of 20 October 2026. Latest version: Iron Admiral 1.2.39 (DIP_GG_Fixes 1.11.67)._

**AI overhaul for Ultimate Admiral: Dreadnoughts, built on the Dreadnought Improvement Project (DIP) "The Great Game" 1.3.5, TweaksAndFixes 3.21.1 and MelonLoader 0.6.6.**

Download / Nexus page: https://www.nexusmods.com/ultimateadmiraldreadnoughts/mods/66

## Videos

The battle AI in action - every caption comes from the mod's own battle log:

- [Three battles, 6 min, ending with two surrendered destroyers taken as prizes](https://youtu.be/DjmOu0KVnwE)
- [Two battles, 4 min](https://youtu.be/ZAPta33uJXE)
- [The first battle uncut, 5 min](https://youtu.be/SZzuVT4X2qY)

## What it adds

- **Battle AI that fights like a navy** (DIP_GG_Fixes add-on): torpedo charges by destroyers, torpedo boats and light cruisers; divisions that cannot outrun a pursuit turn and fight; crippled ships are detached so the healthy ones keep speed; focus fire on damaged targets; national doctrines read from `Mods/battleDoctrines.csv`.
- **Campaign AI that does not give up**: cruisers instead of empty yards when short of money, balance-of-power funding when the player's fleet pulls ahead, construction watch, automatic shared designs, an economic-collapse grace period before a nation is dissolved.
- **Ships built in classes and money with a price**: series production (expensive prototypes, cheaper and faster follow-on ships, ordering together, idle and overloaded yards, cheaper sister ships) and an economy with inflation, credit ratings, interest and growth; treasury panel on the campaign map (F8), inflation in the Finances window, monthly treasury report. Campaign guide: `GUIDE_EN.md`.
- **Fog of war**: enemy fleets on the strategic map hidden or shown inexactly by reconnaissance, contact reports that can misidentify ships, enemy ships in battle unidentified until your ships close in (night, weather, smoke) - and the AI fights in the same fog.
- **A living world**: sovereign minor nations with their territories restored, diplomacy with consequences (worsening x4), a post-war crisis that hurts without being terminal.
- **Ships and tech**: armour penetration rebuilt from De Marre physics and anchored on Okun/NavWeaps tables for 16–21", treaty tonnage limits (Washington 35,000 t, London 45,000 t) then a physical Super Battleship ceiling, radar that matters at night, Long Lance range, research and national specialisations retuned.
- Two game-code bugs that aborted turn processing in DIP are caught.

Problem? F10 > Tools > *Create problem report* writes logs, settings and a Mods folder check into one zip on your desktop - attach it to your report (nothing is sent by the mod).

Full, reasoned change history: `CHANGELOG_EN.txt` / `CHANGELOG_PL.txt` (89 sections). Readme: `README_EN.txt` / `README_PL.txt`.

Latest in the current 1.1 package: DIP_GG_Fixes 1.11.0 (torpedo charges start by torpedo range and speed advantage; AI capital ships charge or close in when outgunned; player scout divisions charge; torpedoes held while a friendly ship is in the lane; smart target selection for all guns; non-linear veterancy up to +100%/-50%; surrender threshold by crew training; panic of green crews; surrendered ships captured by the winner - experimental), DIP_GG_Fixes 1.10.2 (shared designs are no longer re-exported every turn), a save migration tool for 1.0 campaigns (`source/save_tools`), work on the AI ship generator (lower minimum battleship belt for 1890-1900: 6"/7.5" instead of 9"; "Experimental Small Ironclad" relaxed to a 10" calibre limit and 1 turret minimum; full generator diagnostics in the log via taf_debug_shipgen_info=2 while the remaining failed designs are tracked down), and new campaigns start correctly again (predefined designs disabled, as the package ships without `predefinedDesigns.bin`).

## Install

Download `releases/Iron_Admiral_1.2.39_Nexus.zip`, extract anywhere, run `Install.bat` (plain PowerShell, no exe). It backs up your `Mods` folder, installs MelonLoader 0.6.6 if missing (downloaded from the official LavaGang release, SHA-256 verified) and copies the mod. `Uninstall.bat` restores the previous state. Start a new campaign.

Already running MelonLoader 0.6.6? Take `releases/Iron_Admiral_1.2.39_Mods_only.zip` and extract it into the game folder, so that you get `<game>/Mods/TweaksAndFixes.dll`, `DIP_GG_Fixes.dll` and the CSV files.

**".NET 6.0 runtime" error at game start:** MelonLoader 0.6.6 runs on .NET 6 - install the [.NET Desktop Runtime 6.0 (x64)](https://dotnet.microsoft.com/download/dotnet/6.0). Back to the unmodded game: `Uninstall.bat` from `<game>/Iron_Admiral_backup`, or delete `version.dll`, `MelonLoader/` and `Mods/` and verify the game files in Steam.

### One-file setup script

`Install-IronAdmiral.ps1` installs the .NET 6 runtime, the VC++ Redistributable, MelonLoader 0.6.6 and the latest Iron Admiral in one go (old `Mods` kept as a backup): right-click > Run with PowerShell, or `powershell -ExecutionPolicy Bypass -File .\Install-IronAdmiral.ps1`.

### Installing MelonLoader by hand

1. Close the game.
2. Install the [.NET Desktop Runtime 6.0 (Windows x64)](https://dotnet.microsoft.com/download/dotnet/6.0) and the [Microsoft Visual C++ Redistributable (x64)](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).
3. Download `MelonLoader.x64.zip` from the [official MelonLoader 0.6.6 release](https://github.com/LavaGang/MelonLoader/releases/tag/v0.6.6) (Assets) - only 0.6.6 is tested, TweaksAndFixes 3.21.1 needs it.
4. Unzip it straight into the game folder (Steam: right-click the game > Manage > Browse local files) so that `MelonLoader/`, `version.dll` and `dobby.dll` sit next to `Ultimate Admiral Dreadnoughts.exe`.
5. Start the game once (a few minutes, internet needed), close it, then unzip the Mods_only package into the game folder so that `Mods/TweaksAndFixes.dll` exists.

## Repository layout

- `payload/Mods/` — the mod data (CSV, TAF, DIP_GG_Fixes.dll, battleDoctrines.csv, techBonuses.csv)
- `source/DIP_GG_Fixes/` — C# source of the add-on (`Mod.cs`, build notes in `BUILD.md`)
- `source/merge_dip_1.3.5/` — the three-way merge script and report used to rebase on DIP 1.3.5
- `installer/` — Install.ps1 / Uninstall.ps1
- `releases/` — the two zips published on Nexus

## Credits and licences

- Dreadnought Improvement Project "The Great Game" — Brother Munro and the DIP team (Apache 2.0)
- TweaksAndFixes — NathanKell, DukeDagor (MIT)
- MelonLoader — LavaGang (Apache 2.0)
- Iron Admiral data changes, DIP_GG_Fixes add-on and installer — Klaudiusz Ledwoch (Steam: Klod76), Apache 2.0

Unofficial fan modification; do not report issues with the modded game to Game-Labs.
