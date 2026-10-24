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
            Scale = cat.CreateEntry("EraAccuracyScale", 1.0f, null, "Battle (hit chance by era): multiplier of the whole table (1 = historical, higher = more hits)");
        }
        // year -> multiplier of the game's hit chance (calibrated in auto tests, see the log lines "[Ballistics] ... hits")
        static readonly int[] Years = { 1890, 1905, 1916, 1930, 1945 };
        static readonly float[] Mult = { 0.35f, 0.35f, 0.10f, 0.12f, 0.15f };
        internal static float Factor()
        {
            if (On == null || !On.Value) return 1f;
            int y = Ballistics.Year();
            float m = Mult[Mult.Length - 1];
            if (y <= Years[0]) m = Mult[0];
            else for (int i = 1; i < Years.Length; i++) if (y <= Years[i]) { float t = (y - Years[i - 1]) / (float)(Years[i] - Years[i - 1]); m = Mult[i - 1] + (Mult[i] - Mult[i - 1]) * t; break; }
            return m * Math.Max(0.05f, Scale.Value);
        }
    }

    [HarmonyPatch(typeof(GunData), "HitChanceMult")]
    internal static class Patch_HitChanceMult_Era
    {
        static void Postfix(ref float __result) { try { if (GameManager.IsBattle) __result *= EraAccuracy.Factor(); } catch { } }
    }
}
