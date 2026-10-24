# Iron Admiral – what I change compared to DIP, and why

Iron Admiral is built on DIP GG Extended 1.3.5. DIP brings a lot of content (hulls, parts, playable nations, the map, technologies) that I keep. Many of its combat numbers, however, were changed without a stated source, and some of them contradict physics or history. This page lists the values I change: the value in DIP, in the unmodded game (vanilla) where I know it, the value I use, and where the reasoning comes from.

**Rule:** where a historical source exists, I use the historical value first, even if the game gets harder somewhere. The rest of the game is then recalibrated around history, not the other way round. Every change has its own switch in the F10 menu ("Battle AI" tab) and goes to the beta first.

**Technology, not the calendar:** every effect follows the technology fitted to the individual ship - its fire-control equipment (rangefinder or radar), its shell and propellant technologies, the generation of each gun and the torpedo technologies. The calendar year is never used. Where a historical table is indexed by year, the year is the historical year of the technology the ship actually carries.

**Status of this page:** work in progress. Some values below are taken directly from sources; others are my estimates inside the range the sources suggest – they are marked as estimates. Calibration continues with every test battle and every played battle, so numbers will change. Know a better source or a better figure? Write on Nexus or Steam, or open an issue – I will check it and change the value.

Status: ✅ in the beta · 🔧 in work / next beta · 📋 planned

---

## 1. Ballistics ✅ (F10: RealisticBallistics, on)

What I measured in the game with DIP values (test battle, 1916):

| | DIP (measured) | Iron Admiral (measured) | Range table |
|---|---|---|---|
| 15" at 10–12 km: time of flight / angle of fall | 12.8 s / 1.5° | ~17 s / ~9° | 15"/42 Mk I: 9,144 m – 14.4 s / 7.0°; 13,716 m – 23.5 s / 13.8° (muzzle velocity 747 m/s) |
| 15" at ~18 km: angle of fall | – | 20.8° | 18,288 m – 21.1° |
| Effective gravity of the shell arcs | ~3.1 m/s² | 9.81 m/s² plus air drag | physics |

The 15" guns in the test had a higher muzzle velocity (~810 m/s) than the Mk I table, so slightly shorter times are expected.

How: every shell's arc is rebuilt from a point-mass model with quadratic air drag (air density falling with height, drag rising through the sound barrier). Shell mass comes from the calibre. The drag coefficient depends on the era: blunt shells before ~1912, longer-nosed shells later. The model is fitted to the 15"/42 Mk I and 12"/45 Mk X range tables (navweaps.com).

How the shell is modelled: drag follows the shell-nose technologies in the ship's design (blunt early shells; the improved nose, 1907; the shells of 1927). Shell weight and muzzle velocity come from the game for that gun and ship, so light/heavy shells and propellant technologies count.

## 2. Hit rates ✅ (F10: EraAccuracy, EraAccuracyRadar, EraAccuracyScale)

Measured with DIP values: in a 1916 test battle, battleships at 10–16 km hit with 40–70 % of their 15" shells; in a 1940 test battle, 70–90 %. That is ten to twenty times more than in real battles.

With the change, the 1916 test gives 15" 2–4.5 % and 12" 0–1.3 % (Jutland, all heavy guns: British 2.7 %, German 3.4 %). The WWII values are not calibrated yet.

Next steps (in work):
- The hit-chance factor follows the **fire-control equipment fitted to the shooting ship** (the historical year of its rangefinder or radar technology), not the calendar. It represents **optical** fire control; ships with **fire-control radar** get an extra multiplier (EraAccuracyRadar, default 3). In radar A/B tests the game's own radar component changed hit rates very little.
- The generation of the gun (Mk) will be taken into account as well.

| Battle | Guns | Range | Hits | Note |
|---|---|---|---|---|
| Tsushima 1905 | Japanese 12" | ≤ 6 km | ~9 % | optical, short range |
| Dogger Bank 1915 | British 13.5" | ~18 km | 0.4–1.6 % | optical |
| Jutland 1916 | all heavy guns | 10–19 km | British 2.7 %, German 3.4 % | optical |
| River Plate 1939 | 11" / 8" / 6" | 10–18 km | 1.7–2.4 % / 1.6 % / 0.8 % | optical |
| Denmark Strait 1941 | Bismarck 15" | 16–20 km | ~5 % | optical |
| Guadalcanal, Nov 1942 | Washington 16" | 7.5 km, night | 11–27 % | **fire-control radar** |
| North Cape 1943 | Duke of York 14" | 11–20 km, night | 10 hits, 31 of 52 salvoes straddling | **fire-control radar** |

