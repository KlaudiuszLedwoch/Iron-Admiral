// 1.11.77: armour obliquity. The 1.11.43 diagnostics showed that the game's penetration does not depend on the target's
// aspect (EstimatePenetration and GetPenetration identical at 0-90 deg, also with an oblique direction), while real hits
// arrive at every horizontal angle (side-forward bands 0-90 deg roughly even). Only the ricochet chance used the angle.
// Here the belt armour a shell meets is thickened by the line-of-sight factor of the horizontal obliquity:
//   effective = plate / cos(obliquity)                               (capped shells, up to 45 deg)
//   beyond 45 deg the cap is knocked aside (Okun, navweaps Type 91 note 2) - the excess angle counts like an uncapped shell
//   uncapped shells (before CappedShellYear): effective = plate / cos(obliquity)^UncappedExponent
// capped at ObliquityMaxFactor. Decks keep the game's own deck penetration (already a function of the fall angle).
// The angle is taken from the game's own impact computation (Shell.ImpactForwardToAngleSideForward, same frame): the angle
// between its two arguments (shell direction and the side normal) in the horizontal plane (1.11.79). Per battle summary: "[Armor] obliquity ...".
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Obliquity
    {
        internal static MelonPreferences_Entry<bool> On, OnTop;
        // 1.11.312 (#58/#60): the game slopes the plate itself in the hit (Ship.TakeHitRaw: "armor mm sloped" = effective armour /
        // (cos(side-forward angle) x cos(side-up angle)), angles from Shell.ImpactForwardToAngleSideForward / SideUp, compared with
        // the randomised penetration; only EstimatePenetration ignores the angle, which 1.11.43-77 had measured). The factor below came
        // on top of the game's own slope: a 152 mm hit with 63 mm penetration was stopped by a "30 mm" plate, small shells met
        // 463-690 mm. In hits it is now off; the angles and the game's sloped plate are written on the hit line instead.
        // 1.11.320 (#58/#59, test 27.10 16:26, AP hits only): the game's side-up angle of an arriving shell is about 90 - fall (75-89 deg
        // for shells falling at 5-15 deg), and the game divides the plate by cos(min(side-up, 85)) - a flat shell meeting a vertical belt
        // nearly square had its plate thickened 4-11 times (152 mm AP, 110 mm penetration, stopped by a 39 mm end belt at 14 deg
        // side-forward); the formula plate / (cos(min(up,85)) x cos(min(fwd,85))) predicted all 77 stopped AP hits. The same angle drives
        // the side-or-deck roll (Ship.CalcSideHitChance: 78 % side below 15 deg, 7 % above 75), so flat shells were mostly made deck hits.
        // Now the side-up angle of an arriving shell is its angle of fall from the shell's own physical flight: a flat shell meets a side
        // plate almost square and mostly strikes the side, a plunging one the deck. Switch: ImpactAngleFromFall (F10, Battle).
        internal static MelonPreferences_Entry<bool> FromFall; internal static int FallFixed;
        // 1.11.321: the angle is taken from the shell's own impact direction (first argument). The side-up angle is computed at the
        // firing (precalc, before the shell exists) as well as at the impact, so the physical shell used by 1.11.320 was never at hand
        // (test 27.10 17:32: applied 0 times).
        internal static void FixSideUp(Vector3 fwd, ref float r)
        {
            if (FromFall == null || !FromFall.Value) return;
            float h = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z); if (h < 1e-4f && Mathf.Abs(fwd.y) < 1e-4f) return;
            float fall = Mathf.Atan2(Mathf.Abs(fwd.y), h) * Mathf.Rad2Deg;
            r = Mathf.Clamp(fall, 0f, 89f); FallFixed++;
        }
        internal static float LastUp = float.NaN, LastFwd = float.NaN; internal static int LastUpFrame = -1, LastFwdFrame = -1; internal static float LastF = 1f; internal static int LastFFrame = -1;
        internal static MelonPreferences_Entry<float> MaxFactor, UncappedExp;
        internal static MelonPreferences_Entry<int> CappedYear;
        static Vector3 capA, capB; static int capFrame = -1;
        static readonly HashSet<string> zoneNames = new HashSet<string>(); static int calib;
        static readonly int[] bands = new int[7]; static double sumFactor; static int nFactor;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ArmorObliquity", true, null, "Belt armour counts thicker when a shell strikes it at a horizontal angle (line of sight through the plate); the cap stops helping above 45 degrees; uncapped shells suffer more");
            MaxFactor = cat.CreateEntry("ObliquityMaxFactor", 3f, null, "Upper limit of the obliquity thickness factor (3 = about 70 degrees; steeper hits are left to the ricochet rules)");
            FromFall = cat.CreateEntry("ImpactAngleFromFall", true, null, "Battle: the side-up impact angle the game uses for the side-or-deck roll and for sloping the plate is the shell's angle of fall (the game's own value was about 90 - fall, so flat shells were made deck hits or met side plates thickened 4-11 times) (1.11.320)");
            OnTop = cat.CreateEntry("ObliquityOnTopOfGame", false, null, "Battle: apply the mod's belt obliquity factor in hits on top of the game's own sloping of the plate (off since 1.11.312 - the game already divides the plate by the cosines of the impact angles)");
            UncappedExp = cat.CreateEntry("ObliquityUncappedExponent", 1.3f, null, "Exponent of 1/cos(angle) for uncapped shells (capped shells use 1 up to 45 degrees)");
            CappedYear = cat.CreateEntry("CappedShellYear", 1905, null, "AP shells count as capped when the historical year of the firing ship's AP shell technology (its AP shell component) is this or later - never the calendar year (1.11.229)");
        }

        internal static float capRes = float.NaN;
        // 1.11.89: the AI's own penetration estimates must not pick up the angle of a shell impact handled in the same frame
        [ThreadStatic] internal static bool Suspend;
        internal static void Capture(Vector3 a, Vector3 b) { capA = a; capB = b; capFrame = Time.frameCount; capRes = float.NaN; }

        static int Year()
        {
            try { if (CampaignController.Instance != null && GameManager.IsCampaign) return CampaignController.Instance.CurrentDate.AsDate().Year; } catch { }
            return 1920;
        }

        // 1.11.229 (author's rule: mechanics by technology, never by the calendar): capped shells are decided by the firing ship's AP shell
        //          technology - the historical year of its AP shell component (ap_* in components.csv: 1890 standard, 1895/1898, 1905 capped
        //          types, 1915, 1925 SAPC). Cached per ship for the battle.
        static readonly Dictionary<long, int> apYear = new Dictionary<long, int>();
        internal static void ResetCache() { apYear.Clear(); }
        static int ApShellYear(Ship shooter)
        {
            if (shooter == null) return 0;
            long k = shooter.Pointer.ToInt64(); int y; if (apYear.TryGetValue(k, out y)) return y;
            y = 0;
            try { var comps = shooter.components; if (comps != null) foreach (var kv in comps) { var cd = kv.Value; if (cd == null || cd.type != "shell_ap") continue; int ty = 0; try { var tt = cd.tech; if (tt != null) ty = tt.year; } catch { } if (ty > y) y = ty; } } catch { }
            if (apYear.Count > 2000) apYear.Clear();
            apYear[k] = y; return y;
        }
        static Ship shooterNow;
        internal static float Factor(Ship target, Ship.A zone, Ship shooter)
        {
            shooterNow = shooter;
            return Factor(target, zone);
        }
        internal static float Factor(Ship target, Ship.A zone)
        {
            if (Suspend || On == null || !On.Value || target == null || capFrame != Time.frameCount) return 1f;
            string zn = zone.ToString();
            if (zoneNames.Add(zn)) MelonLogger.Msg($"[DIP GG Fixes][Armor] obliquity: armour zone seen in hits: {zn}");
            if (zn.IndexOf("Belt", StringComparison.OrdinalIgnoreCase) < 0) return 1f;
            // Calibration in battle (21.10): the game calls ImpactForwardToAngleSideForward(direction, side normal) with both
            // vectors in the same frame - world (target.right as the 2nd argument) or the target's local frame ((1,0,0)).
            // The obliquity is therefore the angle between the two arguments in the horizontal plane.
            Vector3 dir = capA; dir.y = 0f;
            Vector3 nrm = capB; nrm.y = 0f;
            if (dir.sqrMagnitude < 1e-6f || nrm.sqrMagnitude < 1e-6f) return 1f;
            dir.Normalize(); nrm.Normalize();
            if (calib++ < 5) MelonLogger.Msg($"[DIP GG Fixes][Armor] obliquity calibration: direction {capA}, side normal {capB}, angle {Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(dir, nrm)))) * Mathf.Rad2Deg:0} deg, game's own result {capRes:0.0}");
            float cos = Mathf.Abs(Vector3.Dot(dir, nrm));                 // 1 = square to the belt
            float ang = Mathf.Acos(Mathf.Clamp01(cos)) * Mathf.Rad2Deg;
            int ay = ApShellYear(shooterNow); bool capped = (ay > 0 ? ay : Year()) >= CappedYear.Value; // 1.11.229: by the shell technology; the year only when no AP component is found
            float f;
            if (capped)
            {
                float c45 = Mathf.Cos(45f * Mathf.Deg2Rad);
                f = ang <= 45f ? 1f / Mathf.Max(cos, 0.05f) : (1f / c45) * Mathf.Pow(c45 / Mathf.Max(cos, 0.05f), UncappedExp.Value);
            }
            else f = Mathf.Pow(1f / Mathf.Max(cos, 0.05f), UncappedExp.Value);
            f = Mathf.Clamp(f, 1f, Mathf.Max(1f, MaxFactor.Value));
            int b = Math.Min(5, (int)(ang / 15f)); bands[b]++; sumFactor += f; nFactor++;
            LastF = f; LastFFrame = Time.frameCount;
            if (OnTop == null || !OnTop.Value) return 1f; // 1.11.312: the game slopes the plate itself
            return f;
        }
        static bool IsAxis(Vector3 v, Vector3 fwd, Vector3 right)
        {
            var n = v.normalized;
            return Mathf.Abs(Vector3.Dot(n, fwd)) > 0.9995f || Mathf.Abs(Vector3.Dot(n, right)) > 0.9995f;
        }

        internal static void Summary()
        {
            if (nFactor == 0) return;
            MelonLogger.Msg($"[DIP GG Fixes][Armor] obliquity - belt hits by horizontal angle 0-15/15-30/30-45/45-60/60-75/75-90: {bands[0]}/{bands[1]}/{bands[2]}/{bands[3]}/{bands[4]}/{bands[5]}, average belt thickness factor x{sumFactor / nFactor:0.00} ({nFactor} computations){((OnTop == null || !OnTop.Value) ? " - not applied: the game slopes the plate itself (1.11.312)" : "")}");
            Array.Clear(bands, 0, bands.Length); sumFactor = 0; nFactor = 0;
        }
    }

    [HarmonyPatch(typeof(Shell), "ImpactForwardToAngleSideForward")]
    internal static class Patch_ObliquityCapture
    {
        private static void Prefix(Vector3 __0, Vector3 __1) { try { Obliquity.Capture(__0, __1); } catch { } }
        private static void Postfix(float __result) { Obliquity.capRes = __result; Obliquity.LastFwd = __result; Obliquity.LastFwdFrame = Time.frameCount; }
    }

    [HarmonyPatch(typeof(Ship), "GetEffectiveArmor")]
    internal static class Patch_ObliquityArmor
    {
        private static void Postfix(Ship __instance, Ship.A __0, PartData __2, Ship __3, ref float __result)
        {
            long t0 = Perf.Enabled ? Perf.T() : 0;
            try { if (__result > 0f) __result = ArmorQuality.Apply(__instance, __0, __result); } catch { } // 1.11.250
            try { if (__result > 0f) PhysPen.Adjust(__instance, __0, __2, __3, ref __result); } catch { } // 1.11.251 (#33): physical penetration of this shot (after the armour quality, which reads plate x quality)
            try { if (__result > 0f) { float f = Obliquity.Factor(__instance, __0, __3); if (f > 1f) __result *= f; } } catch { }
            if (t0 != 0) Perf.Add(Perf.ARMOR, t0);
        }
    }

    // 1.11.312 (#58): the side-up angle the game uses for the slope of the plate
    [HarmonyPatch(typeof(Shell), "ImpactForwardToAngleSideUp")]
    internal static class Patch_ObliquitySideUp
    {
        private static void Postfix(Vector3 __0, ref float __result) { try { Obliquity.FixSideUp(__0, ref __result); } catch { } Obliquity.LastUp = __result; Obliquity.LastUpFrame = Time.frameCount; }
    }
}
