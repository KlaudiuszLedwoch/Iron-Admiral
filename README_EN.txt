IRON ADMIRAL 1.1
DIP The Great Game Extended (AI overhaul, one-click install)
for Ultimate Admiral: Dreadnoughts (Steam, Windows)
============================================================

Description as of 19 October 2026. Latest version: Iron Admiral 1.2.27 (DIP_GG_Fixes 1.11.49).

Iron Admiral is an extended edition of the Dreadnought Improvement
Project (DIP) "The Great Game": the same data, map and nations, plus dozens of balance,
economy and diplomacy changes and a new add-on, DIP_GG_Fixes, that
improves the campaign AI and the battle AI where CSV files alone cannot
reach. Data base: DIP The Great Game 1.3.5 (July 2026). The package is
complete: MelonLoader 0.6.6, TweaksAndFixes 3.21.1, the DIP data with
all changes, and the add-on's source code.
One-click install; uninstalling restores the pre-install state.
Extension author: Klaudiusz Ledwoch (Steam: Klod76).
Nexus: https://www.nexusmods.com/ultimateadmiraldreadnoughts/mods/66
GitHub (source code): https://github.com/KlaudiuszLedwoch/Iron-Admiral
Videos (the battle AI in action, captions from the battle log):
  https://youtu.be/DjmOu0KVnwE (three battles, 6 min)
  https://youtu.be/ZAPta33uJXE (two battles, 4 min)
  https://youtu.be/SZzuVT4X2qY (first battle uncut, 5 min)

--------------------------------------------------------------------------
WHY INSTALL IT
--------------------------------------------------------------------------

AN AI THAT FIGHTS LIKE A NAVY, NOT LIKE A SCRIPT
- AI light ships (torpedo boats, destroyers, light cruisers) charge
  battleships and heavy cruisers to deliver torpedoes instead of
  circling at the edge of gun range. Out of torpedoes or badly damaged,
  they withdraw. If they cannot escape because they are slower, they
  fight to the end.
- Any AI division that cannot outrun its pursuers stops fleeing and
  turns to fight. No more free kills on limping pre-dreadnoughts.
- Damaged, slow ships are detached from their division and retreat
  separately, so healthy ships are no longer dragged down to their speed.
- AI guns focus fire on targets that are already damaged.
- Every nation fights by its own historical doctrine, stored in
  Mods\battleDoctrines.csv: the Royal Navy keeps range and line, Japan
  charges with torpedoes from 16 km and accepts losses, Italy fights
  only with an advantage, Germany uses torpedo flotillas to cover a
  battle turn-away. Doctrines are based on the Grand Fleet Battle
  Orders, the USN War Instructions and the literature on the Kaisen
  Yōmurei; full table in DOKTRYNY_TAKTYCZNE_NACJI.md (PL).

A CAMPAIGN WHERE THE AI DOES NOT GIVE UP
- AI nations that cannot afford a battleship build cruisers instead of
  leaving their yards empty.
- When your fleet outgrows the strongest rival by half, AI nations
  receive emergency naval appropriations. Dominance has a price.
- Construction watch: if the game skips advancing an AI nation's ship
  construction in a turn, the add-on does it itself.
- Your campaign designs are exported automatically to the shared design
  pool, so the AI can build from them.

A WORLD THAT LIVES
- Sovereign minor nations (Latin America, Persia, Siam, the Balkans,
  Scandinavia and more) get their territories back. In the original DIP
  most of them existed only on paper; now they form alliances, buy ships
  and take part in wars as in vanilla.
- Minors have stronger province defence, doubled port capacity in the
  warship trade and order ships from you more often.
- Diplomacy has consequences: improvements are 1.5x stronger, worsening
  is 4x stronger, for the player and the AI alike.
- The post-war crisis hurts but no longer traps nations in an endless
  spiral (softer budget penalties, war inflation and a shorter minimum
  time to an economic-collapse peace).
- Ships are built in classes: the first ship of a new design is an
  expensive prototype; ships laid down after it is in service get cheaper
  and faster as the series grows, ordering a class together pays off, idle
  yards bid lower and overloaded ones charge more, sister ships are cheaper
  to keep. Same rules for the AI and for refits.
- Money has a price: loans, printing and windfalls raise inflation relative
  to the economy, cuts lower it; loan interest follows inflation and the
  credit rating, inflation erodes debt, unspent cash keeps its value. Press
  F8 on the campaign map for the treasury panel; inflation is shown in the
  Finances window, and a monthly treasury report follows each turn.
- In-game Help chapter "Iron Admiral - new mechanics"; campaign guide in
  GUIDE_EN.md.

