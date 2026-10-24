// 1.11.185 (beta): optional "historical parts" mode (HistoricalParts, F10, off by default - free design stays the default).
// Parts become available with the technology that brought them historically. Rule of the mod: technology, never the calendar.
// v1: the hulls DIP moved 5-20 years earlier go back to the technology that unlocks them in the unmodded game:
//   bc_5_austrian_pocket (Large Armored Cruiser): DIP hull_cruiser_46 (1909) -> hull_cruiser_51 (1929)
//   cl_4_china_experimental (Compact Light Cruiser): DIP hull_cruiser_9 (1908) -> hull_cruiser_19 (1925)
//   bb_3_usa_var (Experimental Dreadnought II): DIP hull_strength_7 (1907) -> hull_strength_12 (1919)
//   dd_us_flush ("Flush Deck" Destroyer): DIP hull_destroyer_13 (1912) -> hull_destroyer_18 (1918)
//   dd_2_german (Destroyer I): DIP hull_destroyer_8 (1903) -> hull_destroyer_10 (1908)
// (superfiring barbettes on pre-dreadnought hulls, late towers on early cruisers and cage masts are already gated in the mod's
// parts data for everyone.) The same rule applies to the AI and the player; designs already built are not changed.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HistoricalParts
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalParts", false, null, "Ship design (optional historical mode): parts become available with the technology that brought them historically - hulls that DIP made available 5-20 years early need their original technology (AI and player alike; free design is the default)");
        }
        static readonly Dictionary<string, string> NeedTech = new Dictionary<string, string>
        {
            { "bc_5_austrian_pocket", "hull_cruiser_51" }, { "cl_4_china_experimental", "hull_cruiser_19" },
            { "bb_3_usa_var", "hull_strength_12" }, { "dd_us_flush", "hull_destroyer_18" }, { "dd_2_german", "hull_destroyer_10" },
        };
        static readonly Dictionary<long, HashSet<string>> researched = new Dictionary<long, HashSet<string>>();
        static float nextClear; static int logged;
        static bool Has(Player p, string tech)
        {
            if (UnityEngine.Time.unscaledTime > nextClear) { researched.Clear(); nextClear = UnityEngine.Time.unscaledTime + 10f; }
            long k = p.Pointer.ToInt64(); HashSet<string> set;
            if (!researched.TryGetValue(k, out set))
            {
                set = new HashSet<string>();
                try
                {
                    var typed = p.technologiesResearchedAll.GetEnumerator();
                    var untyped = typed.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (untyped.MoveNext()) { try { var t = typed.Current; if (t != null && t.data != null) set.Add(t.data.name); } catch { } }
                }
                catch { }
                researched[k] = set;
            }
            return set.Contains(tech);
        }
        internal static void Check(PartData pd, Player p, ref bool result)
        {
            if (!result || On == null || !On.Value || pd == null || p == null) return;
            string need; if (!NeedTech.TryGetValue(pd.name, out need)) return;
            if (Has(p, need)) return;
            result = false;
            if (logged++ < 20) MelonLogger.Msg($"[DIP GG Fixes][Parts] historical parts: {pd.name} for {(p.data != null ? p.data.name : "?")} needs {need}");
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.IsPartAvailable), new Type[] { typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship) })]
    internal static class Patch_IsPartAvailable_Historical
    {
        static void Postfix(PartData __0, Player __1, ref bool __result) { try { HistoricalParts.Check(__0, __1, ref __result); } catch { } }
    }
}
