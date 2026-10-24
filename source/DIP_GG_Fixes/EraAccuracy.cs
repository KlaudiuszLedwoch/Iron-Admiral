// 1.11.168 (beta): hit chance by era. Measured in auto tests with DIP values: battleships in 1916 at 10-16 km hit with 30-40% of
// their 15" shells, about ten times more than in real battles (Jutland: British 2.7%, German 3.4% of heavy shells).
// The game's per-gun hit chance multiplier (GunData.HitChanceMult) is scaled by a factor that follows the year of the battle,
// calibrated with auto tests against the hit rates of real battles:
//   pre-dreadnoughts at 5-7 km (Tsushima ~9%), dreadnoughts with optical fire control at 10-19 km (Jutland ~3%),
//   WWII optical fire control at 16-24 km (2-5%); radar (game technology) adds its own bonus on top.
// EraAccuracyScale multiplies the whole table (1 = historical).
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class EraAccuracy
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Scale;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("EraAccuracy", true, null, "Battle: hit chance follows the era, calibrated to the hit rates of real battles (pre-dreadnought fights, Jutland, WWII) - much lower than DIP at long range");
            Radar = cat.CreateEntry("EraAccuracyRadar", 3.0f, null, "Battle (hit chance by era): multiplier of the hit chance for ships with fire-control radar (historically radar gave 3-5x more hits than optical fire control)");
            Scale = cat.CreateEntry("EraAccuracyScale", 1.0f, null, "Battle (hit chance by era): multiplier of the whole table (1 = historical, higher = more hits)");
        }
        // year -> multiplier of the game's hit chance (calibrated in auto tests, see the log lines "[Ballistics] ... hits")
        static readonly int[] Years = { 1890, 1905, 1916, 1930, 1945 };
        static readonly float[] Mult = { 0.35f, 0.30f, 0.10f, 0.05f, 0.03f }; // 1.11.171: 1940 test with 0.14 still gave 16" 44-61% at 8-14 km (the game hit chance saturates), so WWII is scaled harder
        // 1.11.175: fire-control radar. Radar A/B tests (AutoTestLab, 1916, radar fitted to every second ship): the game's own radar
        // component changed hit rates very little (14+ km: 2.4% with radar vs 2.8% without). Historically radar fire control gave
        // 3-5x more hits than optical (Washington, Guadalcanal 1942: 11-27%; Duke of York, North Cape 1943; optical WWII 2-5%).
        // Ships with fire-control radar get EraAccuracyRadar x the hit chance (independent of the year).
        internal static MelonPreferences_Entry<float> Radar;
        internal static float RadarFactor(Ship s) { if (On == null || !On.Value || Radar == null) return 1f; return GunLab.HasRadar(s) ? Math.Max(1f, Radar.Value) : 1f; }
        internal static float Factor()
        {
            if (On == null || !On.Value) return 1f;
            int y = Ballistics.Year();
            float m = Mult[Mult.Length - 1];
            if (y <= Years[0]) m = Mult[0];
            else for (int i = 1; i < Years.Length; i++) if (y <= Years[i]) { float t = (y - Years[i - 1]) / (float)(Years[i] - Years[i - 1]); m = Mult[i - 1] + (Mult[i] - Mult[i - 1]) * t; break; }
            return m * Math.Max(0.05f, Scale.Value);
        }

        // ---- 1.11.169: dispersion of missing shells by era (EraDispersion, on) ----
        // DIP narrowed dispersion to a point (splash_disp_far 555 -> 100, max_shell_dispersion 65 -> 10, ...). The game's own (vanilla)
        // values give salvo patterns close to WWI practice (12" pattern ~370 m, 15" ~180 m at 11 km); WWII guns were tighter at longer
        // range (US 16"/50 ~180-270 m in range, Massachusetts 1942), pre-dreadnought fire wider. Vanilla values x era factor.
        internal static MelonPreferences_Entry<bool> Dispersion;
        internal static void CreateDispersionPrefs(MelonPreferences_Category cat)
        {
            Dispersion = cat.CreateEntry("EraDispersion", true, null, "Battle: missing shells spread like real salvos of their era (wide patterns before 1905, Jutland-era patterns in WWI, tighter in WWII) instead of landing in one spot as in DIP");
        }
        static readonly string[] DispNames = { "splash_disp_far", "splash_disp_close", "shell_max_distance_dispersion", "max_shell_dispersion", "splash_disp_normal_worst", "splash_disp_aiming_worst", "splash_overhead_close", "splash_overhead_far" };
        static readonly float[] DispVanilla = { 555f, 4f, 1500f, 65f, 0.5f, 0.35f, 25f, 2.4f };
        static readonly int[] DYears = { 1890, 1905, 1918, 1935, 1945 };
        static readonly float[] DMult = { 1.3f, 1.2f, 1.0f, 0.8f, 0.65f };
        static float DispFactor(int y)
        {
            if (y <= DYears[0]) return DMult[0];
            for (int i = 1; i < DYears.Length; i++) if (y <= DYears[i]) { float t = (y - DYears[i - 1]) / (float)(DYears[i] - DYears[i - 1]); return DMult[i - 1] + (DMult[i] - DMult[i - 1]) * t; }
            return DMult[DMult.Length - 1];
        }
        static readonly System.Collections.Generic.Dictionary<string, float> orig = new System.Collections.Generic.Dictionary<string, float>();
        static IntPtr gdPtr; static int applied = -1, appliedYear; static float next; static int errors;
        static ParamData P(string name) { try { var gd = G.GameData; if (gd == null || gd.paramsRaw == null) return null; ParamData p; return gd.paramsRaw.TryGetValue(name, out p) ? p : null; } catch { return null; } }
        internal static void Tick()
        {
            if (UnityEngine.Time.unscaledTime < next) return; next = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                var gd = G.GameData; if (gd == null) return;
                if (gd.Pointer != gdPtr) { gdPtr = gd.Pointer; orig.Clear(); applied = -1; }
                bool on = Dispersion != null && Dispersion.Value;
                int year = on ? Ballistics.Year() : 0;
                if (on && (applied != 1 || year != appliedYear))
                {
                    float f = DispFactor(year); var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < DispNames.Length; i++)
                    {
                        var p = P(DispNames[i]); if (p == null) { sb.Append(DispNames[i] + " (not found), "); continue; }
                        if (!orig.ContainsKey(DispNames[i])) orig[DispNames[i]] = p.value;
                        float v = DispVanilla[i] * (i >= 4 ? 1f : f); // range-like values scale with the era, worst-case multipliers stay vanilla
                        p.value = v; sb.Append($"{DispNames[i]} {orig[DispNames[i]]} -> {v}, ");
                    }
                    MelonLogger.Msg($"[DIP GG Fixes][Accuracy] dispersion by era on (year {year}, x{f:0.00}): {sb}");
                    applied = 1; appliedYear = year;
                }
                else if (!on && applied != 0)
                {
                    if (applied == 1) { foreach (var kv in orig) { var p = P(kv.Key); if (p != null) p.value = kv.Value; } MelonLogger.Msg("[DIP GG Fixes][Accuracy] dispersion by era off: game values restored"); }
                    applied = 0;
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Msg("[DIP GG Fixes][Accuracy] dispersion: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GunData), "HitChanceMult")]
    internal static class Patch_HitChanceMult_Era
    {
        static void Postfix(Ship __0, ref float __result) { try { if (GameManager.IsBattle) __result *= EraAccuracy.Factor() * EraAccuracy.RadarFactor(__0); } catch { } }
    }
}
