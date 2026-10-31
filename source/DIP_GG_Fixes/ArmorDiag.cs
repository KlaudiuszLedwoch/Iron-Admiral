// 1.11.43: armour diagnostics (log only, no gameplay change).
// Question to answer before any angle-of-impact armour change: does the game already make penetration depend on the target's
// aspect (horizontal obliquity) and on the angle of fall, and how often do shells hit the deck (TweaksAndFixes 3.21.1 contains
// the deck-hit fix by Crux10086)?
//  1. Aspect probe: once per shooter design / target design pair (max 12 per battle) the game's own Ship.EstimatePenetration is
//     called for the same gun, range and target with the firing position moved around the target: 0 deg = square to the target's
//     side, 30 / 45 / 60 / 75 deg = increasingly oblique, 90 = bow-on. Equal values mean the game ignores aspect for penetration.
//     Ship.GetPenetration is only called, never patched (patching it crashes the game - Nullable parameters).
//  2. Impact angles of real shells: Shell.ImpactForwardToAngleSideUp / SideForward results in 15-degree bands.
//  3. Side (belt) hit chance from Ship.CalcSideHitChance by angle band - the rest are deck hits.
//  4. Hit type texts (Ship.HitTypeText) counted per battle (deck, belt, ricochet, ...).
// Summary at the end of each battle: "[Armor] battle summary ...".
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class ArmorDiag
    {
        internal static MelonPreferences_Entry<bool> On;
        static readonly HashSet<string> probed = new HashSet<string>();
        static readonly int[] sideUp = new int[7], sideFwd = new int[7];
        static readonly float[] sideChanceSum = new float[7]; static readonly int[] sideChanceN = new int[7];
        static readonly Dictionary<string, int> hitTypes = new Dictionary<string, int>();
        static bool inBattle; static int errors;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ArmorDiagnostics", false, null,
                "Log only: tests whether the game's penetration depends on target aspect, records impact angles, side/deck hit chances and hit types per battle ([Armor] lines). No effect on gameplay");
        }
        static bool Active { get { return On != null && On.Value; } }
        internal static void ApplyHooks(HarmonyLib.Harmony h)
        {
            if (!Active) { MelonLogger.Msg("[DIP GG Fixes][Armor] diagnostics off - impact/hit hooks not installed"); return; }
            try
            {
                h.Patch(AccessTools.Method(typeof(Shell), "ImpactForwardToAngleSideUp"), postfix: new HarmonyMethod(typeof(Patch_ArmorDiagSideUp), "Postfix"));
                h.Patch(AccessTools.Method(typeof(Shell), "ImpactForwardToAngleSideForward"), postfix: new HarmonyMethod(typeof(Patch_ArmorDiagSideFwd), "Postfix"));
                h.Patch(AccessTools.Method(typeof(Ship), "CalcSideHitChance"), postfix: new HarmonyMethod(typeof(Patch_ArmorDiagSideChance), "Postfix"));
                // 1.11.215 (author, 24.10): hit types from the damage path itself - Ui.RegisterTakenDamage runs once per hit with the gun,
                //           the hit type, the damage and the armour met. Before (1.11.96-1.11.214) Ship.HitTypeText was counted, which the
                //           interface calls every frame for every hit shown (148,000 "Penetration" in one battle) and the gun was unknown.
                try { h.Patch(AccessTools.Method(typeof(Ui), "RegisterTakenDamage"), postfix: new HarmonyMethod(typeof(Patch_ArmorDiagTakenDamage), "Postfix")); }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Armor] hit types unavailable: " + e.Message); }
                MelonLogger.Msg("[DIP GG Fixes][Armor] diagnostics hooks installed");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Armor] diagnostics hooks failed: " + e.Message); }
        }
        static int Band(float deg) { if (float.IsNaN(deg)) return 6; deg = Math.Abs(deg) % 180f; if (deg > 90f) deg = 180f - deg; return Math.Min(5, (int)(deg / 15f)); }

        internal static void Probe(Ship shooter, Il2CppSystem.Collections.Generic.List<Ship> enemies)
        {
            if (!Active || shooter == null || enemies == null || probed.Count >= 12) return;
            try
            {
                var g = shooter.mainGunsGroup; if (g == null) return;
                Ship best = null; float bestD = float.MaxValue;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (e == null || e.isSinking || e.isDead) continue;
                    float d = Vector3.Distance(shooter.transform.position, e.transform.position);
                    if (d < bestD) { bestD = d; best = e; }
                }
                if (best == null || bestD < 500f) return;
                string key = (shooter.shipType != null ? shooter.shipType.name : "?") + "|" + g.caliber + "|" + (best.design != null ? best.design.Pointer.ToString() : best.name);
                if (!probed.Add(key)) return;
                var tp = best.transform.position; var right = best.transform.right; right.y = 0f; right.Normalize();
                var noShell = new Il2CppSystem.Nullable<Ship.ShellType>();
                var vals = new List<string>();
                float first = float.NaN; bool differs = false;
                foreach (float a in new[] { 0f, 30f, 45f, 60f, 75f, 90f })
                {
                    var from = tp + Quaternion.Euler(0f, a, 0f) * right * bestD;
                    float pen = float.NaN;
                    try
                    {
                        pen = Ship.EstimatePenetration(g, shooter, best, new Il2CppSystem.Nullable<float>(bestD),
                            new Il2CppSystem.Nullable<Vector3>(from), new Il2CppSystem.Nullable<Vector3>(tp), noShell);
                    }
                    catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Armor] EstimatePenetration failed: " + e.Message); }
                    if (float.IsNaN(first)) first = pen; else if (Math.Abs(pen - first) > 0.05f) differs = true;
                    vals.Add(float.IsNaN(pen) ? "?" : pen.ToString("0.0"));
                }
                float plain = float.NaN, deck = float.NaN, withDir = float.NaN;
                try
                {
                    var noVec = new Il2CppSystem.Nullable<Vector3>();
                    plain = Ship.GetPenetration(g, shooter, bestD, true, noShell, noVec);
                    deck = Ship.GetPenetration(g, shooter, bestD, false, noShell, noVec);
                    var dir45 = (tp - (tp + Quaternion.Euler(0f, 45f, 0f) * right * bestD)).normalized;
                    withDir = Ship.GetPenetration(g, shooter, bestD, true, noShell, new Il2CppSystem.Nullable<Vector3>(dir45));
                }
                catch { }
                float belt = 0f, deckArm = 0f;
                try { belt = best.GetEffectiveArmor(Ship.A.Belt, null, g, shooter, false); } catch { }
                try { deckArm = best.GetEffectiveArmor(Ship.A.Deck, null, g, shooter, false); } catch { }
                MelonLogger.Msg($"[DIP GG Fixes][Armor] aspect probe: {shooter.name} ({g.caliber:0} mm) vs {best.name} at {bestD:0} m - EstimatePenetration at aspect 0/30/45/60/75/90 deg: {string.Join(" / ", vals)} mm ({(differs ? "DEPENDS on aspect" : "no aspect effect")}); GetPenetration belt {plain:0.0}, deck {deck:0.0}, belt with 45-deg direction {withDir:0.0} mm; target effective belt {belt:0.0}, deck {deckArm:0.0} mm");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Armor] probe failed: " + e.Message); }
        }

        internal static void RecordSideUp(float deg) { if (Active && inBattle) sideUp[Band(deg)]++; }
        internal static void RecordSideFwd(float deg) { if (Active && inBattle) sideFwd[Band(deg)]++; }
        internal static void RecordSideChance(float angle, float chance) { if (!Active || !inBattle) return; int b = Band(angle); sideChanceSum[b] += chance; sideChanceN[b]++; }
        class HitAgg { public int N; public double Dmg, Armor, Pen; }
        static int layerLogged;
        static readonly Dictionary<string, HitAgg> hitsCal = new Dictionary<string, HitAgg>(), hitsVictim = new Dictionary<string, HitAgg>();
        static string CalBand(float c) { return c >= 240f ? "heavy (>=9.4 in)" : c >= 150f ? "medium (6-9 in)" : c >= 100f ? "light (4-6 in)" : c > 0f ? "small (<4 in)" : "unknown gun"; }
        // 1.11.500 (#116, author 31.10, 11:24: "do you take it from my games?"): the [Layers] lines were written only with ArmorDiagnostics
        // switched on (off by default), so no battle logged them - they are now written from the hit the physical penetration always
        // sees (PhysPen.Taken), at most 60 per battle, whatever the diagnostics switch
        static IntPtr layerBattle;
        internal static void LayersAlways(Ship victim, Part from, string hitType, float damage, float armor, float pen)
        {
            if (Active) return;   // RecordHit writes them
            try
            {
                IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer; } catch { }
                if (b != layerBattle) { layerBattle = b; layerLogged = 0; }
                if (layerLogged >= 60 || victim == null || victim.armor == null) return;
                float V(Ship.A z) { float x; return victim.armor.TryGetValue(z, out x) ? x : 0f; }
                float ib1 = V(Ship.A.InnerBelt_1st), ib2 = V(Ship.A.InnerBelt_2nd), ib3 = V(Ship.A.InnerBelt_3rd), id1 = V(Ship.A.InnerDeck_1st), id2 = V(Ship.A.InnerDeck_2nd), id3 = V(Ship.A.InnerDeck_3rd);
                if (ib1 + ib2 + ib3 + id1 + id2 + id3 <= 0f) return;
                float cal = 0f; try { if (from != null && from.data != null) cal = from.data.caliber; } catch { }
                string t = string.IsNullOrEmpty(hitType) ? "?" : hitType; if (t.Length > 40) t = t.Substring(0, 40);
                layerLogged++;
                MelonLogger.Msg($"[DIP GG Fixes][Layers] {victim.name}: hit '{t}' by {cal:0} mm - armour used by the game {armor:0} mm, penetration {pen:0} mm, damage {damage:0}; plates: belt {V(Ship.A.Belt):0} + inner {ib1:0}/{ib2:0}/{ib3:0} mm, deck {V(Ship.A.Deck):0} + inner {id1:0}/{id2:0}/{id3:0} mm");
            }
            catch { }
        }
        internal static void RecordHit(Ship victim, Part from, string hitType, float damage, float armor, float pen)
        {
            if (!Active || !inBattle) return;
            string t = string.IsNullOrEmpty(hitType) ? "?" : hitType; if (t.Length > 40) t = t.Substring(0, 40);
            int n; hitTypes.TryGetValue(t, out n); hitTypes[t] = n + 1;
            float cal = 0f; try { if (from != null && from.data != null) cal = from.data.caliber; } catch { }
            string vt = "?"; try { vt = victim != null && victim.shipType != null ? victim.shipType.name : "?"; } catch { }
            Add(hitsCal, CalBand(cal) + " | " + t, damage, armor, pen);
            Add(hitsVictim, CalBand(cal) + " vs " + vt + " | " + t, damage, armor, pen);
            // 1.11.492 (#116, author 31.10 08:27: one thick outer plate or thick inner layers?): how the game treats the citadel's inner
            // layers - every hit on a ship with an inner belt or inner deck is logged with the plates and the armour the game used, so the
            // log shows whether the game sums the layers, takes them one by one or ignores them
            try
            {
                if (layerLogged < 60 && victim != null && victim.armor != null)
                {
                    float V(Ship.A z) { float x; return victim.armor.TryGetValue(z, out x) ? x : 0f; }
                    float ib1 = V(Ship.A.InnerBelt_1st), ib2 = V(Ship.A.InnerBelt_2nd), ib3 = V(Ship.A.InnerBelt_3rd), id1 = V(Ship.A.InnerDeck_1st), id2 = V(Ship.A.InnerDeck_2nd), id3 = V(Ship.A.InnerDeck_3rd);
                    if (ib1 + ib2 + ib3 + id1 + id2 + id3 > 0f)
                    {
                        layerLogged++;
                        MelonLogger.Msg($"[DIP GG Fixes][Layers] {victim.name}: hit '{t}' by {cal:0} mm - armour used by the game {armor:0} mm, penetration {pen:0} mm, damage {damage:0}; plates: belt {V(Ship.A.Belt):0} + inner {ib1:0}/{ib2:0}/{ib3:0} mm, deck {V(Ship.A.Deck):0} + inner {id1:0}/{id2:0}/{id3:0} mm");
                    }
                }
            }
            catch { }
        }
        static void Add(Dictionary<string, HitAgg> d, string k, float dmg, float armor, float pen)
        {
            HitAgg a; if (!d.TryGetValue(k, out a)) { a = new HitAgg(); d[k] = a; }
            a.N++; a.Dmg += dmg; a.Armor += armor; a.Pen += pen;
        }
        static string Fmt(Dictionary<string, HitAgg> d) { return d.Count > 0 ? string.Join(", ", d.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value.N} (avg dmg {kv.Value.Dmg / kv.Value.N:0}, armour {kv.Value.Armor / kv.Value.N:0} mm, pen {kv.Value.Pen / kv.Value.N:0} mm)")) : "none recorded"; }

        static string Bands(int[] a) { var s = new List<string>(); for (int i = 0; i < 6; i++) s.Add($"{i * 15}-{i * 15 + 15}: {a[i]}"); if (a[6] > 0) s.Add($"n/a: {a[6]}"); return string.Join(", ", s); }

        internal static void Tick()
        {
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (battle && !inBattle) { inBattle = true; Reset(); }
            else if (!battle && inBattle) { inBattle = false; Summary(); }
        }
        // 1.11.250: also called by AutoTest before it quits (the summary was lost when the game closed in the same frame)
        internal static void Summary()
        {
            {
                if (!Active) return;
                try
                {
                    var sc = new List<string>();
                    for (int i = 0; i < 6; i++) if (sideChanceN[i] > 0) sc.Add($"{i * 15}-{i * 15 + 15} deg: {sideChanceSum[i] / sideChanceN[i]:0.00} ({sideChanceN[i]})");
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - impact angle side-up (deg bands): " + Bands(sideUp));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - impact angle side-forward (deg bands): " + Bands(sideFwd));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - side (belt) hit chance by angle: " + (sc.Count > 0 ? string.Join(", ", sc) : "no calls"));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - hits by type: " + (hitTypes.Count > 0 ? string.Join(", ", hitTypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) : "none recorded"));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - hits by calibre and type (from the damage path): " + Fmt(hitsCal)); // 1.11.215
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - hits by calibre, target type and hit type: " + Fmt(hitsVictim));
                    try { Obliquity.Summary(); } catch { }
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Armor] summary failed: " + e.Message); }
            }
        }
        static void Reset()
        {
            probed.Clear(); hitTypes.Clear(); hitsCal.Clear(); hitsVictim.Clear();
            Array.Clear(sideUp, 0, 7); Array.Clear(sideFwd, 0, 7); Array.Clear(sideChanceSum, 0, 7); Array.Clear(sideChanceN, 0, 7);
        }
    }

    // 1.11.96: installed by ArmorDiag.ApplyHooks only when ArmorDiagnostics is on (they ran on every shell impact and hit)
    internal static class Patch_ArmorDiagSideUp { internal static void Postfix(float __result) { try { ArmorDiag.RecordSideUp(__result); } catch { } } }
    internal static class Patch_ArmorDiagSideFwd { internal static void Postfix(float __result) { try { ArmorDiag.RecordSideFwd(__result); } catch { } } }
    internal static class Patch_ArmorDiagSideChance { internal static void Postfix(float __0, float __result) { try { ArmorDiag.RecordSideChance(__0, __result); } catch { } } }
    // 1.11.215: Ui.RegisterTakenDamage(Ship victim, Part from, string hitType, float damage, float armor, float penetration, bool fire, bool flooding, Part partDamaged, List<Module> modulesDamaged)
    internal static class Patch_ArmorDiagTakenDamage { internal static void Postfix(Ship __0, Part __1, string __2, float __3, float __4, float __5) { try { ArmorDiag.RecordHit(__0, __1, __2, __3, __4, __5); } catch { } } }
}
