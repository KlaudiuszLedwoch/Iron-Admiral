// 1.11.253 (#33, author's decision 25.10, 10:08): damage of an AP shell that passes clean through (the game's hit type "over").
// 1.11.257 (author's order 25.10, 13:06 and 13:20, P0): the pass-through damage was still far too small for heavy shells (9.4 in and
// more: 0.7-6 % of a penetration in the day's logs) because 1.11.253 only raised the game's armour modifier, while the damage of a hit
// carries more than that modifier, and the "over" hits mostly struck other parts of the ship than the penetrations they were
// compared with. Now:
//   - every hit that reaches the damage path (Ship.DealDamageToPartSectionAndSpread) is logged with the part and section it struck
//     (upper works, turret / barbette, hull ends, citadel, machinery or magazine section; at / below the waterline) and summed per
//     location and result at the end of the battle;
//   - an AP pass-through gets a fraction of the damage the same shell would deal as a normal penetration in the same place: the
//     damage the game computed is divided by the game's "over" modifier and multiplied by its "normal" modifier and by the fraction
//     of that location.
// Fractions by location, from the US Navy war damage reports (Boise CL-47, South Dakota BB-57, Sterett DD-407, Salt Lake City CA-25,
// destroyer report 2-19/2-20, see claude/fizyka_balistyka_33.md): of 14 pass-through hits none had a serious effect, of about 24
// hits that burst inside about 40 % did; 0 of 14 gives at most about 21 % (rule of three, 95 %). Superstructure pass-throughs holed
// directors, radars, ducts and bulkheads (South Dakota 6, 8, 10, 13-15, 19, 22, 23; San Francisco 20); hull pass-throughs at or below
// the waterline flooded compartments (Boise 3, Salt Lake City 1, Heermann, Kalinin Bay); a shell that stopped in machinery cut main
// steam and the fire main (Albert W. Grant 4). The fractions (own inference from these cases): upper works 0.05, turret or barbette
// 0.10, hull ends 0.15, citadel 0.20, machinery or magazine section 0.20; +0.05 at or below the waterline (flooding), at most 0.25.
// HE shells are left to the game. Switches: OverPenDamage, OverPenBySection (F10, Battle).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class OverPen
    {
        internal static MelonPreferences_Entry<bool> On, BySection;
        internal static MelonPreferences_Entry<float> Frac;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("OverPenDamage", true, null, "Battle: an AP shell passing clean through a ship does a fraction of the damage of a shell bursting inside (holes, splinters, cut cables and pipes), instead of almost nothing");
            Frac = cat.CreateEntry("OverPenDamageFraction", 0.15f, null, "Battle: damage of an AP pass-through as a fraction of a normal penetration when the location is not known (from the US Navy war damage reports)");
            BySection = cat.CreateEntry("OverPenBySection", true, null, "Battle: the pass-through damage follows the place the shell went through (upper works 5 %, turret 10 %, hull ends 15 %, citadel and machinery 20 %, +5 % at the waterline) of the damage the same shell would do as a penetration there");
        }
        // the hit being resolved: GetArmorModForPenetrations runs right before DealDamageToPartSectionAndSpread for the same hit
        static bool busy, pending; static int pendFrame = -1; static string curType; static bool curHe; static float curMod, curNormal = 1f;
        static int raised, logged, errors, locLogs;
        internal static int Raised { get { return raised; } }
        static readonly SortedDictionary<string, float[]> locSum = new SortedDictionary<string, float[]>();
        // last location for the hit log (PhysPen.Taken runs right after the damage)
        internal static string LastLoc, LastModules; internal static int LastFrame = -1; internal static float LastRaw, LastNew, LastMod; internal static bool LastFire, LastFlood; internal static string LastType;
        // 1.11.276 (#33 step 3b, author 25.10, 18:14): what a non-penetrating hit does today - per shell type, calibre band and location:
        // hits | damage | fires | floodings | hits with modules damaged
        static readonly SortedDictionary<string, float[]> nonPen = new SortedDictionary<string, float[]>();
        internal static void NonPen(string key, float damage, bool fire, bool flood, bool modules)
        {
            float[] a; if (!nonPen.TryGetValue(key, out a)) { a = new float[5]; nonPen[key] = a; }
            a[0]++; a[1] += damage; if (fire) a[2]++; if (flood) a[3]++; if (modules) a[4]++;
        }
        internal static string Extra()
        {
            if (LastFrame != Time.frameCount) return "";
            return $"; game modifier {LastMod:0.###} ({LastType ?? "?"}){(LastFire ? "; fire" : "")}{(LastFlood ? "; flooding" : "")}{(LastModules != null ? "; modules " + LastModules : "")}";
        }
        internal static void ResetCount() { raised = 0; locSum.Clear(); locLogs = 0; nonPen.Clear(); }

        // Ship.GetArmorModForPenetrations(string hitType, bool useHe, float angle): the hit type and the game's modifiers
        internal static void ArmorMod(Ship ship, string hitType, bool useHe, float angle, ref float result)
        {
            if (busy || On == null || !On.Value || ship == null) return;
            busy = true;
            try
            {
                pending = true; pendFrame = Time.frameCount; curType = hitType; curHe = useHe; curMod = result; curNormal = 1f;
                if (hitType != "over" || useHe) return;
                float normal = 1f;
                try { normal = ship.GetArmorModForPenetrations("normal", false, angle); } catch { }
                if (!(normal > 0f)) normal = 1f;
                curNormal = normal;
                if (BySection != null && BySection.Value) return;   // the damage path scales the damage itself
                float want = normal * Math.Max(0f, Math.Min(1f, Frac != null ? Frac.Value : 0.15f));
                if (result < want)
                {
                    if (logged++ < 3) MelonLogger.Msg($"[DIP GG Fixes][Damage] AP pass-through: game damage modifier {result:0.###} raised to {want:0.###} ({(Frac != null ? Frac.Value : 0.15f):0.##} of a normal penetration {normal:0.###})");
                    result = want; raised++;
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] pass-through: " + e.Message); }
            finally { busy = false; }
        }

        // location of a hit: part and hull section
        static string Location(Ship ship, Part part, Ship.Section sec, out float frac)
        {
            frac = Frac != null ? Frac.Value : 0.15f;
            string where = "?"; bool wet = false;
            try
            {
                var pd = part != null ? part.data : null;
                bool hull = pd == null || pd.isHull || (ship.hull != null && part != null && part.Pointer == ship.hull.Pointer);
                if (!hull)
                {
                    if (pd.isGun || pd.isBarbette) { where = "turret/barbette"; frac = 0.10f; }
                    else { where = "upper works"; frac = 0.05f; }
                }
                else if (sec != null)
                {
                    string mod = (sec.module ?? "").ToLowerInvariant();
                    var zone = sec.armor;
                    wet = sec.underwater > 0.01f;
                    if (mod.Contains("engine") || mod.Contains("boiler") || mod.Contains("machin") || mod.Contains("ammo") || mod.Contains("magazine")) { where = "machinery/magazine (" + sec.module + ")"; frac = 0.20f; }
                    else if (zone == Ship.A.BeltBow || zone == Ship.A.BeltStern || zone == Ship.A.DeckBow || zone == Ship.A.DeckStern) { where = "hull ends"; frac = 0.15f; }
                    else { where = "citadel"; frac = 0.20f; }
                }
                else { where = "hull"; frac = 0.15f; }
            }
            catch { }
            if (wet) { where += ", waterline"; frac = Math.Min(0.25f, frac + 0.05f); }
            return where;
        }

        // Ship.DealDamageToPartSectionAndSpread(Part part, Section section, float damage, Vector3 impact, ...)
        internal static void Deal(Ship ship, Part part, Ship.Section sec, ref float damage, bool useHe)
        {
            if (On == null || !On.Value || ship == null) return;
            try
            {
                float frac; string loc = Location(ship, part, sec, out frac);
                bool known = pending && pendFrame == Time.frameCount; pending = false;
                string type = known ? (curType ?? "?") : "?";
                float raw = damage;
                if (known && type == "over" && !useHe && !curHe && BySection != null && BySection.Value && curMod > 1e-5f)
                {
                    float want = raw / curMod * curNormal * frac;
                    if (want > damage) { damage = want; raised++; }
                }
                LastLoc = loc; LastFrame = Time.frameCount; LastRaw = raw; LastNew = damage; LastFire = false; LastFlood = false; LastModules = null; LastMod = known ? curMod : 0f; LastType = type;
                string key = $"{loc} | {(useHe ? "He" : "Ap")} {type}";
                float[] a; if (!locSum.TryGetValue(key, out a)) { a = new float[3]; locSum[key] = a; }
                a[0]++; a[1] += raw; a[2] += damage;
                if (type == "over" && locLogs++ < 40) MelonLogger.Msg($"[DIP GG Fixes][Damage] AP pass-through {ship.name}: {loc}, game damage {raw:0.#} (modifier {curMod:0.###}), as a penetration {raw / Math.Max(1e-5f, curMod) * curNormal:0} -> {damage:0.#} ({frac:0.00})");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] location: " + e.Message); }
        }

        // after the damage: fire, flooding and modules damaged by this hit (for the hit dispatches, 1.11.258)
        internal static void After(bool fire, bool flood, Il2CppSystem.Collections.Generic.List<Ship.Module> mods)
        {
            if (LastFrame != Time.frameCount) return;
            LastFire |= fire; LastFlood |= flood;
            try { if (mods != null && mods.Count > 0) { var l = new List<string>(); for (int i = 0; i < mods.Count && l.Count < 3; i++) { var m = mods[i]; if (m == null) continue; string nm = m.nameUi; if (string.IsNullOrEmpty(nm)) nm = m.name; if (!string.IsNullOrEmpty(nm) && !l.Contains(nm.ToLowerInvariant())) l.Add(nm.ToLowerInvariant()); } if (l.Count > 0) LastModules = string.Join(", ", l); } } catch { }
        }

        internal static void Report()
        {
            if (locSum.Count == 0) return;
            var sb = new System.Text.StringBuilder("[DIP GG Fixes][Damage] battle summary - damage by location and result (hits | mean game damage | mean damage dealt):");
            foreach (var kv in locSum) { var v = kv.Value; float n = Math.Max(1f, v[0]); sb.Append($"\n   {kv.Key} | {v[0]:0} | {v[1] / n:0.#} | {v[2] / n:0.#}"); }
            MelonLogger.Msg(sb.ToString());
            if (nonPen.Count > 0)
            {
                var nb = new System.Text.StringBuilder("[DIP GG Fixes][Damage] battle summary - hits without penetration (shell type, calibre band, location, result | hits | mean damage | fires | floodings | hits damaging modules):");
                foreach (var kv in nonPen) { var v = kv.Value; float n = Math.Max(1f, v[0]); nb.Append($"\n   {kv.Key} | {v[0]:0} | {v[1] / n:0.#} | {v[2]:0} | {v[3]:0} | {v[4]:0}"); }
                MelonLogger.Msg(nb.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(Ship), "GetArmorModForPenetrations")]
    internal static class Patch_OverPenArmorMod
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Ship __instance, string __0, bool __1, float __2, ref float __result) { try { OverPen.ArmorMod(__instance, __0, __1, __2, ref __result); } catch { } }
    }
    [HarmonyPatch(typeof(Ship), "DealDamageToPartSectionAndSpread")]
    internal static class Patch_OverPenDeal
    {
        private static void Prefix(Ship __instance, Part __0, Ship.Section __1, ref float __2, bool __11) { try { OverPen.Deal(__instance, __0, __1, ref __2, __11); } catch { } }
        private static void Postfix(ref bool __5, ref bool __7, Il2CppSystem.Collections.Generic.List<Ship.Module> __9) { try { OverPen.After(__5, __7, __9); } catch { } }
    }
}