Sources: N. J. M. Campbell, *Jutland: An Analysis of the Fighting* (figures as summarised on Wikipedia, "Damage to major ships at the Battle of Jutland"); navweaps.com; the battle articles on Wikipedia.

## 3. Damage model ✅ (F10: HistoricalDamage, on)

| Parameter | Vanilla | DIP | Iron Admiral | Basis |
|---|---|---|---|---|
| over_penetration (shell passes through without exploding) | 0.32 | 0.002 | 0.10 (estimate) | Shells that passed through still did real damage. Examples: US Navy War Damage Report No. 57 (South Dakota, Nov 1942 – most hits passed through without exploding); Samar 1944; Bismarck 1941 (a shell through the bow let in a large amount of water and cost speed). DIP's value makes such hits almost harmless. |
| over_penetration_threshold | 2 | 3.5 | 3.0 (estimate) | between the two; long fuze delays of Japanese AP shells (WDR 57) |
| armor_damage (shell breaking up on armour) | 0.42 | 0.05 | 0.10 (estimate) | WDR 57: shells that broke up on South Dakota's armour did little behind it, but damaged structure nearby |
| fire_damage | 1.6 | 0.33 | 0.30 (estimate) | Fires rarely destroyed ships by burning the structure; the danger was ammunition (Seydlitz at Dogger Bank 1915 survived a propellant fire; WDR No. 29 on the cruisers lost at Savo) |
| section_damage_torpedo | 1750 | 5200 | 1750 (the game's base) | The game already scales every torpedo by the torpedo technologies of the ship that fires it (size, propulsion, generation), which follows the historical growth of warheads: ~54 kg (18", 1890s), ~145 kg (1910), 127–234 kg (WWI), 224–373 kg (Mk 15), ~490 kg (Type 93) – navweaps.com torpedo pages. DIP raised the base to the WWII value on top of those technologies. |
| torpedo_damage_ammo_detonation | 690 | 5200 | as above | as above |

Kept from DIP: fire spread, torpedo-tube explosions and torpedo flooding values – they are closer to the record than vanilla.

Torpedo damage therefore follows the torpedo technologies of each ship. The ammunition-detonation value goes back to the game's 690.

## 4. Dispersion ✅ (F10: EraDispersion)

DIP narrowed dispersion almost to a point (`splash_disp_far` 555 → 100, `max_shell_dispersion` 65 → 10). Recorded salvo patterns: WWI at ~11 km – 15" ~180 m, 13.5" ~270 m, 12" ~370 m; US 16"/50 at ~19 km ~820 m; Massachusetts 1942: 180–270 m in range. Base: the game's own (vanilla) dispersion values. Now: the shared dispersion values follow the average fire-control technology of the ships in the battle, and the miss distance of every shot is then scaled by the shooting ship's own fire-control technology – older equipment spreads wider, newer equipment tighter.

## 5. Guns by generation ✅ (F10: HistoricalGuns)

Rate of fire and range in the game depend on the **gun generation** (Mk 1–5). In test battles the game fits generation 2 around 1906, generation 3 around WWI and generation 5 in WWII. Every generation of every calibre from 3" to 18" is set to real guns of its period: the rated rate of fire and the range at the elevation of the period's mountings (navweaps.com). The game's value for each gun is scaled against its base value, so crew, technology and mounting modifiers still apply on top. "–" means no gun of that calibre in that period, or no usable figure: the game's value stays.

| Calibre | Mk 1 (1890s) | Mk 2 (1899–1908) | Mk 3 (WWI) | Mk 4 (1920–35) | Mk 5 (WWII) |
|---|---|---|---|---|---|
| 3" | 15/min | 15/min, 8.8 km | 17.5/min, 8.5 km | 17.5/min, 12.5 km | 17.5/min, 13.3 km |
| 4" | 8.5/min, 9.6 km | 12/min, 12.2 km | 11/min, 10.5 km | 12/min, 16 km | 15 km |
| 5" | 8.5/min, 11.8 km | 7/min, 17.4 km | 8.5/min, 14.5 km | 10/min, 16.3 km | 13.5/min, 16.9 km |
| 6" | 6/min, 11.4 km | 6/min, 15 km | 6/min, 14 km | 7/min, 24.3 km | 9/min, 21.6 km |
| 7"–7.5" | 2/min, 11.5 km | 3.75/min, 13.9 km | 3.3/min, 18.1 km | 5.5/min, 19.3 km | – |
| 8" | 2.4/min, 14.6 km | 3/min, 18.4 km | 5/min, 19.1 km | 3.7/min, 29 km | 4/min, 30.7 km |
| 9.2"–9.4" | 2/min, 12.4 km | 2/min, 15.5 km | 3.5/min, 14.1 km | – | – |
| 10" | 1.5/min, 9.2 km | 2.5/min, 15.9 km | 1.5/min | – | – |
| 11" | 0.5/min, 15.9 km | 2/min, 18.8 km | 3/min, 18.5 km | 2.5/min, 36.5 km | 3.5/min, 40.9 km |
| 12" | 1.3/min, 11.9 km | 1.75/min, 15.8 km | 2/min, 19.7 km | 2.5/min, 21.9 km | – |
| 13"–13.5" | 0.4/min, 11.3 km | – | 1.75/min, 21.8 km | – | – |
| 14" | – | – | 1.6/min, 19.7 km | 1.75/min, 27.3 km | 2/min, 35.3 km |
| 15" | – | – | 2.25/min, 22.4 km | – | 2/min, 39.2 km |
| 16" | – | – | – | 1.75/min, 35.7 km | 2/min, 36.2 km |
| 18" | – | – | 1/min, 26.4 km | – | 1.75/min |

Reference guns include: GB 4"/40 QF, US 5"/40, GB 6"/40 QF, German 15 cm L/40, GB 12"/35 (Mk 1); US 3"/50, German 10.5 cm L/40, US 5"/50, GB 6"/45 Mk VII, GB 7.5"/45, US 8"/45, GB 9.2"/47, GB 10"/45, German 28 cm L/40, GB 12"/40 (Mk 2); US 5"/51, German 21 cm L/45, GB 9.2"/50, German 28 cm L/45 and L/50, US 12"/50, GB 13.5"/45, US 14"/45, GB 15"/42, German 38 cm L/45, GB 18"/40 (Mk 3); US 4"/50 Mk 9, US 5"/25, US 6"/53, GB 6"/50 Mk XXIII, GB 7.5"/45 Mk VI, GB 8"/50, US 8"/55, German 28 cm C/28, US 14"/50, GB 16"/45 (Mk 4); US 5"/38, German 20.3 cm C/34, German 28 cm C/34, GB 14"/45 Mk VII, German 38 cm C/34, Italian 381/50, US 16"/45 and 16"/50, Japanese 46 cm (Mk 5). Where several guns are listed, the value is their average.

Also planned: small guns (2"–3") in casemates, as on real ships of the 1890s–1910s.
## 6. Weights 📋

Planned with a separate switch, because changing weights changes existing designs in a running campaign.

| | Vanilla | DIP | History | Direction |
|---|---|---|---|---|
| Casemate mount per gun | – | – | roughly ⅓–¼ of a turret mount per gun (e.g. German 15 cm casemate 16–18 t vs a 15 cm twin turret 55–58 t per gun) | casemates lighter than turrets |
| Machinery weight factor bb / bc / ca / cl | 3.5 / 2.4 / 0.95 / 0.95 | 1.6 / 1.5 / 1.4 / 1.3 | battleship machinery heavier per horsepower than cruiser machinery | battleships heavier per hp than cruisers |
| Fuel (operational range, % of displacement) | 2–10 | 14–28 | e.g. Dreadnought ~18 %, Bismarck ~16 %, Myoko ~20 % of full load | to be set from data |
| Armour of secondary mounts | 0.125 | 0.08 | armour is a large share of an armoured mount's weight | heavier |
| Torpedo tubes | 8 / 16 / 36 | 3.1 / 1.5 / 3.1 | e.g. a quadruple Type 92 mount 15–18 t | to be set from data |

## 7. Optional modes 📋

- **Historical parts:** parts available only in their own era and for the nations that had them. The free design of DIP stays available.
- **Historically plausible wars:** wars following the real rivalries of each era.

## Main sources

- navweaps.com (gun, shell and torpedo pages, range tables).
- US Navy War Damage Reports No. 29 and No. 57.
- N. J. M. Campbell, *Jutland: An Analysis of the Fighting* (via the Wikipedia summary tables).
- kbismarck.com.
- navalgazing.net (spotting, fuzes, secondary armament).
- Wikipedia articles on the battles and ship classes named above.
