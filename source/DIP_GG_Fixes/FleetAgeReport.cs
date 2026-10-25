// 1.11.219 (author, 24.10): why does the AI fight with obsolete ships? In the author's 1921 campaign Britain sent pre-dreadnought
// battleships and a heavy cruiser designed in July 1921 on the 1890s Armored Cruiser V hull. This report writes, once per campaign
// month for every great power, per ship type: ships in service with the year of their hull's technology (average and oldest),
// the newest hull the nation has unlocked, and the hulls of its current designs - so the cause (research, hull choice, no
// replacement of old ships) can be read from the log. Log only; no effect on the game.
using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class FleetAgeReport
    {
        static int lastMonth = -1; static float next; static int errors;
        static readonly string[] Types = { "bb", "bc", "ca", "cl", "dd", "tb" };
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][FleetAge] " + m); }

        internal static void Tick()
        {
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 20f;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle) return;
                var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) return;
                int m; try { var d = cc.CurrentDate.AsDate(); m = d.Year * 12 + d.Month; } catch { return; }
                if (m == lastMonth) return; lastMonth = m;
                var players = cc.CampaignData.Players; if (players == null) return;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i]; if (p == null || p.data == null) continue;
                    bool major = false; try { major = p.isMajor; } catch { }
                    if (!major) continue;
                    Report(p);
                }
            }
            catch (Exception e) { if (errors++ < 3) Log("tick: " + e.Message); }
        }

        static void Report(Player p)
        {
            try
            {
                var fleet = new List<Ship>(); try { var e = p.fleet.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (ue.MoveNext()) { var s = e.Current; if (s != null) fleet.Add(s); } } catch { }
                var designs = new List<Ship>(); try { var e = p.designs.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (ue.MoveNext()) { var s = e.Current; if (s != null) designs.Add(s); } } catch { }
                var sb = new System.Text.StringBuilder(); sb.Append(p.data.name).Append(p.isAi ? " (AI)" : " (player)").Append(": ");
                foreach (var t in Types)
                {
                    int n = 0, sumY = 0, oldest = 9999, newest = 0; int unknown = 0;
                    foreach (var s in fleet)
                    {
                        string st = null; try { st = s.shipType != null ? s.shipType.name : null; } catch { }
                        if (st != t) continue;
                        n++;
                        int y = 0; try { y = ShipGenSpeed.HullYear(s.hull != null && s.hull.data != null ? s.hull.data.name : null); } catch { }
                        if (y <= 0) { unknown++; continue; }
                        sumY += y; if (y < oldest) oldest = y; if (y > newest) newest = y;
                    }
                    string hn; int top = ShipGenSpeed.NewestUnlocked(p, t, out hn);
                    var dh = new List<string>();
                    foreach (var s in designs)
                    {
                        string st = null; try { st = s.shipType != null ? s.shipType.name : null; } catch { }
                        if (st != t) continue;
                        string h = null; try { h = s.hull != null && s.hull.data != null ? s.hull.data.name : null; } catch { }
                        if (h != null) dh.Add(h + "(" + ShipGenSpeed.HullYear(h) + ")");
                    }
                    if (n == 0 && top == 0 && dh.Count == 0) continue;
                    int known = n - unknown;
                    sb.Append(t.ToUpper()).Append(' ').Append(n).Append(" ships");
                    if (known > 0) sb.Append($" (hull years avg {sumY / known}, {oldest}-{newest})");
                    sb.Append($", newest hull unlocked {(top > 0 ? top + " " + hn : "none")}");
                    if (dh.Count > 0) sb.Append(", designs: ").Append(string.Join(" ", dh.Distinct().Take(4)));
                    sb.Append("; ");
                }
                Log(sb.ToString());
            }
            catch (Exception e) { if (errors++ < 3) Log("report " + p.data.name + ": " + e.Message); }
        }
    }
}
