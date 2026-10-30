// 1.11.467 (author 30.10 20:34: "players must be able to control the spending on aviation, and the spending must be tied to the
// amount of aviation"; 20:40 answers: a 0-200% slider, it drives carrier strikes, shore strikes and air reconnaissance, the number of
// aircraft follows the spending gradually, the AI spends by national doctrine).
//   Budget: 100% = the historical upkeep of the naval air arm (NavalAviationUpkeepScale, % of the fleet maintenance by carrier
//   technology); the player sets 0-200% with the slider under the treasury panel (F8); the cost of the air arm scales with it.
//   Air strength ("stock", 1.00 = the standard air arm of the nation's technologies): moves each month towards budget/100 by at most
//   NavalAviationBudgetStep (10 points) - aircraft and crews take time to buy and train, and an air arm left without money wears out
//   slowly; aircraft shot down in battle are lost from it (a standard air arm is NavalAviationBudgetPool times the aircraft of one
//   full carrier and shore strike).
//   Effects of the strength F: aircraft per wave x F (at least 1), waves x min(1, F), shore strike chance x min(1, F), air
//   reconnaissance quality x min(1.25, F); below 0.1 the nation has no aircraft to send.
//   AI: by doctrine (NavalAviationBudgetAi) - Japan and the USA strong carrier navies, Britain average (the RAF held the Fleet Air
//   Arm until 1937), Germany and the rest below the standard.
// The budget and the strength are saved with the economy state of the campaign (eco_*.txt). Switch: NavalAviationBudget.
using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class AirBudget
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Step, Pool;
        internal static MelonPreferences_Entry<string> Ai;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("NavalAviationBudget", true, null, "Campaign: the player sets the naval aviation budget (0-200% of the historical upkeep) with the slider under the treasury panel (F8); the number of aircraft follows the budget month by month and drives carrier strikes, shore strikes and air reconnaissance; aircraft shot down are lost; the AI spends by national doctrine");
            Step = cat.CreateEntry("NavalAviationBudgetStep", 10f, null, "Campaign: how many points (of the standard air arm, 100) the aircraft strength can change in a month towards the budget");
            Pool = cat.CreateEntry("NavalAviationBudgetPool", 3f, null, "Campaign: the standard air arm, in full carrier plus shore strikes - each aircraft shot down costs 1/(this x aircraft of one strike) of the strength");
            Ai = cat.CreateEntry("NavalAviationBudgetAi", "japan:150,usa:130,britain:100,italy:90,france:80,germany:70,*:80", null, "Campaign: naval aviation budget (%) of the AI nations by doctrine, nation:percent, * for the rest");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirBudget] " + m); }
        internal static bool Enabled { get { return On != null && On.Value && Economy.On; } }

        static float AiPct(Player p)
        {
            string n = ""; try { n = (p.data.name ?? "").ToLowerInvariant(); } catch { }
            float def = 100f;
            foreach (var part in (Ai != null ? Ai.Value : "").Split(','))
            {
                var kv = part.Split(':'); if (kv.Length != 2) continue;
                float v; if (!float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                string k = kv[0].Trim().ToLowerInvariant();
                if (k == n) return Mathf.Clamp(v, 0f, 200f);
                if (k == "*") def = v;
            }
            return Mathf.Clamp(def, 0f, 200f);
        }
        static bool IsMain(Player p) { try { return p.isMain; } catch { return false; } }

        // budget in percent of the historical upkeep
        internal static float Pct(Player p)
        {
            if (!Enabled || p == null) return 100f;
            if (!IsMain(p)) return AiPct(p);
            var st = Economy.Get(p); if (st == null) return 100f;
            if (st.AirBudget < 0f) st.AirBudget = 100f;
            return st.AirBudget;
        }
        internal static void SetPct(Player p, float v)
        {
            var st = Economy.Get(p); if (st == null) return;
            v = Mathf.Clamp(Mathf.Round(v / 10f) * 10f, 0f, 200f);
            if (Math.Abs(v - st.AirBudget) < 0.5f) return;
            Log($"{p.data.name}: naval aviation budget {st.AirBudget:0}% -> {v:0}% (aircraft strength now {Stock(p) * 100f:0}%)");
            st.AirBudget = v;
        }
        // air strength: 1.00 = the standard air arm of the nation's technologies
        internal static float Stock(Player p)
        {
            if (!Enabled || p == null) return 1f;
            var st = Economy.Get(p); if (st == null) return 1f;
            if (st.AirStock < 0f) st.AirStock = Pct(p) / 100f;
            return Mathf.Clamp(st.AirStock, 0f, 2f);
        }
        internal static float F(Player p) { return Stock(p); }
        internal static bool NoAircraft(Player p) { return Enabled && Stock(p) < 0.1f; }
        internal static int PerWave(int n, Player p) { if (!Enabled) return n; float f = Stock(p); return f < 0.1f ? 0 : Math.Max(1, Mathf.RoundToInt(n * f)); }
        internal static int Waves(int n, Player p) { if (!Enabled) return n; float f = Stock(p); return f < 0.1f ? 0 : Math.Max(1, Mathf.RoundToInt(n * Mathf.Min(1f, f))); }
        internal static float ChanceF(Player p) { return Enabled ? Mathf.Clamp01(Stock(p)) : 1f; }
        internal static float ReconF(Player p) { if (!Enabled) return 1f; float f = Stock(p); return f < 0.1f ? 0f : Mathf.Min(1.25f, f); }

        // each month the strength moves towards the budget
        internal static void Month(Player p, EconState st)
        {
            if (!Enabled || p == null || st == null) return;
            float target = Pct(p) / 100f, s = Stock(p), step = Mathf.Max(0.01f, Step.Value / 100f);
            float ns = s < target ? Mathf.Min(target, s + step) : Mathf.Max(target, s - step);
            if (Math.Abs(ns - s) < 0.0001f) return;
            st.AirStock = ns;
            if (IsMain(p)) Log($"{p.data.name}: aircraft strength {s * 100f:0}% -> {ns * 100f:0}% (budget {target * 100f:0}%)");
        }
        // aircraft shot down in a campaign battle
        internal static void Lost(Player p, int n, int strikeSize)
        {
            if (!Enabled || p == null || n <= 0) return;
            try { if (!NavalAviation.CampaignBattle()) return; } catch { return; }
            var st = Economy.Get(p); if (st == null) return;
            float pool = Mathf.Max(6f, Pool.Value * Math.Max(1, strikeSize));
            float s = Stock(p), ns = Mathf.Max(0f, s - n / pool);
            st.AirStock = ns;
            if (IsMain(p) || n >= 3) Log($"{p.data.name}: {n} aircraft lost - strength {s * 100f:0}% -> {ns * 100f:0}% (standard air arm ~{pool:0} aircraft)");
        }
        internal static string Line(Player p)
        {
            float pct = Pct(p), s = Stock(p);
            string trend = s * 100f < pct - 0.5f ? " (growing)" : s * 100f > pct + 0.5f ? " (shrinking)" : "";
            return $"Naval aviation budget {pct:0}% - aircraft strength {s * 100f:0}% of the standard{trend}";
        }
    }
}
