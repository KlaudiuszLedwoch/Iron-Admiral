// 1.11.251 (#33, author's decision 25.10: Okun's formulas): penetration from the physics of each shot.
// The game's penetration is a table (penetration.csv: calibre x range, scaled by its modifiers); it does not know the shell's real
// striking velocity and angle of fall, and the 25.10 log showed 2x the belt and 1.5x the deck that the trajectory allowed.
// Ship.GetPenetration cannot be patched (Nullable parameters crash Harmony under Il2Cpp), so the physics enters through the armour:
// for every hit the plate the shell meets is scaled by (game penetration / physical penetration) - the game then compares the same
// numbers it always does, and the outcome is that of the physical shell. HE keeps the game's HE/AP ratio on top of the physical base.
//
// Physical penetration (normal-equivalent mm, from the striking velocity Vs and the angle of fall of the shell's physical arc):
//   vertical plates (belt, turret sides, barbettes, conning tower) - face-hardened armour, Okun: T = C * W^0.2 * V^1.21
//       (navweaps.com, "Defeating Face-Hardened Armor" and "History of armor penetration formulae"), with the fall angle as obliquity
//       (V = Vs * cos(fall)) and the AP shell quality of the firing ship (PhysGun.Q). C = 0.049 (T mm, W kg, V m/s) fitted to the
//       US 8"/55 AP Mk 21 (152 kg) against Class A armour (navweaps.com, 1942 curves): 254 mm 9.9 km, 203 mm 14.1 km, 152 mm 19.0 km,
//       127 mm 22.3 km, 102 mm 26.2 km - the five points lie within 5 % of C = 0.049.
//   horizontal plates (decks, turret tops) - homogeneous armour, Thompson F-formula with Okun's standard F
//       (navweaps.com, "History of armor penetration formulae", section IV): T/D = 1728.04 (W/D^3) [(V/F) cos(Ob)]^2,
//       F = 6 (T/D - 0.45)(Ob^2 + 2000) + 40000 (T, D in, W lb, V ft/s, Ob in degrees from the plate normal = 90 - fall).
//       Check: 8"/55 AP Mk 21 at 16.8 km (about 397 m/s, 22 deg fall) gives 2.03 in; navweaps lists 2.0 in.
// The horizontal angle to the belt stays with Obliquity.cs (line of sight through the plate).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PhysPen
    {
        internal static MelonPreferences_Entry<bool> On, HitLog;
        internal static MelonPreferences_Entry<float> FhConst;
        internal static MelonPreferences_Entry<int> HitLogMax;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PhysicalPenetration", true, null, "Battle: penetration from the physics of each shot - striking velocity and angle of fall of the shell's arc; Okun's face-hardened formula for belts and turret sides, Thompson's F-formula with Okun's standard F for decks");
            FhConst = cat.CreateEntry("PhysicalPenetrationFhConst", 0.049f, null, "Okun face-hardened formula T[mm] = C * W[kg]^0.2 * V[m/s]^1.21; C = 0.049 from the US 8\"/55 AP Mk 21 against Class A armour (navweaps.com)");
            HitLog = cat.CreateEntry("HitLog", true, null, "Battle: one log line per shell hit - calibre, weight, striking velocity, angle of fall, armour zone, plate, physical and game penetration, result and damage");
            HitLogMax = cat.CreateEntry("HitLogMax", 300, null, "Battle: at most this many hit lines per battle (a summary per calibre and result follows at the end)");
            PhysGun.CreatePrefs(cat);
        }

        internal class Shot { public Ship Shooter; public PartData Pd; public PhysGun.G G; public float Range, Vs, Fall; }
        static readonly Dictionary<long, Shot> shots = new Dictionary<long, Shot>();
        [ThreadStatic] internal static Shell Current;
        static int errors, hitLines, adjusted, skipped, rawLines, shellHits, noShot, uiLogged;
        static readonly SortedDictionary<string, float[]> summary = new SortedDictionary<string, float[]>();
        struct Fac { public float F, Phys, Game, Vs, Fall, R; public int Frame; }
        static readonly Dictionary<long, Fac> facCache = new Dictionary<long, Fac>();

        internal static void Register(Shell s, Part from, Ship shooter, PhysGun.G g, float range, float vs, float fall)
        {
            if (s == null || !g.Ok) return;
            if (shots.Count > 6000) shots.Clear();
            shots[s.Pointer.ToInt64()] = new Shot { Shooter = shooter, Pd = from != null ? from.data : null, G = g, Range = range, Vs = vs, Fall = fall };
        }
        internal static void BattleStart() { shots.Clear(); facCache.Clear(); hitLines = 0; adjusted = 0; skipped = 0; rawLines = 0; shellHits = 0; noShot = 0; summary.Clear(); PhysGun.ResetCache(); }
        internal static void BattleEnd() { Report("battle end"); }
        internal static void Report(string tag)
        {
            if (summary.Count == 0 && adjusted == 0) return;
            var sb = new System.Text.StringBuilder($"[DIP GG Fixes][Hit] {tag} summary - armour checks with the physical penetration: at firing / AI estimates {adjusted} (left to the game {skipped}), arriving shells {shellHits} (shell not registered {noShot}); hits by calibre and result (count | mean plate the shell met mm | mean physical AP belt penetration mm | mean game penetration mm | mean damage):");
            foreach (var kv in summary) { var v = kv.Value; float n = Math.Max(1f, v[0]); sb.Append($"\n   {kv.Key} | {v[0]:0} | {v[1] / n:0} | {v[2] / n:0} | {v[3] / n:0} | {v[4] / n:0.#}"); }
            MelonLogger.Msg(sb.ToString());
        }

        // ---- the formulas ----
        internal static float BeltPen(float kg, float vs, float fallDeg, float q)
        {
            float c = FhConst != null ? FhConst.Value : 0.049f;
            float vn = vs * Mathf.Cos(Mathf.Clamp(fallDeg, 0f, 80f) * Mathf.Deg2Rad);
            if (vn <= 1f || kg <= 0f) return 0f;
            return c * q * Mathf.Pow(kg, 0.2f) * Mathf.Pow(vn, 1.21f);
        }
        internal static float DeckPen(float kg, float calMm, float vs, float fallDeg)
        {
            if (kg <= 0f || calMm <= 1f || vs <= 1f) return 0f;
            double D = calMm / 25.4, W = kg * 2.20462, V = vs * 3.28084, ob = Math.Min(89.5, Math.Max(0.0, 90.0 - fallDeg)); // a nearly flat shell barely scratches a deck (no artificial floor)
            double c = Math.Cos(ob * Math.PI / 180.0), wd = W / (D * D * D), td = 0.3;
            for (int i = 0; i < 40; i++)
            {
                double F = Math.Max(10000.0, 6.0 * (td - 0.45) * (ob * ob + 2000.0) + 40000.0);
                double x = V * c / F, nt = 1728.04 * wd * x * x;
                if (Math.Abs(nt - td) < 1e-5) { td = nt; break; }
                td = 0.5 * (td + nt);
            }
            return (float)(td * D * 25.4);
        }
        static bool Horizontal(string zone) { return zone.IndexOf("Deck", StringComparison.OrdinalIgnoreCase) >= 0 || zone.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0; }

        // ---- the armour of each hit ----
        internal static void Adjust(Ship target, Ship.A zone, PartData gun, Ship shooter, ref float armor)
        {
            if (On == null || !On.Value || armor <= 0f) return;
            // the arriving shell (Shell.Update): the game asks for the plate without the gun - the shell's own physical flight is used
            if ((gun == null || shooter == null) && Current != null)
            {
                try
                {
                    Shot sh; if (!shots.TryGetValue(Current.Pointer.ToInt64(), out sh) || sh.Pd == null || sh.Shooter == null) { noShot++; return; }
                    bool hz2 = Horizontal(zone.ToString());
                    var gAp = sh.G.Type == Ship.ShellType.Ap ? sh.G : PhysGun.Get(sh.Pd, sh.Shooter, Ship.ShellType.Ap);
                    if (!gAp.Ok) return;
                    float phys2 = hz2 ? DeckPen(gAp.Kg, gAp.CalMm, sh.Vs, sh.Fall) : BeltPen(gAp.Kg, sh.Vs, sh.Fall, gAp.Q);
                    float game2 = 0f;
                    try { game2 = Ship.GetPenetration(sh.Pd, sh.Shooter, sh.Range, !hz2, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>()); } catch { }
                    if (game2 <= 0.01f) return;
                    armor *= Mathf.Clamp(phys2 > 0.01f ? game2 / phys2 : 50f, 0.05f, 50f); shellHits++;
                }
                catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] adjust (shell): " + e.Message); }
                return;
            }
            if (shooter == null) return;
            // The game decides a hit's outcome when the gun fires (Ship.HitPrecalc, before the shell exists) and calls this with the gun
            // (partData) and the firing ship; the AI's penetration estimates call it the same way. The shot is rebuilt from those
            // arguments: the physical gun, the distance between the ships and the striking velocity and fall at that range.
            if (gun == null || target == null) return;
            try
            {
                string zn = zone.ToString(); bool hz = Horizontal(zn);
                Vector3 dv = target.transform.position - shooter.transform.position; dv.y = 0f; float R = dv.magnitude;
                if (R < 50f) { skipped++; return; }
                long key = (gun.Pointer.ToInt64() * 31) ^ (shooter.Pointer.ToInt64() << 3) ^ ((long)Mathf.RoundToInt(R / 100f) << 1) ^ (hz ? 1L : 0L);
                Fac fc;
                if (!facCache.TryGetValue(key, out fc) || Time.frameCount - fc.Frame > 600)
                {
                    var g = PhysGun.Get(gun, shooter, Ship.ShellType.Ap);
                    if (!g.Ok) { skipped++; return; }
                    float vs, fall, t; bool beyond;
                    Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, Ballistics.CdOf(shooter), R, MountElevation.Elev(Math.Max(1, g.Grade)), out vs, out fall, out t, out beyond);
                    float phys = hz ? DeckPen(g.Kg, g.CalMm, vs, fall) : BeltPen(g.Kg, vs, fall, g.Q);
                    float game = 0f;
                    try { game = Ship.GetPenetration(gun, shooter, R, !hz, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>()); } catch { }
                    fc = new Fac { Phys = phys, Game = game, Vs = vs, Fall = fall, R = R, Frame = Time.frameCount, F = game > 0.01f ? Mathf.Clamp(phys > 0.01f ? game / phys : 50f, 0.05f, 50f) : 1f };
                    if (facCache.Count > 20000) facCache.Clear();
                    facCache[key] = fc;
                }
                if (fc.Game <= 0.01f) { skipped++; return; }
                armor *= fc.F; adjusted++;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] adjust: " + e.Message); }
        }

        // Ui.RegisterTakenDamage(Ship victim, Part from, string hitType, float damage, float armor, float penetration, ...)
        internal static void Taken(Ship victim, Part from, string hitType, float damage, float armor, float penetration)
        {
            try
            {
                // runs inside the arriving shell's Update: its own physical flight (registered at Shell.Create) is known here
                Shot sh = null; var s = Current; if (s != null) shots.TryGetValue(s.Pointer.ToInt64(), out sh);
                float cal = sh != null ? sh.G.CalMm : (from != null && from.data != null ? from.data.caliber : 0f);
                float physB = 0f, physD = 0f;
                if (sh != null) { physB = BeltPen(sh.G.Kg, sh.Vs, sh.Fall, sh.G.Q); physD = DeckPen(sh.G.Kg, sh.G.CalMm, sh.Vs, sh.Fall); }
                string key = $"{cal,6:0.0} mm {(sh != null ? sh.G.Type.ToString() : "?"),-2} | {hitType}";
                float[] a; if (!summary.TryGetValue(key, out a)) { a = new float[5]; summary[key] = a; }
                a[0]++; a[1] += Math.Max(0f, armor); a[2] += physB; a[3] += Math.Max(0f, penetration); a[4] += damage;
                if (HitLog == null || !HitLog.Value || hitLines >= (HitLogMax != null ? HitLogMax.Value : 300)) return;
                if (hitType == "none" && rawLines++ >= 5) return; // the game's "none" (no armour met, no damage): only the first few
                hitLines++;
                if (sh != null)
                    MelonLogger.Msg($"[DIP GG Fixes][Hit] {sh.G.CalMm:0.#} mm {sh.G.Type} {sh.G.Kg:0.#} kg ({sh.G.Mv:0} m/s) from {(sh.Shooter != null ? sh.Shooter.name : "?")} -> {(victim != null ? victim.name : "?")}: {sh.Range:0} m, strike {sh.Vs:0} m/s, fall {sh.Fall:0.0} deg; physical AP penetration belt {physB:0.#} mm / deck {physD:0.#} mm; game: plate met {armor:0.#} mm (after quality, obliquity and the physical scaling), penetration {penetration:0.#} mm; result {hitType}, damage {damage:0.#}");
                else
                    MelonLogger.Msg($"[DIP GG Fixes][Hit] {cal:0.#} mm (no physical shell) -> {(victim != null ? victim.name : "?")}: plate met {armor:0.#} mm, penetration {penetration:0.#} mm; result {hitType}, damage {damage:0.#}");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] damage path: " + e.Message); }
        }

        // ---- gun window: the values the battle uses ----
        static string StripTags(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length); bool tag = false;
            foreach (char c in s) { if (c == '<') { tag = true; continue; } if (c == '>' && tag) { tag = false; continue; } if (!tag) sb.Append(c); }
            return sb.ToString();
        }
        // numbers directly followed by "mm"
        static List<float> MmNumbers(string s)
        {
            var r = new List<float>();
            for (int i = 0; i + 1 < s.Length; i++)
            {
                if (s[i] != 'm' || s[i + 1] != 'm') continue;
                int j = i - 1; while (j >= 0 && s[j] == ' ') j--;
                int e = j; while (j >= 0 && (char.IsDigit(s[j]) || s[j] == '.')) j--;
                if (e > j) { float v; if (float.TryParse(s.Substring(j + 1, e - j), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) r.Add(v); }
                i++;
            }
            return r;
        }
        // leading range "15,000m - ..." -> 15000
        static bool RowRange(string plain, out float R)
        {
            R = 0f; if (plain.Length == 0 || !char.IsDigit(plain[0])) return false;
            int i = 0; var sb = new System.Text.StringBuilder();
            while (i < plain.Length && (char.IsDigit(plain[i]) || plain[i] == ',' || plain[i] == '.')) { if (plain[i] != ',') sb.Append(plain[i]); i++; }
            while (i < plain.Length && plain[i] == ' ') i++;
            if (i >= plain.Length || plain[i] != 'm' || (i + 1 < plain.Length && plain[i + 1] == 'm')) return false;
            return float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out R) && R > 0f;
        }
        internal static string RewriteInfo(string text, PartData pd, Ship ship)
        {
            if (On == null || !On.Value || string.IsNullOrEmpty(text) || pd == null || ship == null) return text;
            if (text.IndexOf("Penetration", StringComparison.OrdinalIgnoreCase) < 0) return text;
            try
            {
                var ap = PhysGun.Get(pd, ship, Ship.ShellType.Ap, false); if (!ap.Ok) return text;
                var he = PhysGun.Get(pd, ship, Ship.ShellType.He, false);
                int grade = Math.Max(1, ap.Grade); float cd = Ballistics.CdOf(ship), elev = MountElevation.Elev(grade);
                var lines = text.Split('\n'); bool inTable = false; bool changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string plain = StripTags(lines[i]).Trim();
                    if (plain.StartsWith("Muzzle Velocity AP", StringComparison.OrdinalIgnoreCase)) { lines[i] = $"Muzzle Velocity AP: {ap.Mv:0} m/s (game {ap.MvGame:0} m/s; same charge, {ap.Kg:0} kg shell)"; changed = true; continue; }
                    if (plain.StartsWith("Muzzle Velocity HE", StringComparison.OrdinalIgnoreCase) && he.Ok) { lines[i] = $"Muzzle Velocity HE: {he.Mv:0} m/s (game {he.MvGame:0} m/s; same charge, {he.Kg:0} kg shell)"; changed = true; continue; }
                    if (plain.IndexOf("Penetration Belt/Deck", StringComparison.OrdinalIgnoreCase) >= 0) { inTable = true; lines[i] = lines[i] + " (physics: Okun)"; continue; }
                    if (!inTable) continue;
                    float R;
                    if (!RowRange(plain, out R)) { if (plain.Length > 0 && !char.IsDigit(plain[0])) inTable = false; continue; }
                    var nums = MmNumbers(plain); if (nums.Count < 4) continue;
                    float heB = nums[0], apB = nums[1], heD = nums[2], apD = nums[3];
                    float vs, fall, t; bool beyond;
                    Ballistics.Impact(ap.Mv, ap.CalMm / 25.4f, ap.Kg, cd, R, elev, out vs, out fall, out t, out beyond);
                    if (beyond) { lines[i] = $"{R:#,0}m - beyond the reach of the mount ({elev:0.#} deg)"; changed = true; continue; }
                    float pB = BeltPen(ap.Kg, vs, fall, ap.Q), pD = DeckPen(ap.Kg, ap.CalMm, vs, fall);
                    float hB = apB > 0.01f ? pB * heB / apB : 0f, hD = apD > 0.01f ? pD * heD / apD : 0f;
                    int comma = plain.LastIndexOf("mm,"); string acc = comma >= 0 ? plain.Substring(comma + 3).Trim() : "";
                    lines[i] = $"{R:#,0}m - {hB:0.#}mm - {pB:0.#}mm / {hD:0.#}mm - {pD:0.#}mm, {acc} [{vs:0} m/s, {fall:0.#} deg]";
                    changed = true;
                }
                string res = changed ? string.Join("\n", lines) : text;
                if (changed && uiLogged++ < 2) MelonLogger.Msg("[DIP GG Fixes][Hit] gun window with the physical values (" + (ship != null ? ship.name : "?") + "):\n" + StripTags(res));
                return res;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] gun window: " + e.Message); return text; }
        }
    }

    [HarmonyPatch(typeof(Shell), "Update")]
    internal static class Patch_ShellUpdate_PhysPen
    {
        static void Prefix(Shell __instance) { PhysPen.Current = __instance; }
        static void Postfix() { PhysPen.Current = null; }
    }

    [HarmonyPatch(typeof(Ui), "RegisterTakenDamage")]
    internal static class Patch_TakenDamage_PhysPen
    {
        static void Postfix(Ship __0, Part __1, string __2, float __3, float __4, float __5) { PhysPen.Taken(__0, __1, __2, __3, __4, __5); }
    }

    [HarmonyPatch(typeof(Ui), "GetPartInfoText")]
    internal static class Patch_PartInfo_PhysPen
    {
        static void Postfix(PartData __0, Ship __1, ref string __result) { __result = PhysPen.RewriteInfo(__result, __0, __1); }
    }
}
