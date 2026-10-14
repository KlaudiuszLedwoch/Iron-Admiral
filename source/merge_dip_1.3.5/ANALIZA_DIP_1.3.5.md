# Analiza DIP GG 1.3.5 względem bazy Iron Admiral (2026-10-14)

Źródło: tag GG1.3.5 (13.07.2026) z github.com/brothermunro/Dreadnought-Improvement-Project
("Patch Only" — bez plików predefinedDesigns.bin; pełna paczka na Nexus mod 7).
Baza naszych zmian: klon DIP v3 The Great Game (wersja bez numeru; recenzent: 1.0.1).
Nasze pliki: payload/Mods paczki Iron Admiral 1.0 (po sekcji 32 changelogu).

## Pliki — poziom plików
- Bez zmian w obu: aiAdmirals, battleTypeEx, compTypes, components, flags, stats, torpedoTubes.
- Zmienione tylko u nas (do zachowania): aiPersonalities, governmentMod, battleDoctrines (nasz), DIP_GG_Fixes.dll.
- Zmienione tylko w 1.3.5 (do przejęcia): English.lng, TweaksAndFixes.dll (3.16.7 → 3.21.1), accuracies, events,
  genarmordata, missions, partModels, players, ports, relationMatrix, shipNames, shipTypes, techGroups, techTypes,
  TAFData (nowe: genArmorDefaults.csv, locText, baseGamePartModelData, grafiki).
- Nowe w 1.3.5: German.lng, accuraciesEx.csv, mounts.csv, params_override.csv (116 wierszy TAF, m.in.
  taf_war_min_months_for_econ_collapse_peace=24 — u nas 12 w params.csv), predefinedDesignsData.csv
  (wskazuje predefinedDesigns.bin — brak w patch-only), sprites.csv, foldery Sprites, DecorativeShips, Default_Files.
- Zmienione w obu (scalanie komórkowe): guns, params, parts, penetration, provinces, technologies.

## Scalanie komórkowe
guns.csv: nasze 804 komórki (barrelW, shellW, shellV, penetration_1-5, baseWeight) vs 127 nowe
  (hitChanceCurver, accuracy_1/2, firerate, range, damageMod, ammo). 58 "konfliktów" to tylko zaokrąglenie
  w 1.3.5 (0.14330525 → 0.143) → nasze wartości. Rozłączne kolumny → scalanie bez strat.
params.csv: nasze 53 wartości, nowe 27 — zero konfliktów. Nowe w 1.3.5: revol_* -0.01→-0.001, dyspersja pocisków
  (shell_max_distance_dispersion 1500→500, max_shell_dispersion 65→10, splash_*), penetration_1st/2nd/3rd_layer_reduction
  0.6/0.3/0.125 → 0.45/0.33/0.25, torpedo_detonation_chance_mod 100→10, torpedo_damage_ammo_detonation 400→5200,
  section_damage_torpedo 4200→5200, flooding 10→15, acceleration/deceleration_mod 0.02875/0.03→0.06,
  approach_clamp 0.4→0.3, approach_clamp_torpedoes 1→2.5, ai_aggressiveness_exp_factor 1→1.3, max_gun_caliber_mod 0.9→0.93,
  nowy army_support_factor_borders. 36 kluczy taf_* przeniesionych do params_override.csv.
parts.csv: 249 nowych kadłubów (bb_7_scandinavia, bb_7_france, bb_9_china, bb_6_byzantine, bb_6_portugal, bb_5_*_compact,
  hull_bb_super_3_korea, bb_4_*F ...), 34 usunięte (duplikaty i warianty modernizacji), 1877 komórek zmienionych
  (param, speedLimiter, maxAllowedCaliber, hullWeightRatio, scale, countries, weight, stats...). Nasze: 68 komórek
  tonnageMin/Max. Konflikt: bb_7_china tonnageMin/Max (baza 74500/82500; my 45000/203000; 1.3.5 82500/95000).
penetration.csv: nasze 410 komórek (pełna przebudowa + Okun 16-21"); 1.3.5 zmienił tylko kalibry 1-2" → nasze w całości.
provinces.csv: nasze 595 komórek (kontrolerzy minorów, revoltChance, provinceArmyPercentage, ProvinceDefenderBonus);
  nowe 123 (neighbour_provinces 114 + pojedyncze). Konflikty: portugal revoltChance (my 5.25, nowe 5);
  mongolia controller_1940 (baza "v" błąd; my "mongolia"; 1.3.5 "china").
technologies.csv: 8 nowych wierszy (hull_strength_12_2, hull_strength_12_3, hull_cruiser_52_2 ...). difficulty:
  my = baza×0.1 (koszt badań), 1.3.5 = baza×0.025 (632 wierszy; część ×0.05–0.07) — DIP sam obniżył koszt
  4× mocniej niż my, przy domyślnych parametrach research_* (1.5/5/5/0.4/4/0.67/1.125/3). Kolumna effect: 168 zmian
  w 1.3.5 (nowe unlock/obsolete), nasze 5 (tonnage(bb;35000) w hull_strength_12, tonnage(bb;45000) w hull_strength_15,
  tonnage(dd;7500) w hull_destroyer_27 i inne) → do ponownego nałożenia na nowe stringi effect.
players.csv (tylko 1.3.5): kolory nacji, shipyardStart portugal/greece/netherlands/china → 8500, japan percentParty,
  usa startingyear_growth_mod.