HISTORICAL ACCURACY AND BATTLESHIP DESIGN FREEDOM
- Armour penetration from physics, not from a slider. DIP's penetration
  grew roughly linearly with calibre (about +58 mm per extra inch). Iron
  Admiral rebuilds the curves for all 21 calibres (1"-21") and all 5 gun
  generations with the De Marre formula (T/D = K x D^1.071 x (W/D^3)^0.714
  x V^1.429), calibrated on six documented navweaps.com penetration
  tables: US 8"/55, German 28 cm SK C/34, US 12"/50, British 13.5"/45 and
  15"/42, US 16"/50 Mk 7. Error against the real tables: 0.5-13%. Each
  calibre/generation is based on a real gun's shell weight and muzzle
  velocity (list in gun_sources_reference.csv). Result: small guns no
  longer scratch belt armour, 13"+ guns are strong but not linearly
  absurd, generations improve monotonically.
- Gun weights that follow real metal. Barrel weight is a power law
  W = 0.05564 x D^2.7732 t, calibrated on the 16"/50 Mk 7, the 15"/42
  Mk I and Yamato's 46 cm Type 94 (about 4% extrapolation error). Turret
  structure weight stays at DIP values after a power-law attempt
  collapsed small turrets to zero and made armouring them impossible.
- The naval treaties, modelled for the first time. DIP had no battleship
  tonnage cap at all. The 1919 hull-strength tech brings the Washington
  Treaty limit (35,000 t standard, the 5:5:3 world), 1936 brings the
  Second London Treaty escalator clause (45,000 t, triggered historically
  when Japan refused to sign), and in 1940 the treaties lapse. You feel
  the treaty era as a design constraint exactly when the real
  admiralties did.
- After the treaties: design freedom up to 240,000 t. Once the limits
  fall, the Super Battleship tier is no longer capped at DIP's
  78,500-130,000 t. Each of the 14 super-battleship hulls is stretched
  proportionally into a 200,000-240,000 t band (bb_8_china 200,000 t ...
  bb_6_iowa 224,000 t ... bb_7_bismarck 240,000 t), and the global BB
  ceiling is a hard 240,000 t instead of infinity. The number is
  deliberate: hull steel was never the limit (the 564,763 DWT Seawise
  Giant proved that), dry docks were; the German H-44 study (~131,000 t)
  was judged unbuildable on 1940s slipways. 200,000-240,000 t means
  "better infrastructure than the 1940s powers had, short of today's
  700 m docks". The ship H-44 could not be is buildable here, by you and
  by the AI.
- Hulls you could research but never build: fixed. In DIP, 29 "Modern
  Battleship" hull variants (Iowa, North Carolina, Bismarck, Richelieu,
  King George V, Littorio, Soyuz...) unlocked with minimum tonnages of
  42,500-66,000 t while the treaty cap was 35,000-45,000 t, so the tech
  was dead on arrival. Their minimum is now 35,000 t, closer to history
  than DIP's numbers (South Dakota ~37,000 t, KGV ~36,000 t, Bismarck
  ~41,700 t, Littorio ~40,700 t). The same audit fixed 23 earlier BB
  hulls, the whole Super Battleship tier at unlock time, 5 permanently
  unbuildable "Hybrid Destroyer" hulls (the last DD tech capped
  destroyers at 4,200 t and unlocked 4,450-4,750 t hulls in the same
  line), and a corrupted CSV row that silently dropped the unlock list
  for every modern battleship hull.
- Long Lance. The oxygen torpedo tech gives +100% range instead of +40%:
  the Type 93 reached 40,000 m at 33-35 kn, roughly three times any
  contemporary (US Mk 15: 13,700 m; UK Mk IX: 12,300 m; German G7a:
  12,000 m). Fully developed torpedoes now reach about 28,000-35,000 m.
- Radar that matters. DIP's "night vision" stat existed but no
  technology ever granted it. The three radar techs (1935/1940/1945)
  now give night visibility +20/35/50 and bad-weather accuracy
  +15/20/25. Second World War night actions become possible.

SHIPS AND TECHNOLOGY (SHORT LIST)
- Armour penetration rebuilt from scratch for 21 calibres and 5 gun
  generations using penetration physics (De Marre), instead of a linear
  growth with calibre.
- Barrel weights recomputed as power laws calibrated on real guns
  (16"/50 Mk7, 15"/42 Mk I, 46 cm Type 94).
- Naval treaties, which DIP did not model at all (battleships had no
  tonnage cap): Washington Treaty 35,000 t from the 1919 hull-strength
  tech, Second London Treaty escalator clause 45,000 t from 1936,
  treaties lapse in 1940.
