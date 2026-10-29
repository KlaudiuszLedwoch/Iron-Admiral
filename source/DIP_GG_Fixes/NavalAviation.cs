// 1.11.407 (#85, step 1 - air reconnaissance in the campaign; author 29.10 22:58: technologies as the new type naval_aviation,
// "the list looks OK"; 23:46: "work to the plan"): the Naval Aviation technologies a nation has researched decide how far its
// air patrols reach from its own ports:
//   av_recon_1 coastal seaplane stations (1912)   ~100 km
//   av_recon_2 naval airships (1915)              ~400 km
//   av_recon_3 flying boats (1918)                ~500 km
//   av_recon_4 long-range maritime patrol (1930)  ~900 km
//   av_recon_5 airborne search radar (1941)       ~1000 km, and flies in bad weather
// (ranges: the working list claude/technologie_lotnicze_85.md - estimates, not from a source). An enemy force within that range
// of one of the nation's ports adds up to AirReconQ (0.30) to the intelligence quality Fog.Q, falling off with distance. Air
// patrols do not fly in bad weather: in the winter months 50 %, otherwise 30 % of the months in each 800 km sea area are
// unflyable (with the airborne radar 15 %). Switch: NavalAviationRecon.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class NavalAviation
    {
        internal static MelonPreferences_Entry<bool> Recon;
        internal static MelonPreferences_Entry<float> ReconQ;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Recon = cat.CreateEntry("NavalAviationRecon", true, null, "Fog of war: the Naval Aviation technologies (seaplane stations, airships, flying boats, long-range patrol, airborne radar) let a nation's air patrols find enemy forces within their range of its ports - not in bad weather");
            ReconQ = cat.CreateEntry("NavalAviationReconQ", 0.30f, null, "Fog of war: the most that air patrols add to the intelligence quality of a force close to one of our ports (0.30)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][NavalAviation] " + m); }

        // researched Naval Aviation technologies per nation, refreshed each month
        static int month = -1; static readonly Dictionary<IntPtr, HashSet<string>> have = new Dictionary<IntPtr, HashSet<string>>();
        static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; } }
        internal static bool Has(Player p, string id)
        {
            if (p == null) return false;
            int m = Month(); if (m != month) { month = m; have.Clear(); }
            if (!have.TryGetValue(p.Pointer, out var set))
            {
                set = new HashSet<string>();
                try
                {
                    var tl = p.technologies;
                    for (int i = 0; tl != null && i < tl.Count; i++)
                    {
                        var t = tl[i]; if (t == null || t.data == null) continue;
                        string n = t.data.name ?? ""; if (!n.StartsWith("av_") && !n.StartsWith("at_") && !n.StartsWith("aa_")) continue;
                        bool ok = false; try { ok = t.isResearched; } catch { }
                        if (ok) set.Add(n);
                    }
                }
                catch { }
                have[p.Pointer] = set;
            }
            return set.Contains(id);
        }
        internal static int ReconLevel(Player p)
        {
            for (int l = 5; l >= 1; l--) if (Has(p, "av_recon_" + l)) return l;
            return 0;
        }
        static readonly float[] rangeKm = { 0f, 100f, 400f, 500f, 900f, 1000f };
        static readonly string[] what = { "", "seaplane stations", "airships", "flying boats", "long-range patrol", "patrol aircraft with radar" };
        static int logged;

        // added to Fog.Q: own port portKm away from the force, month m, 800 km sea area cx/cz
        internal static float AirQ(Player obs, float portKm, int m, int cx, int cz, out string why)
        {
            why = "";
            if (Recon == null || !Recon.Value || obs == null) return 0f;
            int lv = ReconLevel(obs); if (lv == 0) return 0f;
            float range = rangeKm[lv]; if (portKm >= range) { why = $"air {what[lv]}: out of range ({portKm:0} of {range:0} km)"; return 0f; }
            int mon = ((m - 1) % 12 + 12) % 12 + 1; bool winter = mon == 11 || mon == 12 || mon == 1 || mon == 2;
            float bad = lv >= 5 ? 0.15f : winter ? 0.5f : 0.3f;
            if (Fog.Roll($"air|{m}|{cx}|{cz}") < bad) { why = $"air {what[lv]}: grounded by the weather"; return 0f; }
            float q = (ReconQ != null ? ReconQ.Value : 0.30f) * Mathf.Clamp01(1f - portKm / range);
            why = $"air {what[lv]} +{q:0.00} ({portKm:0} of {range:0} km)";
            if (obs.isMain && logged++ < 10) Log($"{obs.data.name}: {why}");
            return q;
        }
    }
}
