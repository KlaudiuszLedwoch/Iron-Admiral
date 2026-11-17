# Iron Admiral

**A total overhaul of battle AI, campaign mechanics and naval aviation for Ultimate Admiral: Dreadnoughts.**

Enemy flotillas charge your battle line with torpedoes. Carriers send out strikes and scouts. Fleets on the map hide in the fog of war. Every ship is built in classes and paid for in an economy with inflation and credit. And every feature has its own switch in the F10 panel.

[Download (Releases)](https://github.com/KlaudiuszLedwoch/Iron-Admiral/releases) | [Nexus Mods](https://www.nexusmods.com/ultimateadmiraldreadnoughts/mods/66) | [Wiki](https://github.com/KlaudiuszLedwoch/Iron-Admiral/wiki) | [Discord](https://discord.gg/EgKHjQ5DB)

## Iron Admiral Battle AI
- **Torpedo attacks** - torpedo boats, destroyers and light cruisers charge capital ships to get into torpedo range, pull back when empty or hit, and return if they carry reloads.
- **Massed attacks** - when you are stronger, the whole enemy torpedo force goes in together.
- **An enemy that picks its fights** - ships that cannot hurt your armour stay out of your range or close in; crippled ships leave their division; guns finish off damaged targets first.
- **National doctrines** - Britain, Japan, Austria-Hungary, France and others fight differently (`Mods/battleDoctrines.csv`, with sources).
- **Torpedo aiming error**, **crew panic**, **surrender and prizes**.

## Carrier aviation
- Carriers (CV) as their own ship type, with a ready historical design for each nation and period.
- Campaign: battle support within aircraft reach (225 km in 1929 to 300 km in 1942), air reconnaissance, CV tab, naval aviation budget.
- Battle: launches in waves, torpedo planes from both bows, dive bombers, escorting fighters; losses replaced over time.

## Fog of war (F7)
Strategic map dark beyond your reconnaissance, estimated enemy fleets that age; unidentified grey silhouettes in battle. The AI plays by the same rules.

## Task force orders
Patrol, Raid, Shadow, Intercept, Bombard, Blockade; control zones by speed and range; convoy escorts.

## Campaign
Series production, treasury with inflation and credit (F8), ship trade (F9), Washington and London treaties, war weariness and peace treaties, sovereign minor nations, a campaign AI that keeps building.

## Ships and gunnery
Penetration from De Marre and Okun's tables, historical shells by generation, recorded salvo dispersion, radar at night, historical classes and shared player designs with a ranking.

## F10 panel
Press F10 anywhere: every feature with an ON/OFF switch, update channel (stable / beta), and *Create problem report* for bug reports.

## Install
Download the installer package from Releases, extract, run `Install.bat`. It backs up your `Mods` folder, installs MelonLoader 0.6.6 if missing and copies everything the mod needs. Then start a new campaign. Details: [Installation](https://github.com/KlaudiuszLedwoch/Iron-Admiral/wiki/Installation).

## Videos

The battle AI in action - every caption comes from the mod's own battle log:

- [Three battles, 6 min, ending with two surrendered destroyers taken as prizes](https://youtu.be/DjmOu0KVnwE)
- [Two battles, 4 min](https://youtu.be/ZAPta33uJXE)
- [The first battle uncut, 5 min](https://youtu.be/SZzuVT4X2qY)

## Stable and beta

Iron Admiral is released in two lines.

- **Stable** - the main download (GitHub "Latest" release, Nexus main files). Only features that have been played and tested go here. Bug fixes are released here straight away.
- **Beta** - new features that still need testing (GitHub pre-releases named 1.2.N-beta.K, a separate BETA file on Nexus). Every beta also contains all stable fixes. A feature moves to stable after it has been in the beta for a while without bug reports.

The in-game update check follows the line you installed. To switch, install the package of the other line, or set UpdateChannel = stable / beta in UserData\MelonPreferences.cfg.

## Install in detail

Download `releases/Iron_Admiral_1.2.77.10_Nexus.zip`, extract anywhere, run `Install.bat` (plain PowerShell, no exe). It backs up your `Mods` folder, installs MelonLoader 0.6.6 if missing (downloaded from the official LavaGang release, SHA-256 verified) and copies the mod. `Uninstall.bat` restores the previous state. Start a new campaign.

Already running MelonLoader 0.6.6? Take `releases/Iron_Admiral_1.2.77.10_Mods_only.zip` and extract it into the game folder, so that you get `<game>/Mods/TweaksAndFixes.dll`, `DIP_GG_Fixes.dll` and the CSV files.

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

- `payload/Mods/` - the mod data (CSV, TAF, DIP_GG_Fixes.dll, battleDoctrines.csv, techBonuses.csv)
- `source/DIP_GG_Fixes/` - C# source of the add-on (`Mod.cs`, build notes in `BUILD.md`)
- - `installer/` - Install.ps1 / Uninstall.ps1
- `releases/` - the two zips published on Nexus

## Credits and licences
Iron Admiral includes and builds on:
- **Dreadnought Improvement Project "The Great Game"** 1.3.6 HF1 - Brother Munro and the DIP team. Used under the Apache License 2.0 that Brother Munro published with [DIP on GitHub](https://github.com/brothermunro/Dreadnought-Improvement-Project), with his permission (basis: [licenses/DIP_LICENSE_SOURCE.md](licenses/DIP_LICENSE_SOURCE.md)). Hulls, parts, nations, map and technologies come from DIP; files taken from DIP and changed for Iron Admiral are marked as changed. Comparison: [Differences from DIP](https://github.com/KlaudiuszLedwoch/Iron-Admiral/wiki/Differences-from-DIP).
- **TweaksAndFixes** 3.21.1 - NathanKell, DukeDagor (MIT): the modding framework Iron Admiral runs on
- **MelonLoader** 0.6.6 - LavaGang (Apache 2.0)
- **Ultimate Admiral's Shippack** - Badkapp / MarineWerft (free use with credit, not for paid mods)
- **CV Barebone Modification** - ChonkE (MIT)
- **Fleet Operations** - wangchuanyu666 / MaoDDi (MIT)
- **UAD Vanilla Plus** - xxfivebyfivexx (ideas, reimplemented with credit); **Custom Task Force Names** - GorillaLegs1 (idea)

Third-party licences: [NOTICE.md](NOTICE.md) and `licenses/` (the same texts ship in both downloads and in the game under Help > Credits and licences). My own work (1.2.48 and later): [LICENSE](LICENSE).

Unofficial fan modification, not affiliated with Game-Labs.

Licence: from version 1.2.48 my own work is covered by [LICENSE](LICENSE) - free to play, change for private use and make videos about; republishing, forks and reuse in other mods need my written permission; no commercial use. Versions up to 1.2.47 remain under Apache 2.0. Contributions are welcome under [CONTRIBUTING.md](CONTRIBUTING.md).
