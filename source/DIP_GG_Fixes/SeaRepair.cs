// 1.11.453 (author 30.10 18:12: "ships should gradually repair themselves during a voyage, at 3 % per turn - makeshift repairs
// at sea - never to 100 %; I think half of the damage received is the most"): once a turn every ship at sea (not in port, not
// repairing) loses SeaRepairPerTurn of its damage, down to half the worst damage it has had since its last repair in port (the
// other half needs a dockyard). For the player and the AI. Experimental until the night test has shown how the game's
// Ship.ApplyDamageOverall treats the value (each repair is logged with the damage before and after). Switch: SeaRepair.
using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class SeaRepair
    {
        internal static MelonPreferences_Entry<bool> On; internal static MelonPreferences_Entry<float> PerTurn, MaxShare;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SeaRepair", false, null, "Campaign (experimental until tested): ships at sea make makeshift repairs - their damage falls a little every turn, but never below half of the worst damage since their last repair in port");
            PerTurn = cat.CreateEntry("SeaRepairPerTurn", 3f, null, "Campaign: damage repaired at sea per turn (% of the ship)");
            MaxShare = cat.CreateEntry("SeaRepairMaxShare", 50f, null, "Campaign: the most of the damage received that can be repaired at sea (%) - the rest needs a dockyard");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][SeaRepair] " + m); }
        static readonly Dictionary<string, float> peak = new Dictionary<string, float>();
        static string lastDate = ""; static float next; static int logged, errors;

        internal static void Tick()
        {
            if (On == null || !On.Value || Time.unscaledTime < next) return; next = Time.unscaledTime + 3f;
            try
            {
                var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) { lastDate = ""; return; }
                bool battle = false; try { battle = GameManager.IsBattle; } catch { } if (battle) return;
                string date = ""; try { date = cc.CurrentDate.ToString(); } catch { }
                if (date == lastDate) return;
                bool first = lastDate == ""; lastDate = date;
                float step = Mathf.Clamp(PerTurn.Value, 0f, 50f) / 100f, keep = 1f - Mathf.Clamp(MaxShare.Value, 0f, 100f) / 100f;
                int repaired = 0;
                var ps = cc.CampaignData.Players;
                for (int i = 0; ps != null && i < ps.Count; i++)
                {
                    var p = ps[i]; if (p == null) continue;
                    var ships = new List<Ship>();
                    try { var e = p.fleet.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (ue.MoveNext()) { var s = e.Current; if (s != null) ships.Add(s); } } catch { continue; }
                    foreach (var s in ships)
                    {
                        string id; try { id = s.id.ToString(); } catch { continue; }
                        float d = 0f; try { d = Mathf.Clamp01(s.DamageEstimation()); } catch { continue; }
                        bool sea = false, normal = false; try { sea = s.IsInSea; } catch { } try { normal = s.status == VesselEntity.Status.Normal; } catch { }
                        float pk; if (!peak.TryGetValue(id, out pk)) pk = d;
                        if (!sea || !normal) pk = d; else if (d > pk) pk = d;   // in port or repairing: the worst damage starts again from now
                        peak[id] = pk;
                        if (first || !sea || !normal || d <= 0.005f || step <= 0f) continue;
                        float floor = pk * keep, want = Math.Max(floor, d - step);
                        if (want >= d - 0.001f) continue;
                        float after = d;
                        try { s.ApplyDamageOverall(want); after = Mathf.Clamp01(s.DamageEstimation()); } catch (Exception ex) { if (errors++ < 5) Log("apply: " + ex.Message); continue; }
                        repaired++;
                        if (logged++ < 30 || p.isMain) Log($"{s.name} ({p.data.name}) at sea: damage {d * 100f:0.0}% -> {after * 100f:0.0}% (asked {want * 100f:0.0}%, worst since port {pk * 100f:0.0}%, at most down to {floor * 100f:0.0}%)");
                        if (after > d + 0.001f) { Log($"STOP: ApplyDamageOverall raised the damage of {s.name} - sea repair switched off"); On.Value = false; return; }
                    }
                }
                if (repaired > 0) Log($"turn {date}: {repaired} ship(s) repaired at sea");
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
    }
}
