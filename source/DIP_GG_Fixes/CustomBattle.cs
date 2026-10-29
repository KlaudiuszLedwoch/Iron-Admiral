// 1.11.82: custom battles - TweaksAndFixes' "Pregenerated" switch off by default.
// For each ship in a custom battle TAF tries: (1) a shared design, (2) a predefined design, (3) the ship generator.
// Iron Admiral ships no predefined-design file, so in step 2 TAF loads the game's built-in designs (31692, about 8 s), which
// refer to technologies DIP removed (submarine_exp_1 and others): every one of them fails ("no such TechnologyData",
// "Failed to load predef ... Trying again", about 20 tries per ship type) before step 3 builds the ship anyway.
// With the switch off TAF goes from step 1 straight to the generator. Shared designs (the mod's historical classes,
// the Shippack designs, the player's own) stay the first choice. TAF keeps the switch only for the running game
// (UiM.skirmishSetupMod.usePredefs, not saved), so it is set once per game start on the main menu; the player can turn it
// on again with the "Pregenerated" button in the custom battle screen (e.g. after installing predefined-design files).
// Switch: CustomBattlePregenerated (false = off by default).
// Historical classes first: in custom battles TAF asks for shared designs through its own CampaignControllerM.GetSharedDesign
// (not the game's method that HistPriority patches), so the same historical-first choice is hooked there too: when a
// historical class of the nation exists for the battle year (up to HistoricalDesignsYearsBack years older), it is taken
// before any other shared design. Switch: HistoricalDesignsFirst.
using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class CustomBattle
    {
        internal static MelonPreferences_Entry<bool> Pregenerated, NoShared;
        static bool applied, labelDone; static int errors; static object setup; static FieldInfo usePredefs;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            HistBack = cat.CreateEntry("CustomBattleHistoricalYearsBack", 6, null, "Custom battles: a historical class is chosen only when it is at most this many years older than the battle year (campaign: HistoricalDesignsYearsBack); older - the ship generator designs a ship for the year"); // 1.11.367 (#80)
            MaxAge = cat.CreateEntry("CustomBattleDesignMaxAge", 8, null, /* 1.11.367: 15 -> 8 (an 8-year-old pre-dreadnought got through in 1916) */ "Custom battles: a shared design older than this many years before the battle year is not used - the ship generator designs a ship for the year (0 = any age)"); // 1.11.363 (#80)
            NoShared = cat.CreateEntry("CustomBattleNoSharedDesigns", false, null, "Author's tool: custom battles take no shared design - every ship comes from the ship generator (generator tests)");
            Pregenerated = cat.CreateEntry("CustomBattlePregenerated", false, null,
                "Custom battles: let TweaksAndFixes use predefined designs when no shared design fits (off = straight to the ship generator; the game's built-in predefined designs do not load in Iron Admiral). The \"Pregenerated\" button in the custom battle screen still switches it for the running game");
        }

        internal static void Apply(HarmonyLib.Harmony h)
        {
            try
            {
                var t = HarmonyLib.AccessTools.TypeByName("TweaksAndFixes.CampaignControllerM");
                var m = t == null ? null : HarmonyLib.AccessTools.Method(t, "GetSharedDesign");
                if (m == null) { MelonLogger.Warning("[DIP GG Fixes][CustomBattle] TweaksAndFixes GetSharedDesign not found - historical-first in custom battles off"); return; }
                h.Patch(m, new HarmonyLib.HarmonyMethod(typeof(CustomBattle).GetMethod(nameof(PreShared), BindingFlags.Static | BindingFlags.NonPublic)),
                new HarmonyLib.HarmonyMethod(typeof(CustomBattle).GetMethod(nameof(PostShared), BindingFlags.Static | BindingFlags.NonPublic))); // 1.11.363 (#80)
                MelonLogger.Msg("[DIP GG Fixes][CustomBattle] historical classes first in custom battles: hooked");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][CustomBattle] hook failed: " + e.Message); }
        }
        static bool PreShared(Il2Cpp.Player player, Il2Cpp.ShipType shipType, int year, System.Collections.Generic.List<Il2CppSystem.Guid> ignore, ref Il2Cpp.Ship __result)
        {
            try
            {
                if (!Il2Cpp.GameManager.IsCustomBattle) return true;
                // 1.11.244 (author's tool): generator tests - no shared design is offered, every ship comes from the generator
                if (NoShared != null && NoShared.Value) { __result = null; return false; }
                // 1.11.367 (#80, test 29.10 10:05: usa bb 1916 got the historical class Mississippi (BB-23) of 1908 - a pre-dreadnought,
                // the newest US battleship class in the set): in custom battles a historical class may be at most
                // CustomBattleHistoricalYearsBack years older than the battle, otherwise the generator designs the ship
                var s = HistPriority.Pick(player, shipType, year, false, ignore, HistBack != null ? HistBack.Value : -1);
                if (s != null) { __result = s; return false; }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][CustomBattle] historical-first: " + e.Message); }
            return true;
        }

        // 1.11.363 (#80, player video 28.10: in a 1916 custom battle the AI got a pre-dreadnought and 1890s light cruisers): a shared
        // design much older than the battle year is not used - the ship generator designs a ship for the year instead
        internal static MelonPreferences_Entry<int> MaxAge, HistBack;
        static int oldDropped;
        static void PostShared(Il2Cpp.ShipType shipType, int year, ref Il2Cpp.Ship __result)
        {
            try
            {
                if (__result == null || !Il2Cpp.GameManager.IsCustomBattle || MaxAge == null || MaxAge.Value <= 0) return;
                float y = __result.GetYear(__result);
                // 1.11.371 (#80, test 29.10 11:05: usa cl 1916 got "Matsushima", a design of a later year on the 1891 hull
                // cl_2_straightbow): the hull's technology year counts too - the older of the two decides
                int hy = 0; try { hy = ShipGenSpeed.HullYear(__result.hull != null && __result.hull.data != null ? __result.hull.data.name : null); } catch { }
                if (hy > 0 && (y <= 0f || hy < y)) y = hy;
                if (y <= 0f || year - y <= MaxAge.Value) return;
                if (oldDropped++ < 20) MelonLogger.Msg($"[DIP GG Fixes][CustomBattle] shared design {__result.vesselName} ({y:0}) for {shipType?.name} {year} is more than {MaxAge.Value} years old - the ship generator designs one for the year");
                __result = null;
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][CustomBattle] design age: " + e.Message); }
        }

        static bool Find()
        {
            if (usePredefs != null) return true;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name != "TweaksAndFixes") continue;
                var ui = a.GetType("TweaksAndFixes.UiM"); if (ui == null) break;
                var f = ui.GetField("skirmishSetupMod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); if (f == null) break;
                setup = f.GetValue(null); if (setup == null) return false;
                usePredefs = setup.GetType().GetField("usePredefs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                break;
            }
            return usePredefs != null;
        }

        static bool hooked;
        internal static void Tick()
        {
            try
            {
                bool menu = false, setupScr = false; try { menu = Il2Cpp.GameManager.IsMainMenu; setupScr = Il2Cpp.GameManager.IsCustomBattleSetup; } catch { return; }
                // hooked on the first main menu, when TweaksAndFixes is surely loaded
                if (!hooked && menu) { hooked = true; Apply(new HarmonyLib.Harmony("DipGgFixes.CustomBattle")); }
                if (Pregenerated == null || Pregenerated.Value) return;
                if (!applied && menu)
                {
                    applied = true;
                    if (!Find()) { MelonLogger.Warning("[DIP GG Fixes][CustomBattle] TweaksAndFixes switch for predefined designs not found - left as it is"); return; }
                    usePredefs.SetValue(setup, false);
                    MelonLogger.Msg("[DIP GG Fixes][CustomBattle] predefined designs in custom battles: off (shared designs, then the ship generator)");
                }
                // the button label may have been built before the switch changed: make it show the current value once
                if (applied && !labelDone && usePredefs != null && setupScr)
                {
                    var go = GameObject.Find("TAF Predef Designs"); if (go == null) return;
                    labelDone = true;
                    var tr = go.transform.Find("Text"); if (tr == null) return;
                    var t = tr.GetComponent<UnityEngine.UI.Text>(); if (t == null) return;
                    bool on = (bool)usePredefs.GetValue(setup);
                    string s = t.text ?? "";
                    if (!on && s.EndsWith("On")) t.text = s.Substring(0, s.Length - 2) + "Off";
                    MelonLogger.Msg("[DIP GG Fixes][CustomBattle] Pregenerated button shows: " + t.text);
                }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][CustomBattle] " + e.Message); }
        }
    }
}