- Raised battleship tonnage after 1940: the Super Battleship tier goes
  from DIP's 78,500-130,000 t to 200,000-240,000 t per hull
  (bb_8_china 200,000 t ... bb_7_bismarck 240,000 t), with 240,000 t as
  the hard ceiling instead of infinity. Modern Battleship hulls start at
  35,000 t; destroyer cap set to a realistic 4,200 t.
- Fixed hulls that could never be built in DIP because of wrong tonnage
  limits: Modern Battleship (29 variants), Super Battleship, earlier BB
  hulls and 5 destroyer hulls.
- Fixed a corrupted CSV row that blocked the modern battleship unlocks.
- Radar finally matters at night and in bad weather (aim_rangefinder
  11/12/13: night vision +20/35/50, weather resistance).
- Oxygen "Long Lance" torpedoes: +100% range instead of +40%.
- Torpedoes under automatic fire are launched more aggressively and from
  longer range (all ships).

RESEARCH AND NATIONAL CHARACTER
- Research about 20% faster than the previous calibration, technology
  cost x10, the bonus for the top research spender cut to a sane level.
- National tech specialisations strengthened x1.5: Japan in torpedoes
  and light cruisers, Germany in armour and optics, the USA in hulls and
  gunnery, and so on. Nations really differ.
- A new profile for the Byzantine Empire (Greece in DIP): heavy armour,
  the biggest calibres, fast heavy and light cruisers.
- Revolts and internal instability more frequent, stronger and
  geographically varied.

STABILITY
- The add-on catches two bugs in the game's own code
  (CheckMinorNationThreat, TryGenerateSmallerNationsBattle: "An item
  with the same key has already been added") that aborted turn
  processing in DIP. The turn completes normally; the error goes to the
  log.
- An AI nation at zero state budget no longer vanishes on the first
  turn: economic-collapse dissolution needs 24 consecutive months of
  crisis (losing all ports still dissolves at once).
- Ship-loading guard: a design saved without a turret-caliber list no
  longer kills battle loading (TweaksAndFixes 3.16.7 bug, also fixed
  in 3.21.1).

Every add-on behaviour has a switch and values in
UserData\MelonPreferences.cfg, section [DIP_GG_Fixes]; national
doctrines live in Mods\battleDoctrines.csv. Nothing is forced.

--------------------------------------------------------------------------
TEST STATUS
--------------------------------------------------------------------------
Confirmed in a test campaign (MelonLoader log): turn-error guards,
balance-of-power events, shared-design export, torpedo charges, fight
instead of fleeing, detaching slow ships, national doctrines. Not yet
tested over a long game: the direction of the doctrine distance
multiplier (distanceMult) and the economic changes across a whole
campaign. Full change history with reasoning, sources and test status:
CHANGELOG_EN.txt (72 sections).

--------------------------------------------------------------------------
INSTALLATION
--------------------------------------------------------------------------
The game itself does not read the Mods folder. The CSV data is loaded by
TweaksAndFixes, which is a MelonLoader mod, hence the installer sets up
MelonLoader for you.

Extract the ZIP anywhere and double-click Install.bat. It runs
installer\Install.ps1, a plain-text PowerShell script you can read first.
No exe. The script:
- finds the game through Steam (or asks for the folder),
- moves the current Mods folder (and MelonLoader, if it is not 0.6.6) to
  <game folder>\Iron_Admiral_backup\<date>\,
- downloads the official MelonLoader.x64.zip v0.6.6 from the LavaGang
  GitHub release (only when MelonLoader 0.6.6 is not already installed),
  verifies its SHA-256 (687b8260...3c0db) and aborts on a mismatch,
- installs MelonLoader and the mod files.
Internet access is needed during installation (MelonLoader download) and
on the first launch. Close the game before installing.

FIRST LAUNCH
The first start after installing MelonLoader takes a few minutes and
needs internet access - MelonLoader generates the game's support files.
A console window appears next to the game; this is normal.

SAVE GAMES
Start a new campaign. Province owners and other starting data apply to
new campaigns only. Older saves may load but are not supported.

UNINSTALL
Run Uninstall.bat from <game folder>\Iron_Admiral_backup\.
It restores the previous Mods folder and, if the installer replaced it,
the previous MelonLoader (or removes MelonLoader if there was none).

ONE-FILE SETUP SCRIPT
Install-IronAdmiral.ps1 (repository root and Nexus zip) installs the .NET 6
runtime, the VC++ Redistributable, MelonLoader 0.6.6 and the latest Iron
Admiral in one go; the old Mods folder is kept as Mods_backup_<date>.
Right-click > Run with PowerShell, or:
  powershell -ExecutionPolicy Bypass -File .\Install-IronAdmiral.ps1

INSTALLING MELONLOADER BY HAND (Mods_only package, or when the installer
cannot do it)
1. Close the game.
2. Install the .NET Desktop Runtime 6.0 (Windows x64):
   https://dotnet.microsoft.com/download/dotnet/6.0
3. Install the Microsoft Visual C++ Redistributable (x64):
   https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
4. Download MelonLoader.x64.zip from the official MelonLoader 0.6.6 release
   (Assets): https://github.com/LavaGang/MelonLoader/releases/tag/v0.6.6
   Only 0.6.6 is tested - TweaksAndFixes 3.21.1 needs it.
5. Unzip it straight into the game folder (Steam: right-click the game >
   Manage > Browse local files), so that the MelonLoader folder,
   version.dll and dobby.dll sit next to "Ultimate Admiral Dreadnoughts.exe".
6. Start the game once (a few minutes, internet needed), then close it.
7. Unzip the Mods_only package into the game folder so that
   <game folder>\Mods\TweaksAndFixes.dll exists, and start the game.

TROUBLESHOOTING
- ".NET 6.0 runtime" error at game start: MelonLoader 0.6.6 runs on .NET 6.
  Install the .NET Desktop Runtime 6.0 (Windows x64) from
  https://dotnet.microsoft.com/download/dotnet/6.0 (or: winget install
  Microsoft.DotNet.DesktopRuntime.6). The installer checks this since
  version 1.2.14.
