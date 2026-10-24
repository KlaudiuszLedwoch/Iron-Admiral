// 1.11.184 (beta): historical weights (HistoricalWeights, F10 Battle AI tab, on by default, also for running
// campaigns). Values from the review in DIP_CHANGES.md (sources: ship weight breakdowns - Dreadnought, Bismarck, Richelieu, Myoko;
// navweaps mount weights):
//   machinery weight factor by ship type: battleships 1.6 -> 1.7, battlecruisers 1.5 -> 1.45, heavy and light cruisers 1.4/1.3 -> 1.0
//     (battleship machinery was 1.5-2x heavier per horsepower than cruiser machinery: 31-36 kg/hp vs 15-21 kg/hp around 1940);
//   fuel for operational range, % of displacement: DIP 14-28 -> 5.5-20 (Dreadnought 18.4 %, Bismarck 15.7 %, Richelieu 15.8 %,
//     Myoko ~20 % of full load);
//   armour of secondary mounts 0.08 -> 0.20 of the mount weight (US 5" mount 47.9 t at 0.75" vs 77.4 t at 2.5").
// Technologies still change these weights on top (engine, boiler and hull technologies). Existing designs are recomputed with the new
// values. The game's values come back when the switch is off.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HistoricalWeights
    {
        internal static MelonPreferences_Entry<bool> On, Casemates;
        internal static MelonPreferences_Entry<float> MinCrew;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalWeights", true, null, "Ship design: historical weights - battleship machinery heavier and cruiser machinery lighter, fuel for range as on real ships, heavier armoured secondary mounts (applies to existing designs too)");
            // 1.11.200: casemate mounts by gun technology. Sources (navweaps mount weights): a casemate mount of the same period and calibre
            //           weighs about a third to a quarter of a turret per gun (15 cm SK L/45 in a C/06 casemate 16-18 t vs 55-58 t per gun in
            //           Bismarck's twin turrets; US 5"/51 casemate 10-12 t vs 35-39 t per gun in the 5"/38 twin); against a fully armoured
            //           WWII dual-purpose turret the gap reaches 1:7. DIP uses one flat factor 0.5 of the turret base weight.
            Casemates = cat.CreateEntry("HistoricalCasemates", true, null, "Ship design: casemate mounts weigh a third to a quarter of a turret per gun, the ratio following the gun technology (Mk) of the ship instead of a flat half (applies to existing designs too)");
            MinCrew = cat.CreateEntry("MinCrewToSail", 0.35f, null, "Campaign: minimum crew (share of complement) for a ship to put to sea; a passage from port to port needs far fewer men than fighting (the game: 0.7). 0 = the game's value");
        }
        // casemate mount factor of the turret base weight by gun generation Mk 1..5 (the turret of the period grows heavier and better armoured)
        static readonly float[] CasemateByMk = { 0.40f, 0.35f, 0.30f, 0.25f, 0.20f };
        static float casemateOrig = -1f; static ParamData casemateParam; static int casemateLogged;
        internal static bool IsCasemate(PartData d) { try { return d != null && d.name != null && d.name.StartsWith("casemate"); } catch { return false; } }
        // Ship.PartWeight(Part): the game's casemate factor is set for this part by the ship's gun technology and put back afterwards
        internal static void CasemateBefore(Ship ship, Part part)
        {
            if (Casemates == null || !Casemates.Value || ship == null || part == null || !IsCasemate(part.data)) return;
            try
            {
                if (casemateParam == null) { var gd = G.GameData; if (gd == null || gd.paramsRaw == null || !gd.paramsRaw.TryGetValue("w_turret_casemate_mod", out casemateParam)) return; }
                if (casemateOrig < 0f) casemateOrig = casemateParam.value;
                int g = 1; try { g = ship.TechGunGrade(part.data, false); } catch { }
                g = Math.Max(1, Math.Min(5, g));
                casemateParam.value = CasemateByMk[g - 1];
                if (casemateLogged++ < 6) Log($"casemate {part.data.name} ({part.data.caliber:0} mm) on {ship.name}: Mk {g} - mount factor {casemateOrig:0.00} -> {CasemateByMk[g - 1]:0.00} of the turret weight");
            }
            catch { }
        }
        internal static void CasemateAfter() { try { if (casemateParam != null && casemateOrig >= 0f) casemateParam.value = casemateOrig; } catch { } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Weights] " + m); }
        static readonly Dictionary<string, float> origP = new Dictionary<string, float>();
        static readonly Dictionary<string, float> origE = new Dictionary<string, float>();
        static readonly Dictionary<string, float> TargetP = new Dictionary<string, float> { { "w_op_range_min", 5.5f }, { "w_op_range_max", 20f }, { "w_armor_secondaries", 0.20f } };
        static readonly Dictionary<string, float> TargetE = new Dictionary<string, float> { { "bb", 1.7f }, { "bc", 1.45f }, { "ca", 1.0f }, { "cl", 1.0f } };
        static IntPtr gdPtr; static int applied = -1; static float next; static int errors;

        internal static void Tick()
        {
            if (UnityEngine.Time.unscaledTime < next) return; next = UnityEngine.Time.unscaledTime + 2f;
            try
            {
                var gd = G.GameData; if (gd == null || gd.paramsRaw == null || gd.shipTypes == null) return;
                if (gd.Pointer != gdPtr) { gdPtr = gd.Pointer; origP.Clear(); origE.Clear(); applied = -1; }
                bool on = On != null && On.Value;
                if (on && applied != 1)
                {
                    var sb = new System.Text.StringBuilder("historical weights on: ");
                    foreach (var kv in TargetP)
                    {
                        ParamData p; if (!gd.paramsRaw.TryGetValue(kv.Key, out p) || p == null) { sb.Append(kv.Key + " (not found), "); continue; }
                        if (!origP.ContainsKey(kv.Key)) origP[kv.Key] = p.value;
                        p.value = kv.Value; sb.Append($"{kv.Key} {origP[kv.Key]} -> {kv.Value}, ");
                    }
                    foreach (var kv in TargetE)
                    {
                        ShipType st; if (!gd.shipTypes.TryGetValue(kv.Key, out st) || st == null) { sb.Append(kv.Key + " (not found), "); continue; }
                        if (!origE.ContainsKey(kv.Key)) origE[kv.Key] = st.engineWeightMod;
                        st.engineWeightMod = kv.Value; sb.Append($"machinery {kv.Key} {origE[kv.Key]} -> {kv.Value}, ");
                    }
                    Log(sb.ToString());
                    applied = 1;
                }
                else if (!on && applied != 0)
                {
                    if (applied == 1)
                    {
                        foreach (var kv in origP) { ParamData p; if (gd.paramsRaw.TryGetValue(kv.Key, out p) && p != null) p.value = kv.Value; }
                        foreach (var kv in origE) { ShipType st; if (gd.shipTypes.TryGetValue(kv.Key, out st) && st != null) st.engineWeightMod = kv.Value; }
                        Log("historical weights off: game values restored");
                    }
                    applied = 0;
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
            try { MinCrewTick(); } catch (Exception e) { if (errors++ < 5) Log("min crew: " + e.Message); }
        }
        // 1.11.200: minimum crew to put to sea (min_crew_threshold)
        static float minCrewOrig = -1f, minCrewApplied = -1f; static IntPtr minCrewGd;
        static void MinCrewTick()
        {
            var gd = G.GameData; if (gd == null || gd.paramsRaw == null || MinCrew == null) return;
            ParamData p; if (!gd.paramsRaw.TryGetValue("min_crew_threshold", out p) || p == null) return;
            if (minCrewOrig < 0f || gd.Pointer != minCrewGd) { minCrewOrig = p.value; minCrewGd = gd.Pointer; minCrewApplied = -1f; }
            float want = MinCrew.Value > 0f ? UnityEngine.Mathf.Clamp(MinCrew.Value, 0.1f, 0.9f) : minCrewOrig;
            if (Math.Abs(want - minCrewApplied) < 0.001f) return;
            p.value = want; minCrewApplied = want;
            Log($"minimum crew to put to sea: {minCrewOrig:0.00} -> {want:0.00}");
        }
    }

    // 1.11.200: casemate mount weight by the ship's gun technology (HistoricalWeights.CasemateBefore/After)
    [HarmonyLib.HarmonyPatch(typeof(Ship), "PartWeight", new Type[] { typeof(Part) })]
    internal static class Patch_PartWeight_Casemate
    {
        private static void Prefix(Ship __instance, Part __0) { try { HistoricalWeights.CasemateBefore(__instance, __0); } catch { } }
        private static void Postfix() { HistoricalWeights.CasemateAfter(); }
    }
}
