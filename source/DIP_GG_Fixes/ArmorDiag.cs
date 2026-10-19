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
            On = cat.CreateEntry("ArmorDiagnostics", true, null,
                "Log only: tests whether the game's penetration depends on target aspect, records impact angles, side/deck hit chances and hit types per battle ([Armor] lines). No effect on gameplay");
        }
        static bool Active { get { return On != null && On.Value; } }
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
        internal static void RecordHitType(string t)
        {
            if (!Active || !inBattle || string.IsNullOrEmpty(t)) return;
            if (t.Length > 40) t = t.Substring(0, 40);
            int n; hitTypes.TryGetValue(t, out n); hitTypes[t] = n + 1;
        }

        static string Bands(int[] a) { var s = new List<string>(); for (int i = 0; i < 6; i++) s.Add($"{i * 15}-{i * 15 + 15}: {a[i]}"); if (a[6] > 0) s.Add($"n/a: {a[6]}"); return string.Join(", ", s); }

        internal static void Tick()
        {
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (battle && !inBattle) { inBattle = true; Reset(); }
            else if (!battle && inBattle)
            {
                inBattle = false;
                if (!Active) return;
                try
                {
                    var sc = new List<string>();
                    for (int i = 0; i < 6; i++) if (sideChanceN[i] > 0) sc.Add($"{i * 15}-{i * 15 + 15} deg: {sideChanceSum[i] / sideChanceN[i]:0.00} ({sideChanceN[i]})");
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - impact angle side-up (deg bands): " + Bands(sideUp));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - impact angle side-forward (deg bands): " + Bands(sideFwd));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - side (belt) hit chance by angle: " + (sc.Count > 0 ? string.Join(", ", sc) : "no calls"));
                    MelonLogger.Msg("[DIP GG Fixes][Armor] battle summary - hit types: " + (hitTypes.Count > 0 ? string.Join(", ", hitTypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) : "none recorded"));
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Armor] summary failed: " + e.Message); }
            }
        }
        static void Reset()
        {
            probed.Clear(); hitTypes.Clear();
            Array.Clear(sideUp, 0, 7); Array.Clear(sideFwd, 0, 7); Array.Clear(sideChanceSum, 0, 7); Array.Clear(sideChanceN, 0, 7);
        }
    }

    [HarmonyPatch(typeof(Shell), "ImpactForwardToAngleSideUp")]
    internal static class Patch_ArmorDiagSideUp { private static void Postfix(float __result) { try { ArmorDiag.RecordSideUp(__result); } catch { } } }

    [HarmonyPatch(typeof(Shell), "ImpactForwardToAngleSideForward")]
    internal static class Patch_ArmorDiagSideFwd { private static void Postfix(float __result) { try { ArmorDiag.RecordSideFwd(__result); } catch { } } }

    [HarmonyPatch(typeof(Ship), "CalcSideHitChance")]
    internal static class Patch_ArmorDiagSideChance { private static void Postfix(float __0, float __result) { try { ArmorDiag.RecordSideChance(__0, __result); } catch { } } }

    [HarmonyPatch(typeof(Ship), "HitTypeText")]
    internal static class Patch_ArmorDiagHitType { private static void Postfix(string __result) { try { ArmorDiag.RecordHitType(__result); } catch { } } }
}