- Mods_only zip: extract it into the game folder so that you get
  <game folder>\Mods\TweaksAndFixes.dll, DIP_GG_Fixes.dll and the CSV
  files. MelonLoader 0.6.6 must already be installed (version.dll and the
  MelonLoader folder next to the game exe). Zips up to version 1.2.13
  could extract as loose files named "Mods\..." in some unpackers; use
  1.2.14 or newer.
- Back to the unmodded game: run Uninstall.bat from
  <game folder>\Iron_Admiral_backup\, or delete version.dll, the MelonLoader
  folder and the Mods folder and let Steam verify the game files. Campaigns
  saved with the mod are not meant for the unmodded game.
- Logs for bug reports: <game folder>\MelonLoader\Latest.log.

--------------------------------------------------------------------------
ADD-ON SETTINGS (UserData\MelonPreferences.cfg, [DIP_GG_Fixes])
--------------------------------------------------------------------------
AutoShareDesigns / ShareToAllNations      shared designs
CruisersWhenPoor, PoorBigShipRatioFactor,
  PoorCruiserRatioFactor                  cruisers instead of BB
MinorPortCapacityMultiplier               minor port capacity
BalanceOfPowerEvents, BalanceTonnageRatio,
  BalanceEventChance, BalanceGrantMonths  balance of power
DiplomacyAttitudeMultiplier (1.5),
  DiplomacyNegativeAttitudeMultiplier (4) diplomacy
FixStalledConstruction                    construction watch
EconomicCollapseGraceMonths (24)          collapse grace period
LightShipTorpedoCharge, TorpedoChargeStartRange,
  TorpedoChargeRangeMargin,
  LightShipChargeAbortDamage,
  TorpedoRangeFallback                    torpedo charge
FightWhenCannotEscape, PursuitRange,
  EscapeSpeedMargin                       fight instead of fleeing
DetachSlowShips, SlowShipSpeedRatio,
  DetachCooldownSeconds                   detaching slow ships
FocusFire, FocusFireMinDamage             focus fire
BattleAiThinkSeconds, BattleAiLog         decision rate, log
Mods\battleDoctrines.csv                  doctrines per nation

--------------------------------------------------------------------------
KNOWN ISSUES
--------------------------------------------------------------------------
- The log still shows "An item with the same key has already been added.
  Key: <name> (Province)" - a game-code bug caught by the add-on; the
  turn completes normally.
- Messages like "failed to generate ship ... in 4 tries", "tonnage ...
  is out of bounds, clamped", "non-unique name in PartData" and
  "missing hull: ..._mission" come from the DIP data and are harmless.

--------------------------------------------------------------------------
CREDITS AND LICENSES (licenses folder)
--------------------------------------------------------------------------
- Dreadnought Improvement Project (DIP) "The Great Game" - Brother Munro
  and the DIP team. Apache 2.0 license. This package modifies DIP files;
  the changes are described in CHANGELOG_EN.txt.
- TweaksAndFixes - NathanKell. MIT license.
- MelonLoader - LavaGang. Apache 2.0 license.
- Iron Admiral / DIP GG Extended (data changes, DIP_GG_Fixes add-on,
  installer) -
  Klaudiusz Ledwoch (Steam: Klod76). Source code in the source folder,
  Apache 2.0 license.
- Ultimate Admiral: Dreadnoughts - Game-Labs. This is an unofficial fan
  modification; do not report issues with the modded game to Game-Labs.
