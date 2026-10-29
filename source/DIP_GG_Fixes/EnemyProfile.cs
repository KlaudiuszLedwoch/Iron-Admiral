// 1.11.368 (#84 step a, author 29.10: "the AI should make its designs against the fleets its opponents have - if someone has
// battleships with 12 in guns the AI should try larger calibres; if someone has a slow fleet the AI should build torpedo boats to
// fight it; the AI should fit its fleet to the fleets of the states it has bad relations with, planning war with them").
// Step a (log only): once per campaign month, for every AI great power - its likely enemies (at war, or attitude at or below
// EnemyProfileAttitude) and their fleet profile: capital ships, largest main calibre and how many ships carry it, battle-line speed
// (average and slowest capital ship), cruisers and torpedo craft; plus the design targets and the build mix. Switch: EnemyProfileLog.
// 1.11.374 (#84 step b, author 29.10: "and the AI should not keep deleting designs whenever its enemies change"):
//  - hysteresis on enemies: a war makes an enemy at once; bad relations only after EnemyProfileEnterMonths months in a row; a
//    nation stops being a likely enemy only after EnemyProfileLeaveMonths months without war or bad relations;
//  - hysteresis on targets: the design targets (main calibre, capital speed) and the build mix are fixed when first found and change
//    only on a real shift (calibre by 1 in or more, speed by 2 kn or more, another build mix) and at most once in
//    EnemyProfileTargetMonths months;
//  - the build mix now acts (switch EnemyFitBuildMix): Player.GetShipBuildRatio of an AI great power is multiplied - torpedo craft
//    x1.3 against a slower or poorly screened battle line or a battleship race it would lose, light cruisers x1.2 against many enemy
//    torpedo craft, capital ships x0.85 in a battleship race it would lose. Nothing deletes or replaces existing designs.
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
        internal static MelonPreferences_Entry<bool> On, Mix;
        internal static MelonPreferences_Entry<float> Attitude;
        internal static MelonPreferences_Entry<int> EnterMonths, LeaveMonths, TargetMonths;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("EnemyProfileLog", true, null, "Campaign AI: once a month every AI great power sizes up its likely enemies (at war or bad relations) - their largest guns, battle-line speed, cruisers and torpedo craft - and writes the design targets and build mix it would take (log; the basis for designs fitted against the enemy)");
            Attitude = cat.CreateEntry("EnemyProfileAttitude", -30f, null, "Campaign AI: relations at or below this attitude make a nation a likely enemy (at war: always)");
            Mix = cat.CreateEntry("EnemyFitBuildMix", true, null, "Campaign AI: an AI great power fits its building to its likely enemies - more torpedo craft against a slower or poorly screened battle line or a battleship race it would lose, more light cruisers against many torpedo craft; enemies and targets change slowly (no design is deleted because enemies change)");
            EnterMonths = cat.CreateEntry("EnemyProfileEnterMonths", 6, null, "Campaign AI: months in a row of bad relations before a nation counts as a likely enemy (war: at once)");
            LeaveMonths = cat.CreateEntry("EnemyProfileLeaveMonths", 12, null, "Campaign AI: months without war or bad relations before a nation stops counting as a likely enemy");
            TargetMonths = cat.CreateEntry("EnemyProfileTargetMonths", 12, null, "Campaign AI: shortest time in months between two changes of an AI great power's design targets and build mix");
        }
        static int lastMonth = -1; static float next; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][EnemyProfile] " + m); }

        internal class Profile
        {
            public string Name; public float Weight; public int Capitals, Cruisers, Torpedo; public float MaxCal; public int MaxCalShips;
            public float LineSpeed, SlowestCapital = 99f;
        }
        internal static readonly Dictionary<string, List<Profile>> Last = new Dictionary<string, List<Profile>>(); // AI nation -> enemies (for steps b, c)

        // hysteresis state per AI nation
        class Watch { public int Bad, Good; public bool Enemy; }
        internal class Targets { public float Cal, Speed; public bool Torp, Screen, Race; public int SetMonth = -1; }
        static readonly Dictionary<string, Dictionary<string, Watch>> watch = new Dictionary<string, Dictionary<string, Watch>>();
        internal static readonly Dictionary<string, Targets> Fixed = new Dictionary<string, Targets>();

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
                if (m == lastMonth) return;
                if (lastMonth > 0 && (m < lastMonth || m > lastMonth + 3)) { watch.Clear(); Fixed.Clear(); }   // another campaign loaded
                lastMonth = m;
                var players = cc.CampaignData.Players; if (players == null) return;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i]; if (p == null || p.data == null) continue;
                    bool major = false, ai = false; try { major = p.isMajor; ai = p.isAi; } catch { }
                    if (major && ai) Report(cc, p, m);
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

        static void Report(CampaignController cc, Player p, int month)
        {
            try
            {
                string me = p.data.name;
                if (!watch.TryGetValue(me, out var w)) { w = new Dictionary<string, Watch>(); watch[me] = w; }
                var list = new List<Profile>(); var changes = new List<string>();
                foreach (var kv in cc.CampaignData.Relations)
                {
                    var r = kv.Value; if (r == null || !r.AnyOnePlayerIs(p)) continue;
                    var o = r.OppositePlayer(p); if (o == null || o.data == null || o.Pointer == p.Pointer) continue;
                    bool dis = false, war = false; float att = 0f; try { dis = o.isDisabled; war = r.isWar; att = r.attitude; } catch { }
                    if (dis) { w.Remove(o.data.name); continue; }
                    if (!w.TryGetValue(o.data.name, out var x)) { x = new Watch(); w[o.data.name] = x; }
                    bool bad = war || att <= Attitude.Value;
                    if (bad) { x.Bad++; x.Good = 0; } else { x.Good++; x.Bad = 0; }
                    bool was = x.Enemy;
                    if (!x.Enemy && (war || x.Bad >= EnterMonths.Value)) x.Enemy = true;
                    else if (x.Enemy && !war && x.Good >= LeaveMonths.Value) x.Enemy = false;
                    if (was != x.Enemy) changes.Add($"{Economy.Name(o)} {(x.Enemy ? (war ? "becomes a likely enemy (war)" : $"becomes a likely enemy ({x.Bad} months of bad relations)") : $"is no longer a likely enemy ({x.Good} months without war or bad relations)")}");
                    if (!x.Enemy) continue;
                    list.Add(Of(o, war ? 2f : Mathf.Clamp(-att / 50f, 0.5f, 2f)));
                }
                Last[me] = list;
                if (changes.Count > 0) Log($"{Economy.Name(p)}: " + string.Join("; ", changes));
                if (list.Count == 0) { Log($"{Economy.Name(p)}: no likely enemy (no war, no attitude at or below {Attitude.Value:0} for {EnterMonths.Value} months){(Fixed.ContainsKey(me) ? " - design targets and build mix kept" : "")}"); return; }
                var own = Of(p, 1f);
                var main = list.OrderByDescending(x => x.Weight * Math.Max(1, x.Capitals)).First();
                float wantCal = list.Max(x => x.MaxCal);
                float wantSpeed = list.Where(x => x.LineSpeed > 0f).Select(x => x.LineSpeed).DefaultIfEmpty(0f).Max();
                var mix = new List<string>();
                bool torp = false, screen = false, race = false;
                if (main.Capitals > 0 && own.LineSpeed > 0f && main.LineSpeed > 0f && main.LineSpeed < own.LineSpeed - 1.5f) { mix.Add("more torpedo craft against a slower battle line"); torp = true; }
                if (main.Capitals > 0 && main.Torpedo < main.Capitals * 2) { mix.Add("more torpedo craft - weak enemy screen"); torp = true; }
                if (main.Torpedo > Math.Max(4, own.Torpedo)) { mix.Add("more light cruisers and destroyers as screen"); screen = true; }
                if (main.Capitals > own.Capitals * 1.5f + 1) { mix.Add("torpedo craft instead of a losing battleship race"); torp = true; race = true; }

                // hysteresis on the targets
                string fix;
                if (!Fixed.TryGetValue(me, out var tg))
                {
                    tg = new Targets { Cal = wantCal, Speed = wantSpeed, Torp = torp, Screen = screen, Race = race, SetMonth = month }; Fixed[me] = tg;
                    fix = "targets set";
                }
                else
                {
                    bool shift = Math.Abs(wantCal - tg.Cal) >= 1f || Math.Abs(wantSpeed - tg.Speed) >= 2f || torp != tg.Torp || screen != tg.Screen || race != tg.Race;
                    if (!shift) fix = "targets kept";
                    else if (month - tg.SetMonth < TargetMonths.Value) fix = $"targets kept (a shift is seen, but they changed {month - tg.SetMonth} month(s) ago - at most once in {TargetMonths.Value})";
                    else
                    {
                        fix = $"targets changed (guns {tg.Cal:0.#} -> {wantCal:0.#} in, speed {tg.Speed:0.0} -> {wantSpeed:0.0} kn)";
                        tg.Cal = wantCal; tg.Speed = wantSpeed; tg.Torp = torp; tg.Screen = screen; tg.Race = race; tg.SetMonth = month;
                    }
                }
                Log($"{Economy.Name(p)}: likely enemies " + string.Join("; ", list.Select(x => $"{x.Name} (weight {x.Weight:0.0}: {x.Capitals} capital ships, largest guns {x.MaxCal:0.#} in on {x.MaxCalShips}, line speed {x.LineSpeed:0.0} kn, slowest {x.SlowestCapital:0.0}; {x.Cruisers} cruisers, {x.Torpedo} torpedo craft)"))
                    + $" | own: {own.Capitals} capital ships, largest guns {own.MaxCal:0.#} in, line speed {own.LineSpeed:0.0} kn, {own.Torpedo} torpedo craft"
                    + $" | seen now: main guns at least {wantCal:0.#} in, capital speed at least {wantSpeed:0.0} kn, build mix {(mix.Count > 0 ? string.Join(", ", mix) : "no change")}"
                    + $" | {fix}: guns {tg.Cal:0.#} in, speed {tg.Speed:0.0} kn, build mix{(tg.Torp ? " +torpedo craft" : "")}{(tg.Screen ? " +light cruisers" : "")}{(tg.Race ? " -capital ships" : "")}{(!tg.Torp && !tg.Screen && !tg.Race ? " unchanged" : "")}{(Mix.Value ? "" : " (EnemyFitBuildMix off - log only)")}");
            }
            catch (Exception e) { if (errors++ < 5) Log($"{p.data?.name}: {e.Message}"); }
        }

        // build-ratio factor for an AI great power and a ship type (Patch_GetShipBuildRatio)
        internal static float BuildFactor(Player p, string shipType)
        {
            try
            {
                if (Mix == null || !Mix.Value || On == null || !On.Value || p == null || p.data == null) return 1f;
                if (!Fixed.TryGetValue(p.data.name, out var tg)) return 1f;
                float f = 1f;
                if ((shipType == "dd" || shipType == "tb") && tg.Torp) f *= 1.3f;
                if (shipType == "cl" && tg.Screen) f *= 1.2f;
                if ((shipType == "bb" || shipType == "bc") && tg.Race) f *= 0.85f;
                return f;
            }
            catch { return 1f; }
        }
    }
}
