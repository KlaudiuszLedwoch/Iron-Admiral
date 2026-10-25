// 1.11.233 (author's decision 24.10, from the full generator log): the AI generator chose 15" and 16" guns and 8" casemates for a
// 24-28 000 t Dreadnought III hull (France, bb_2_france, 1922) - 34 500 t of parts on a 24 100 t hull, "Failed to reduce
// weight" on every attempt, 238 s lost, no ship. The hull's own limit (maxAllowedCaliber 16) is the designer's limit, not what
// the hull carries: historically a 22-28 000 t dreadnought carried 12"-14" (Courbet 12", Bretagne 13.4", Iron Duke 13.5",
// New York 14"), 15" came with 28-33 000 t (Bayern, Queen Elizabeth), 16" with 33-40 000 t (Nagato, Colorado, Nelson).
// This TweaksAndFixes build reads a hull param shipgen_limit(max_main_gun_cal:N; ...) for the AI generator (DIP sets it on
// the Battlecruiser I hulls: 12"); here every battleship and battlecruiser hull without such a limit gets one from its maximum
// tonnage, so the generator starts from guns the hull can carry instead of reducing turret diameters for minutes. The human
// designer is not limited (the param is read by the AI generator only). Switch: AiHullCaliberCap (F10, Campaign).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HullCaliberCap
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiHullCaliberCap", true, null, "Campaign: the AI ship generator's main calibre on a battleship or battlecruiser hull is capped by the hull's tonnage as history had it (up to 20 000 t 12\", 26 000 t 13\", 30 000 t 14\", 34 000 t 15\", 42 000 t 16\", 52 000 t 17\", above: the hull's own limit) its main turrets (6 up to 28 000 t, 5 up to 33 000 t, 4 above) and its secondary guns limited in number (12 up to 20 000 t, 14 up to 30 000 t, 16 up to 42 000 t, 20 above) - no 16\" guns and 8\" casemates on a 25 000 t hull that then fails on weight");
        }
        // cap by the hull's maximum tonnage (inches); 0 = no cap beyond the hull's own
        internal static int CapFor(float tonnageMax)
        {
            if (tonnageMax <= 0f) return 0;
            if (tonnageMax <= 20000f) return 12;
            if (tonnageMax <= 26000f) return 13;
            if (tonnageMax <= 30000f) return 14;
            if (tonnageMax <= 34000f) return 15;
            if (tonnageMax <= 42000f) return 16;
            if (tonnageMax <= 52000f) return 17;
            if (tonnageMax <= 72000f) return 18;   // 1.11.248: Yamato 65 000 t
            return 0;
        }
        internal static int SecCountFor(float tonnageMax)
        {
            if (tonnageMax <= 20000f) return 12;
            if (tonnageMax <= 30000f) return 14;
            if (tonnageMax <= 42000f) return 16;
            return 20;
        }
        // 1.11.246: main turrets by tonnage (Dreadnought 5 x 2 on 18 000 t, Iron Duke 5 x 2 on 25 000 t, Fuso 6 x 2 on 30 000 t, Queen
        //           Elizabeth / Nagato / Colorado 4 x 2 on 32-33 000 t, Nelson 3 x 3, Iowa 3 x 3) - the 25.10 test: 6 x 2 16" on a 32 000 t
        //           hull, 2 100 t over even with every secondary gun removed
        internal static int MainCountFor(float tonnage)
        {
            if (tonnage <= 28000f) return 6;
            if (tonnage <= 33000f) return 5;
            return 4;
        }
        static bool applied; static int errors;
        internal static void Apply(GameData gd)
        {
            if (On == null || !On.Value || gd == null || applied) return;
            try
            {
                var parts = gd.parts; if (parts == null) return;
                int hulls = 0, capped = 0, kept = 0; var sample = new List<string>();
                foreach (var kv in parts)
                {
                    var pd = kv.Value; if (pd == null) continue;
                    bool hull = false; try { hull = pd.isHull; } catch { }
                    if (!hull) continue;
                    string type = null; try { type = pd.shipType != null ? pd.shipType.name : null; } catch { }
                    if (type != "bb" && type != "bc") continue;
                    hulls++;
                    var px = pd.paramx; if (px == null) continue;
                    Il2CppSystem.Collections.Generic.List<string> lim = null;
                    bool has = false;
                    try { if (px.ContainsKey("shipgen_limit")) { lim = px["shipgen_limit"]; for (int i = 0; i < lim.Count; i++) if (lim[i] != null && lim[i].Contains("max_main_gun_cal")) has = true; } } catch { }
                    if (has) { kept++; if (sample.Count < 3) sample.Add($"{pd.name}: kept {lim[0]}"); continue; }
                    // 1.11.246: modern hulls carry a nominal maximum of 200 000+ t (the technology limits the real displacement), so the
                    //           basis there is 1.35 x the hull's minimum tonnage (bb_5_china 35 000 t -> 47 000 t)
                    float basis = pd.tonnageMax > 100000f && pd.tonnageMin > 0f ? pd.tonnageMin * 1.35f : pd.tonnageMax;
                    int cap = CapFor(basis);
                    int own = 0; try { own = (int)pd.maxAllowedCaliber; } catch { }
                    bool capMain = cap > 0 && !(own > 0 && cap >= own);   // the hull's own limit may already be tighter or equal
                    if (lim == null) { lim = new Il2CppSystem.Collections.Generic.List<string>(); px["shipgen_limit"] = lim; }
                    if (capMain) lim.Add("max_main_gun_cal:" + cap);
                    // 1.11.237: secondary battery count as the period's ships carried (Iron Duke 12 x 6", Queen Elizabeth 14 x 6",
                    //           Bayern 16 x 5.9", Nagato 20 x 5.5") - the 24.10 session still failed on weight with the main calibre capped
                    // 1.11.246: set on every battleship/battlecruiser hull, also where the main calibre needs no cap (the 25.10 test:
                    //           42 twin 3" mounts and 16 casemates on a 1922 hull, 6 000 t overweight on every attempt)
                    bool hasSec = false; try { for (int i = 0; i < lim.Count; i++) if (lim[i] != null && lim[i].Contains("max_sec_gun_count")) hasSec = true; } catch { }
                    if (!hasSec) lim.Add("max_sec_gun_count:" + SecCountFor(basis));
                    bool hasCnt = false; try { for (int i = 0; i < lim.Count; i++) if (lim[i] != null && lim[i].Contains("max_main_gun_count")) hasCnt = true; } catch { }
                    if (!hasCnt) lim.Add("max_main_gun_count:" + MainCountFor(basis));
                    if (!capMain && hasSec && hasCnt) continue;
                    capped++;
                    if (sample.Count < 8) sample.Add($"{pd.name} ({pd.tonnageMin:0}-{pd.tonnageMax:0} t, hull limit {(own > 0 ? own + "\"" : "none")}): AI max {(capMain ? cap.ToString() : (own > 0 ? own.ToString() : "hull's"))}\", {MainCountFor(basis)} main turrets, secondary mounts {SecCountFor(basis)} per type");
                }
                applied = true;
                MelonLogger.Msg($"[DIP GG Fixes][HullCap] AI main calibre by hull tonnage: {capped} of {hulls} battleship/battlecruiser hulls capped, {kept} had a limit already - {string.Join("; ", sample)}");
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][HullCap] " + e.Message); }
        }
    }

    // 1.11.248: the limits follow the displacement the generator actually chose. Modern hulls carry a nominal maximum of 200 000 t
    //           and the technology sets the real displacement (1936 test: 20" guns in 4 turrets on a 40 000 t Soviet hull); when the
    //           generator has fixed the tonnage (before the parts are placed) the hull's entries are tightened to that tonnage and
    //           put back when the generation ends.
    internal static class DynamicCaps
    {
        class Caps { public int Cap, Cnt, Sec; }
        static readonly Dictionary<string, Caps> original = new Dictionary<string, Caps>();
        static readonly HashSet<IntPtr> active = new HashSet<IntPtr>();
        static int logged;
        static int Read(Il2CppSystem.Collections.Generic.List<string> lim, string key)
        {
            if (lim == null) return 0;
            for (int i = 0; i < lim.Count; i++) { var e = lim[i]; if (e != null && e.StartsWith(key + ":")) { int v; if (int.TryParse(e.Substring(key.Length + 1).Trim(), out v)) return v; } }
            return 0;
        }
        static void Set(Il2CppSystem.Collections.Generic.List<string> lim, string key, int v)
        {
            for (int i = 0; i < lim.Count; i++) { var e = lim[i]; if (e != null && e.StartsWith(key + ":")) { if (v > 0) lim[i] = key + ":" + v; else lim.RemoveAt(i); return; } }
            if (v > 0) lim.Add(key + ":" + v);
        }
        static int Min(int a, int b) { if (a <= 0) return b; if (b <= 0) return a; return Math.Min(a, b); }
        internal static void Begin(Ship ship, IntPtr gen)
        {
            if (HullCaliberCap.On == null || !HullCaliberCap.On.Value || ship == null) return;
            try
            {
                var pd = ship.hull != null ? ship.hull.data : null; if (pd == null || pd.paramx == null) return;
                string type = pd.shipType != null ? pd.shipType.name : null; if (type != "bb" && type != "bc") return;
                float t = ship.Tonnage(); if (t <= 0f) return;
                Il2CppSystem.Collections.Generic.List<string> lim = null;
                if (pd.paramx.ContainsKey("shipgen_limit")) lim = pd.paramx["shipgen_limit"];
                if (lim == null) { lim = new Il2CppSystem.Collections.Generic.List<string>(); pd.paramx["shipgen_limit"] = lim; }
                Caps o; if (!original.TryGetValue(pd.name, out o)) { o = new Caps { Cap = Read(lim, "max_main_gun_cal"), Cnt = Read(lim, "max_main_gun_count"), Sec = Read(lim, "max_sec_gun_count") }; original[pd.name] = o; }
                int cap = Min(o.Cap, HullCaliberCap.CapFor(t * 1.05f)), cnt = Min(o.Cnt, HullCaliberCap.MainCountFor(t)), sec = Min(o.Sec, HullCaliberCap.SecCountFor(t));
                Set(lim, "max_main_gun_cal", cap); Set(lim, "max_main_gun_count", cnt); Set(lim, "max_sec_gun_count", sec);
                active.Add(gen);
                if (logged++ < 10) MelonLogger.Msg($"[DIP GG Fixes][HullCap] {pd.name} at {t:0} t: AI limits for this design {(cap > 0 ? cap + "\"" : "hull's calibre")}, {cnt} main turrets, {sec} secondary mounts per type (hull entries {(o.Cap > 0 ? o.Cap + "\"" : "-")}/{o.Cnt}/{o.Sec})");
            }
            catch (Exception e) { if (logged++ < 10) MelonLogger.Warning("[DIP GG Fixes][HullCap] dynamic: " + e.Message); }
        }
        internal static void End(Ship ship, IntPtr gen)
        {
            if (!active.Remove(gen) || ship == null) return;
            try
            {
                var pd = ship.hull != null ? ship.hull.data : null; if (pd == null || pd.paramx == null || !pd.paramx.ContainsKey("shipgen_limit")) return;
                Caps o; if (!original.TryGetValue(pd.name, out o)) return;
                var lim = pd.paramx["shipgen_limit"];
                Set(lim, "max_main_gun_cal", o.Cap); Set(lim, "max_main_gun_count", o.Cnt); Set(lim, "max_sec_gun_count", o.Sec);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(GameData), "PostProcessAll")]
    internal static class Patch_PostProcessAll_HullCap
    {
        static void Postfix(GameData __instance) { HullCaliberCap.Apply(__instance); }
    }
}
