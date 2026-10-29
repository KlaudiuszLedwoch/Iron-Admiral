// 1.11.368 (#84 step a, author 29.10: "the AI should make its designs against the fleets its opponents have - if someone has
// battleships with 12 in guns the AI should try larger calibres; if someone has a slow fleet the AI should build torpedo boats to
// fight it; the AI should fit its fleet to the fleets of the states it has bad relations with, planning war with them").
// Step a (log only, no effect yet): once per campaign month, for every AI great power - its likely enemies (at war, or attitude
// at or below EnemyProfileAttitude) and their fleet profile: capital ships, largest main calibre and how many ships carry it,
// battle-line speed (average and slowest capital ship), cruisers and torpedo craft; plus the design targets and the build mix the
// later steps will use. Switch: EnemyProfileLog.
using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class EnemyProfile
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Attitude;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("EnemyProfileLog", true, null, "Campaign AI: once a month every AI great power sizes up its likely enemies (at war or bad relations) - their largest guns, battle-line speed, cruisers and torpedo craft - and writes the design targets and build mix it would take (log; the basis for designs fitted against the enemy)");
            Attitude = cat.CreateEntry("EnemyProfileAttitude", -30f, null, "Campaign AI: relations at or below this attitude make a nation a likely enemy (at war: always)");
        }
        static int lastMonth = -1; static float next; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][EnemyProfile] " + m); }

        internal class Profile
        {
            public string Name; public float Weight; public int Capitals, Cruisers, Torpedo; public float MaxCal; public int MaxCalShips;
            public float LineSpeed, SlowestCapital = 99f;
        }
        internal static readonly Dictionary<string, List<Profile>> Last = new Dictionary<string, List<Profile>>(); // AI nation -> enemies (for steps b, c)

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
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
                    bool major = false, ai = false; try { major = p.isMajor; ai = p.isAi; } catch { }
                    if (major && ai) Report(cc, p);
                }
            }
            catch (Exception e) { if (errors++ < 3) Log("tick: " + e.Message); }
        }

        static List<Ship> Fleet(Player p)
        {
            var l = new List<Ship>();
            try { var e = p.fleet.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (ue.MoveNext()) { var s = e.Current; if (s != null) l.Add(s); } } catch { }
            return l;
        }
        static float MainCal(Ship s)
        {
            float best = 0f;
            try
            {
                var parts = s.parts; if (parts == null) return 0f;
                for (int j = 0; j < parts.Count; j++)
                {
                    var pd = parts[j] != null ? parts[j].data : null; if (pd == null || !pd.isGun) continue;
                    GunData gd = null; try { gd = G.GameData.GunData(pd); } catch { }
                    if (gd != null && gd.caliberInch > best) best = gd.caliberInch;
                }
            }
            catch { }
            return best;
        }
        static string T(Ship s) { try { return s.shipType != null ? s.shipType.name : ""; } catch { return ""; } }

        static Profile Of(Player e, float weight)
        {
            var pr = new Profile { Name = Economy.Name(e), Weight = weight };
            float spd = 0f; int ns = 0;
            foreach (var s in Fleet(e))
            {
                string t = T(s);
                if (t == "bb" || t == "bc")
                {
                    pr.Capitals++;
                    float c = MainCal(s);
                    if (c > pr.MaxCal + 0.05f) { pr.MaxCal = c; pr.MaxCalShips = 1; } else if (Math.Abs(c - pr.MaxCal) <= 0.05f) pr.MaxCalShips++;
                    float kn = 0f; try { kn = s.speedMax * 1.94384f; } catch { }
                    if (kn > 1f) { spd += kn; ns++; if (kn < pr.SlowestCapital) pr.SlowestCapital = kn; }
                }
                else if (t == "ca" || t == "cl") pr.Cruisers++;
                else if (t == "dd" || t == "tb") pr.Torpedo++;
            }
            pr.LineSpeed = ns > 0 ? spd / ns : 0f; if (ns == 0) pr.SlowestCapital = 0f;
            return pr;
        }

        static void Report(CampaignController cc, Player p)
        {
            try
            {
                var list = new List<Profile>();
                foreach (var kv in cc.CampaignData.Relations)
                {
                    var r = kv.Value; if (r == null || !r.AnyOnePlayerIs(p)) continue;
                    var o = r.OppositePlayer(p); if (o == null || o.data == null || o.Pointer == p.Pointer) continue;
                    bool dis = false, war = false; float att = 0f; try { dis = o.isDisabled; war = r.isWar; att = r.attitude; } catch { }
                    if (dis || (!war && att > Attitude.Value)) continue;
                    list.Add(Of(o, war ? 2f : Mathf.Clamp(-att / 50f, 0.5f, 2f)));
                }
                Last[p.data.name] = list;
                if (list.Count == 0) { Log($"{Economy.Name(p)}: no likely enemy (no war, no attitude at or below {Attitude.Value:0})"); return; }
                var own = Of(p, 1f);
                var main = list.OrderByDescending(x => x.Weight * Math.Max(1, x.Capitals)).First();
                float wantCal = list.Max(x => x.MaxCal);
                float wantSpeed = list.Where(x => x.LineSpeed > 0f).Select(x => x.LineSpeed).DefaultIfEmpty(0f).Max();
                var mix = new List<string>();
                if (main.Capitals > 0 && own.LineSpeed > 0f && main.LineSpeed > 0f && main.LineSpeed < own.LineSpeed - 1.5f) mix.Add("more torpedo craft against a slower battle line");
                if (main.Capitals > 0 && main.Torpedo < main.Capitals * 2) mix.Add("more torpedo craft - weak enemy screen");
                if (main.Torpedo > Math.Max(4, own.Torpedo)) mix.Add("more light cruisers and destroyers as screen");
                if (main.Capitals > own.Capitals * 1.5f + 1) mix.Add("torpedo craft instead of a losing battleship race");
                Log($"{Economy.Name(p)}: likely enemies " + string.Join("; ", list.Select(x => $"{x.Name} (weight {x.Weight:0.0}: {x.Capitals} capital ships, largest guns {x.MaxCal:0.#} in on {x.MaxCalShips}, line speed {x.LineSpeed:0.0} kn, slowest {x.SlowestCapital:0.0}; {x.Cruisers} cruisers, {x.Torpedo} torpedo craft)"))
                    + $" | own: {own.Capitals} capital ships, largest guns {own.MaxCal:0.#} in, line speed {own.LineSpeed:0.0} kn, {own.Torpedo} torpedo craft"
                    + $" | design targets: main guns at least {wantCal:0.#} in (one step more when researched), capital speed at least {wantSpeed:0.0} kn"
                    + $" | build mix: {(mix.Count > 0 ? string.Join(", ", mix) : "no change")}");
            }
            catch (Exception e) { if (errors++ < 5) Log($"{p.data?.name}: {e.Message}"); }
        }
    }
}
