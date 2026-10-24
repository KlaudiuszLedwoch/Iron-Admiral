# Iron Admiral – what we change compared to DIP, and why

Iron Admiral is built on DIP GG Extended 1.3.5. DIP brings a lot of content (hulls, parts, playable nations, the map, technologies) that we keep. Many of its combat numbers, however, were changed without sources, and some contradict physics or history. This list shows every value we change, the value in DIP and in the unmodded game (vanilla), the value we use, and where it comes from.

**Rule:** where a historical source exists, we use the historical value first – even if the game gets harder or stranger somewhere. The rest of the game is then recalibrated around history, not the other way round. Every change has a switch in the F10 menu (most of them in the "Battle AI" tab) and arrives in the beta first.

Know a better source? Open an issue or write on Nexus/Steam – we will check it and change the value.

Status: ✅ in the beta · 🔧 in work · 📋 planned

---

## 1. Ballistics ✅ (F10: RealisticBallistics, on)

| | Vanilla | DIP | Iron Admiral | Source |
|---|---|---|---|---|
| Time of flight, 15" at ~9–11 km | ~13 s | ~13 s | ~14–17 s (by range and muzzle velocity) | NavWeaps 15"/42 Mk I range table: 9,144 m – 14.4 s |
| Angle of fall, 15" at ~9–11 km | ~1.5° | ~1.5° | ~7–9° | NavWeaps: 9,144 m – 7.0°; 13,716 m – 13.8°; 18,288 m – 21.1° |
| Gravity used for shell arcs | ~3.1 m/s² | ~3.1 m/s² | 9.81 m/s² (plus drag) | physics |

How: every shell's arc is rebuilt from a point-mass model with quadratic air drag (air density falling with height, drag rising through the sound barrier), shell mass from calibre, and a drag coefficient by era – blunt 2 crh shells before ~1912, 4 crh shells by 1918, longer shells later. The model is fitted to the 15"/42 Mk I and 12"/45 Mk X range tables. Long-range fire plunges onto decks again and arrives as late as it really did.

## 2. Hit rates by era 🔧 (F10: EraAccuracy, on; EraAccuracyScale)

Measured with DIP values: battleships in a 1916 battle at 10–16 km hit with 30–40 % of their 15" shells – about ten times more than in real battles. The hit chance is now scaled by the year of the battle and calibrated in test battles against:

| Battle | Guns | Range | Hits |
|---|---|---|---|
| Tsushima 1905 | Japanese 12" | ≤ 6 km | ~9 % |
| Dogger Bank 1915 | British 13.5" | ~18 km | 0.4–1.6 % |
| Jutland 1916 | all heavy guns | 10–19 km | British 2.7 %, German 3.4 % |
| River Plate 1939 | 11" / 8" / 6" | 10–18 km | 1.7–2.4 % / 1.6 % / 0.8 % |
| Denmark Strait 1941 | Bismarck 15" | 16–20 km | ~5 % |
| Guadalcanal, Nov 1942 | Washington 16", radar | 7.5 km, night | 11–27 % |
| North Cape 1943 | Duke of York 14", radar | 11–20 km, night | 10 hits, 31 of 52 salvoes straddling |

Sources: Campbell, *Jutland: An Analysis of the Fighting* (tables summarised on Wikipedia "Damage to major ships at the Battle of Jutland"); navweaps.com; battle reports summarised on Wikipedia.

## 3. Damage model ✅ (F10: HistoricalDamage, on)

