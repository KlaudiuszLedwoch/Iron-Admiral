// 1.11.444 (player report hurepoix 30.10: "when a country declares war on me it takes back the score of a previous war - Britain
// declared war this month and starts at 237 000 VP for me and 10 000 for it; peace next month would make it pay 850 millions for
// a month where nothing happened"): the victory points live on the relation of the two nations and a new war started with the
// points left from the last one. Once a turn every relation between nations is watched: when a war ends its points are noted; when
// a new war starts between the same two nations, the points left over from the previous war are taken off (points won in the
// new war are kept). Wars already running when a save is loaded are left as they are. Switch: WarScoreReset.
using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class WarScoreReset
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("WarScoreReset", true, null, "Campaign: a new war between two nations starts with no victory points - the points left over from their previous war are not carried into it (they made an early peace cost a fortune)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][WarScore] " + m); }
        class St { public bool War; public float A, B; }
        static readonly Dictionary<string, St> seen = new Dictionary<string, St>();
        static string lastDate = "", campaign = ""; static float next; static int errors;

        static string Key(Relation r) { try { return r.a.data.name + "|" + r.b.data.name; } catch { return null; } }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 2f;
            try
            {
                var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) { seen.Clear(); lastDate = ""; return; }
                string date = ""; try { date = cc.CurrentDate.ToString(); } catch { }
                if (date == lastDate) return;
                bool first = lastDate == ""; lastDate = date;
                string camp = ""; try { camp = cc.CampaignData.GetHashCode().ToString(); } catch { }
                if (camp != campaign) { campaign = camp; seen.Clear(); first = true; }
                foreach (var kv in cc.CampaignData.Relations)
                {
                    var r = kv.Value; if (r == null) continue;
                    string k = Key(r); if (k == null) continue;
                    bool war = false; float a = 0f, b = 0f;
                    try { war = r.isWar; a = r.victoryPointsA; b = r.victoryPointsB; } catch { continue; }
                    if (!seen.TryGetValue(k, out var s)) { seen[k] = new St { War = war, A = war ? 0f : a, B = war ? 0f : b }; continue; }
                    if (!war) { s.War = false; s.A = a; s.B = b; continue; }      // at peace: the points left lie here
                    if (s.War) continue;                                            // a war already running
                    s.War = true;                                                   // a new war: take off what was left
                    if (first) continue;
                    if (s.A <= 0.5f && s.B <= 0.5f) continue;
                    float na = Mathf.Max(0f, a - s.A), nb = Mathf.Max(0f, b - s.B);
                    try { r.victoryPointsA = na; r.victoryPointsB = nb; } catch (Exception e) { if (errors++ < 5) Log("reset: " + e.Message); continue; }
                    Log($"new war {r.a.data.name} - {r.b.data.name}: victory points left from the previous war taken off ({a:0} -> {na:0} / {b:0} -> {nb:0})");
                    s.A = 0f; s.B = 0f;
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
    }
}
