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
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalWeights", true, null, "Ship design: historical weights - battleship machinery heavier and cruiser machinery lighter, fuel for range as on real ships, heavier armoured secondary mounts (applies to existing designs too)");
        }
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
        }
    }
}
