// 1.11.223 (author, 24.10): shared designs that refer to technologies, parts or components this mod does not have load anyway.
// The Shippack battleships (MHMW Valiant, Renown, Carnot, Tourville ...) carry submarine technologies (sub_surv_*) that DIP removed;
// the game logged "no such TechnologyData from save" 27 times per design and TweaksAndFixes gave up on the design ("Couldn't
// load") - 605 log lines and no modern battleship designs for the AI in the 24.10 session. After the game has read the shared
// design files (GameData.LoadSharedDesigns), every stored design is checked against the game data and the unknown entries are
// dropped from the store in memory (the files are not touched); the design then loads with the technologies it has.
// Switch: SharedDesignsSkipUnknownTech (F10, Campaign).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignTechFix
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SharedDesignsSkipUnknownTech", true, null, "Shared designs: technologies, parts and components the mod does not have are skipped when a design is read, instead of the whole design being refused (Shippack battleships with submarine technologies)");
        }
        static int errors;
        internal static void AfterLoad(GameData gd)
        {
            if (On == null || !On.Value || gd == null) return;
            try
            {
                var per = gd.sharedDesignsPerNation; if (per == null) return;
                int designs = 0, fixedDesigns = 0, techs = 0, parts = 0, comps = 0; var names = new List<string>(); var techNames = new Dictionary<string, int>();
                foreach (var kv in per)
                {
                    var list = kv.Value; if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i].Item1; if (s == null) continue; designs++;
                        int t0 = techs, p0 = parts, c0 = comps;
                        var tl = s.techs;
                        if (tl != null && gd.technologies != null)
                            for (int k = tl.Count - 1; k >= 0; k--) { string n = tl[k]; if (!string.IsNullOrEmpty(n) && !gd.technologies.ContainsKey(n)) { tl.RemoveAt(k); techs++; int c; techNames.TryGetValue(n, out c); techNames[n] = c + 1; } }
                        var pl = s.parts;
                        if (pl != null && gd.parts != null)
                            for (int k = pl.Count - 1; k >= 0; k--) { string n = null; try { n = pl[k] != null ? pl[k].name : null; } catch { } if (!string.IsNullOrEmpty(n) && !gd.parts.ContainsKey(n)) { pl.RemoveAt(k); parts++; } }
                        var cl = s.components;
                        if (cl != null && gd.components != null)
                            for (int k = cl.Count - 1; k >= 0; k--) { string n = null; try { n = cl[k].Value; } catch { } if (!string.IsNullOrEmpty(n) && !gd.components.ContainsKey(n)) { cl.RemoveAt(k); comps++; } }
                        if (techs != t0 || parts != p0 || comps != c0) { fixedDesigns++; if (names.Count < 12) names.Add($"{s.vesselName} ({kv.Key}, {s.YearCreated}): {techs - t0} tech, {parts - p0} parts, {comps - c0} comp."); }
                    }
                }
                MelonLogger.Msg($"[DIP GG Fixes][Designs] shared designs checked against the game data: {designs} designs, {fixedDesigns} with unknown entries dropped ({techs} technologies, {parts} parts, {comps} components){(names.Count > 0 ? " - " + string.Join("; ", names) : "")}");
                if (techNames.Count > 0) { var top = new List<KeyValuePair<string, int>>(techNames); top.Sort((x, y) => y.Value.CompareTo(x.Value)); MelonLogger.Msg("[DIP GG Fixes][Designs] unknown technologies dropped (" + techNames.Count + " names): " + string.Join(", ", top.ConvertAll(kv => kv.Key + " x" + kv.Value).GetRange(0, Math.Min(40, top.Count)))); }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Designs] unknown-entry check: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GameData), "LoadSharedDesigns")]
    internal static class Patch_LoadSharedDesigns_TechFix
    {
        static void Postfix(GameData __instance) { DesignTechFix.AfterLoad(__instance); }
    }
}
