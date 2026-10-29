// 1.11.77: historical designs first. When an AI nation (or the game for any nation) asks for a shared design of a ship
// type, a historical class of that nation is offered first if one exists for the year and the nation's technology covers
// it; otherwise the normal choice (TweaksAndFixes) runs unchanged. Historical classes come with the mod in
// Mods\IronAdmiralDesigns\Historical (design files + historical_ids.csv: id,nation,name).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class HistPriority
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<int> YearsBack;
        static HashSet<string> ids;
        static int logged;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalDesignsFirst", true, null, "Historical ship classes that come with the mod are chosen first when a nation picks a shared design (own nation, technology available)");
            YearsBack = cat.CreateEntry("HistoricalDesignsYearsBack", 12, null, "How many years back a historical class may still be chosen");
        }
        internal static HashSet<string> Ids()
        {
            if (ids != null) return ids;
            ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var f = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns", "Historical", "historical_ids.csv");
                if (File.Exists(f)) foreach (var l in File.ReadAllLines(f)) { var c = l.Split(','); if (c.Length >= 1 && c[0].Trim().Length > 30) ids.Add(c[0].Trim().TrimStart('﻿')); }
                MelonLogger.Msg($"[DIP GG Fixes][Historical] {ids.Count} historical classes known");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Historical] id list: " + e.Message); }
            return ids;
        }

        // TweaksAndFixes tech-match helpers (same test TAF uses for shared designs); looked up once by reflection
        static MethodInfo cacheTechs, techMatch, cleanup; static bool looked;
        static float TechRatio(Player p, Ship s)
        {
            if (!looked)
            {
                looked = true;
                var t = AccessTools.TypeByName("TweaksAndFixes.CampaignControllerM");
                if (t != null)
                {
                    cacheTechs = AccessTools.Method(t, "CachePlayerTechs");
                    techMatch = t.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "TechMatchRatio" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Ship));
                    cleanup = AccessTools.Method(t, "CleanupSDCaches");
                }
            }
            if (cacheTechs == null || techMatch == null) return -1f;
            try
            {
                var ps = cacheTechs.GetParameters();
                cacheTechs.Invoke(null, ps.Length == 2 ? new object[] { p, false } : new object[] { p });
                return (float)techMatch.Invoke(null, new object[] { s });
            }
            catch { return -1f; }
            finally { try { cleanup?.Invoke(null, null); } catch { } }
        }

        internal static Ship Pick(Player player, ShipType shipType, int year) { return Pick(player, shipType, year, true, null); }
        // 1.11.82: also used for custom battles (TweaksAndFixes' own shared-design call, no technology check, ignore list = designs
        // already placed in this battle)
        internal static Ship Pick(Player player, ShipType shipType, int year, bool checkTech, System.Collections.Generic.List<Il2CppSystem.Guid> ignore, int yearsBack = -1)
        {
            if (On == null || !On.Value) return null;
            return PickSet(Ids(), "historical class", "Historical", player, shipType, year, checkTech, ignore, yearsBack >= 0 ? yearsBack : YearsBack.Value);
        }
        // 1.11.394: shared by the historical classes and the player's own designs (PlayerDesigns, chosen before everything else)
        internal static Ship PickSet(HashSet<string> set, string what, string tag, Player player, ShipType shipType, int year, bool checkTech, System.Collections.Generic.List<Il2CppSystem.Guid> ignore, int yearsBack)
        {
            if (set == null || set.Count == 0 || player == null || shipType == null) return null;
            Il2CppSystem.Collections.Generic.List<Il2CppSystem.ValueTuple<Ship.Store, string>> designs = null;
            try { if (!G.GameData.sharedDesignsPerNation.TryGetValue(player.data.name, out designs)) return null; } catch { return null; }
            var owned = new HashSet<string>();
            try { foreach (var d in new Il2CppSystem.Collections.Generic.List<Ship>(player.designs)) owned.Add(d.id.ToString()); } catch { }
            var cands = new List<Ship.Store>();
            foreach (var tuple in designs)
            {
                var st = tuple.Item1; if (st == null) continue;
                string id; try { id = st.id.ToString(); } catch { continue; }
                if (!set.Contains(id) || owned.Contains(id)) continue;
                if (ignore != null) { bool skip = false; foreach (var g in ignore) if (g.ToString() == id) { skip = true; break; } if (skip) continue; }
                if (st.shipType != shipType.name) continue;
                int y = st.YearCreated;
                if (y > year || y < year - yearsBack) continue;
                cands.Add(st);
            }
            foreach (var st in cands.OrderByDescending(x => x.YearCreated))
            {
                var ship = Ship.Create(null, null, false, false, false);
                if (!ship.FromStore(st, new Il2CppSystem.Nullable<Il2CppSystem.Guid>(), null, null, false)) { try { ship.Erase(); } catch { } continue; }
                ship.SetActive(false);
                float r = checkTech ? TechRatio(player, ship) : -1f;
                if (r >= 0f && r < 0.9f) { try { ship.Erase(); } catch { } continue; }   // nation lacks the technology of this class
                if (ship.id == Il2CppSystem.Guid.Empty) ship.id = Il2CppSystem.Guid.NewGuid();
                if (logged++ < 40) MelonLogger.Msg($"[DIP GG Fixes][{tag}] {player.data.name} {shipType.name} {year}: {what} {st.vesselName} ({st.YearCreated}) chosen{(r >= 0f ? $", tech match {r:0.00}" : "")}");
                return ship;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(CampaignController), nameof(CampaignController.GetSharedDesign))]
    internal static class Patch_HistoricalFirst
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Player player, ShipType shipType, int year, ref Ship __result)
        {
            try
            {
                var s = PlayerDesigns.Pick(player, shipType, year, true, null) ?? HistPriority.Pick(player, shipType, year);
                if (s != null) { __result = s; return false; }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Historical] " + e.Message); }
            return true;
        }
    }
}