| Parameter | Vanilla | DIP | Iron Admiral | Source |
|---|---|---|---|---|
| over_penetration (shell passes through without exploding) | 0.32 | 0.002 | 0.10 | US Navy War Damage Report No. 57 (South Dakota, Nov 1942 – most hits passed through, clean holes, but real damage); Samar 1944 (Gambier Bay's engine room flooded by a shell passing through); Bismarck 1941 (one 14" shell through the bow: 1,000–2,000 t of water, −2 knots, 9° list) |
| over_penetration_threshold | 2 | 3.5 | 3.0 | long fuze delays of Japanese AP (WDR 57) |
| armor_damage (shell breaking up on armour) | 0.42 | 0.05 | 0.10 | WDR 57: six shells broke up on South Dakota's armour – almost nothing behind it, but structure beside it damaged |
| fire_damage | 1.6 | 0.33 | 0.30 | WDR 29 (Astoria, Quincy, Vincennes at Savo – fire killed through ammunition, not by burning the structure); Seydlitz at Dogger Bank 1915 burned 6.5 t of propellant and survived |
| section_damage_torpedo | 1750 | 5200 | 2000 in 1900 → 5200 in 1940 | warheads: 18" 1894 – 54 kg; 18" Mk VII 1910 – 145 kg; 21" WWI – 127–234 kg; Mk 15 – 224–373 kg; Type 93 – ~490 kg (navweaps torpedo pages). Sinkings: destroyer – 1 hit, cruiser – 1–2, WWII battleship – 3–5 (Prince of Wales 4, Barham 3) |
| torpedo_damage_ammo_detonation | 690 | 5200 | as above | as above |

Kept from DIP because it is closer to history: fire spread 20, torpedo tube explosions ×10 (rare, catastrophic – Suzuya, Mikuma, Furutaka), torpedo flooding 15.

## 4. Weights 📋

Planned (weights change existing designs, so they need a careful switch for running campaigns):

| | Vanilla | DIP | History | Planned |
|---|---|---|---|---|
| Casemate per gun | – | – | ⅓–¼ of a turret mount per gun (15 cm C/06 16–18 t vs Bismarck 15 cm 55–58 t per gun; 5"/51 10–12 t vs 5"/38 35–39 t) | 0.3–0.4 × turret |
| Machinery weight bb / bc / ca / cl | 3.5 / 2.4 / 0.95 / 0.95 | 1.6 / 1.5 / 1.4 / 1.3 | battleships 1.5–2× heavier per hp than cruisers (88 kg/hp 1905 → 31–36 kg/hp 1940; cruisers 15–21) | 1.6–1.8 / 1.4–1.5 / 0.9–1.1 |
| Fuel (op. range, % of displacement) | 2–10 | 14–28 | Dreadnought 18.4 %, Bismarck 15.7 %, Richelieu 15.8 %, Myoko ~20 % | 5–20 |
| Armour of secondary mounts | 0.125 | 0.08 | armour 25–40 % of an armoured mount (US 5": 47.9 t at 0.75" vs 77.4 t at 2.5") | 0.15–0.25 |
| Torpedo tubes | 8 / 16 / 36 | 3.1 / 1.5 / 3.1 | ~4 t per tube (Type 92 quadruple 15–18 t) | quadruple 15–18 t |

## 5. Small and medium guns 📋

| Calibre | Rate of fire vanilla / DIP / history | Range vanilla / DIP / history |
|---|---|---|
| 3" | 12.6 / 12 / 15–20 | 9.1 / 6.0 / 8.5 → 13.3 km |
| 5" | 4.6 / 5 / 8–9 (5"/51), 12–15 (5"/38) | 10.5 / 7.0 / 16.6–17.3 km |
| 6" | 3.6 / 4 / 4 in action | 11.2 / 7.5 / 13.4–14.5 → 19.7–23.6 km |
| 8" | 2.5 / 2 / 3–4 | 12.95 / 9.1 / ~28 km (WWII) |

Also planned: small guns (2"–3") in casemates, as on real ships of the 1890s–1910s.

## 6. Dispersion 📋

DIP narrowed dispersion to a point (`splash_disp_far` 555 → 100, `max_shell_dispersion` 65 → 10). Real salvo patterns: WWI at 11 km – 15" 180 m, 13.5" 270 m, 12" 370 m; USN 1917 14" at 16.5 km ~900 m; US 16"/50 at 19 km ~820 m; Massachusetts 1942: 180–270 m in range. Dispersion will follow era and calibre.

## 7. Optional modes 📋

- **Historical parts:** parts only in their own era and for the nations that had them (the free design of DIP stays available – default or second option).
- **Historically plausible wars:** wars following the real rivalries of each era.

## Main sources

navweaps.com (gun, shell and torpedo pages, range tables); US Navy War Damage Reports No. 29 and No. 57; N. J. M. Campbell, *Jutland: An Analysis of the Fighting*; kbismarck.com; navalgazing.net (spotting, fuzes, secondary armament); ship monographs summarised on the de/ru/en Wikipedia pages of the classes listed above.
