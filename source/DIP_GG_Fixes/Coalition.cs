// 1.11.291 (#40, author's decision 25.10, 13:30): coalition wars with a balance of power.
// Piling on a nation that is in a serious war stays - it is how wars spread - but every great power not yet in the war
// takes one decision per month, with the reason in the log:
//   join      - the opportunist: hostile to the target ("almost war", attitude at or below CoalitionAttitude); the chance grows
//               with the hostility and with how badly the target is doing (victory points of its wars) and falls to
//               CoalitionSaturatedFactor once the coalition already outweighs the target (CoalitionPowerRatio);
//   other side - balance of power: when the coalition outweighs the target by more than BalanceSideRatio and the power fears the
//               coalition's leader more than the target (allied to the target, or attitude to the leader lower by BalanceAttitudeGap),
//               it declares war on the leader (Britain against a continental hegemon; France and Britain for the Ottomans, 1854);
//   wait       - small navies (below CoalitionMinShare of the target's fleet) and mixed relations stay out; when the target is
//               clearly losing (score above LateJoinScore) they may join the winner late (Italy 1915, Romania 1916);
//   neutral    - at war elsewhere (CoalitionMaxWars), bankrupt, good relations with both, or a peace with this nation within
//               CoalitionPeaceMonths.
// The peace cool-down is saved with the economy state ("C|" lines). One line per month counts the wars of every nation and the
// rule that started each war of the mod.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class CoalitionWar
    {
        internal static MelonPreferences_Entry<float> PeaceMonths, Saturated, MinShare, SideRatio, SideChance, SideGap, LateScore, LateChance;
        internal static MelonPreferences_Entry<bool> Balance;
        static readonly System.Random rng = new System.Random();
        // pair key "a~b" (sorted nation ids) -> month of the last peace between them
        internal static readonly Dictionary<string, int> peace = new Dictionary<string, int>();
        // wars seen last month (pair keys) and the rule of the mod that started a war (session only)
        static readonly HashSet<string> lastWars = new HashSet<string>();
        static readonly Dictionary<string, string> startedBy = new Dictionary<string, string>();
        static bool seeded; static int lastRunMonth = -1;

        internal static void Create(MelonPreferences_Category cat)
        {
            Balance = cat.CreateEntry("CoalitionBalanceOfPower", true, null, "Coalition wars with a balance of power (#40): every great power outside a coalition war decides each month to join, take the target's side, wait or stay neutral, with the reason in the log. Off: the 1.11.37 rule (every hostile power joins with CoalitionJoinChance)");
            PeaceMonths = cat.CreateEntry("CoalitionPeaceMonths", 12f, null, "After a peace with a nation, a great power does not join a coalition war against it (or take its side against the coalition) for this many months");
            Saturated = cat.CreateEntry("CoalitionSaturatedFactor", 0.2f, null, "Join chance factor once the coalition already outweighs the target (fleet power at or above CoalitionPowerRatio x the target's)");
            MinShare = cat.CreateEntry("CoalitionMinShare", 0.25f, null, "A navy below this share of the target's fleet power waits instead of joining at once (it may join the winner late)");
            SideRatio = cat.CreateEntry("BalanceSideRatio", 2.0f, null, "Coalition fleet power, as a multiple of the target's, above which powers that fear the coalition's leader may take the target's side");
            SideChance = cat.CreateEntry("BalanceSideChance", 0.2f, null, "Chance per month that such a power declares war on the coalition's leader (an ally of the target: twice this)");
            SideGap = cat.CreateEntry("BalanceAttitudeGap", 20f, null, "A power fears the coalition's leader more than the target when its attitude to the leader is lower by at least this much");
            LateScore = cat.CreateEntry("LateJoinScore", 0.4f, null, "War score of the target (-1 winning ... +1 losing, from the victory points of its wars) above which waiting powers may join the winning side");
            LateChance = cat.CreateEntry("LateJoinChance", 0.15f, null, "Chance per month that a waiting power joins the winning side once the target is clearly losing");
        }

        internal static void Reset() { peace.Clear(); lastWars.Clear(); startedBy.Clear(); seeded = false; lastRunMonth = -1; }
        internal static bool Parse(string line)
        {
            if (line == null || !line.StartsWith("C|")) return false;
            var a = line.Split('|'); int m; if (a.Length >= 3 && int.TryParse(a[2], out m)) peace[a[1]] = m;
            return true;
        }
        internal static IEnumerable<string> Serialize() { foreach (var kv in peace) yield return "C|" + kv.Key + "|" + kv.Value; }

        static string Id(Player p) { return Economy.Key(p); }
        static string Nm(Player p) { return Economy.Name(p); }
        static string Pair(Player a, Player b) { string x = Id(a), y = Id(b); return string.CompareOrdinal(x, y) < 0 ? x + "~" + y : y + "~" + x; }
        static int Month() { return Economy.Month(); }
        static float Fleet(Player p) { try { return p.TotalPowerProjection(); } catch { return 0f; } }
        static Relation Rel(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && a.Pointer != b.Pointer && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return r; } } catch { }
            return null;
        }
        static bool AtWar(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isWar; } catch { return false; } }
        static float Att(Player a, Player b) { var r = Rel(a, b); try { return r != null ? r.attitude : 0f; } catch { return 0f; } }
        static bool Ally(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isAlliance; } catch { return false; } }
        static int Wars(Player p)
        {
            int c = 0;
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.isWar && r.AnyOnePlayerIs(p)) c++; } } catch { }
            return c;
        }
        // +1: the target is losing all its wars on victory points, -1: winning
        static float Score(Player t)
        {
            float tv = 0f, ev = 0f;
            try
            {
                foreach (var kv in CampaignController.Instance.CampaignData.Relations)
                {
                    var r = kv.Value; if (r == null) continue;
                    try { if (!r.isWar || !r.AnyOnePlayerIs(t)) continue; tv += Math.Max(0f, r.PlayerVictoryPoints(t)); ev += Math.Max(0f, r.EnemyOfPlayerVictoryPoints(t)); } catch { }
                }
            }
            catch { }
            return (ev - tv) / (ev + tv + 100f);
        }
        static bool Cooling(Player a, Player b, int month, out int since)
        {
            since = 0; int m;
            if (!peace.TryGetValue(Pair(a, b), out m)) return false;
            since = month - m; return since < (PeaceMonths != null ? PeaceMonths.Value : 12f);
        }
        static bool Broke(Player p, int month)
        {
            try { var st = Economy.Get(p); return p.cash < 0f || (st != null && st.BankruptUntil > month); } catch { return false; }
        }
        static void Declare(Player a, Player t, string rule, string why)
        {
            Economy.DeclareWarPublic(a, t, why);
            if (AtWar(a, t)) startedBy[Pair(a, t)] = rule;
        }

        // wars that ended since last month become peace dates; one line with the wars of every nation
        static void TrackWars(List<Player> majors, int month)
        {
            var now = new HashSet<string>();
            var counts = new Dictionary<string, List<string>>();
            for (int i = 0; i < majors.Count; i++)
                for (int j = i + 1; j < majors.Count; j++)
                {
                    var a = majors[i]; var b = majors[j];
                    if (!AtWar(a, b)) continue;
                    string k = Pair(a, b); now.Add(k);
                    string rule; if (!startedBy.TryGetValue(k, out rule)) rule = "game";
                    foreach (var x in new[] { a, b })
                    {
                        var y = x.Pointer == a.Pointer ? b : a;
                        List<string> l; if (!counts.TryGetValue(Nm(x), out l)) counts[Nm(x)] = l = new List<string>();
                        l.Add(Nm(y) + (rule == "game" ? "" : " (" + rule + ")"));
                    }
                }
            if (seeded) foreach (var k in lastWars) if (!now.Contains(k)) { peace[k] = month; startedBy.Remove(k); MelonLogger.Msg($"[DIP GG Fixes][War] peace: {k.Replace("~", " - ")} (no coalition war between them for {PeaceMonths.Value:0} months)"); }
            lastWars.Clear(); foreach (var k in now) lastWars.Add(k); seeded = true;
            var many = counts.Where(kv => kv.Value.Count >= 2).OrderByDescending(kv => kv.Value.Count).ToList();
            if (many.Count > 0) MelonLogger.Msg("[DIP GG Fixes][War] wars this month: " + string.Join("; ", many.Select(kv => $"{kv.Key} {kv.Value.Count} ({string.Join(", ", kv.Value)})")));
        }

        // returns false when the old rule should run instead
        internal static bool Run()
        {
            if (Balance == null || !Balance.Value) return false;
            int month = Month();
            if (month == lastRunMonth) return true; // once per month
            lastRunMonth = month;
            try
            {
                var majors = Economy.MajorsPublic().Where(x => { try { return !x.isDisabled; } catch { return false; } }).ToList();
                TrackWars(majors, month);
                foreach (var t in majors) { try { Target(t, majors, month); } catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][War] coalition against {Nm(t)}: {e.Message}"); } }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][War] coalitions: " + e.Message); }
            return true;
        }

        static void Target(Player t, List<Player> majors, int month)
        {
            float tf = Fleet(t); if (tf <= 0f) return;
            var atWar = new List<Player>(); var hostile = new List<Player>(); float sumW = 0f, sumH = 0f;
            foreach (var o in majors)
            {
                if (o.Pointer == t.Pointer) continue;
                var r = Rel(t, o); if (r == null) continue;
                bool war = false, almost = false; float att = 0f;
                try { war = r.isWar; almost = r.AlmostWar(); att = r.attitude; } catch { continue; }
                if (war) { atWar.Add(o); sumW += Fleet(o); }
                else if (almost && att <= EcoPrefs.CoalAttitude.Value) { hostile.Add(o); sumH += Fleet(o); }
            }
            int maxWars = (int)EcoPrefs.CoalMaxWars.Value;
            if (atWar.Count == 0)
            {
                // the start of a coalition: as 1.11.37/38 (strong enough together, the target stronger than each of them), plus the peace cool-down
                if (hostile.Count == 0 || sumH < EcoPrefs.CoalRatio.Value * tf) return;
                if (tf <= hostile.Max(Fleet)) return;
                int since;
                var cands = hostile.Where(o => !o.isMain && !Broke(o, month) && Wars(o) < maxWars && !Cooling(o, t, month, out since)).OrderByDescending(Fleet).ToList();
                if (cands.Count == 0) return;
                string coal = string.Join(", ", hostile.Select(Nm));
                if (rng.NextDouble() < EcoPrefs.CoalStart.Value) Declare(cands[0], t, "coalition", $"leads the coalition ({coal}: fleet power x{sumH / tf:F2} of {Nm(t)})");
                else MelonLogger.Msg($"[DIP GG Fixes][War] coalition against {Nm(t)} ready ({coal}: fleet power x{sumH / tf:F2}) - {Nm(cands[0])} did not declare war this turn");
                return;
            }
            // a war against t is on. Joining (and a late join) only in a coalition situation, as before: the target is stronger at sea
            // than every single enemy or hostile power, and they are strong enough together. Taking the target's side applies to
            // any pile-up of two or more enemies that outweighs the target by BalanceSideRatio.
            float ratio = sumW / tf, score = Score(t);
            bool dominant = tf > atWar.Concat(hostile).Max(Fleet);
            bool coalition = dominant && sumW + sumH >= EcoPrefs.CoalRatio.Value * tf;
            if (!coalition && !(atWar.Count >= 2 && ratio > SideRatio.Value)) return;
            var leader = atWar.OrderByDescending(Fleet).First();
            var dec = new List<string>();
            foreach (var o in majors)
            {
                if (o.Pointer == t.Pointer || atWar.Any(w => w.Pointer == o.Pointer)) continue;
                if (o.isMain) continue; // the player decides his own wars
                float of = Fleet(o), attT = Att(o, t), attL = Att(o, leader);
                bool allyT = Ally(o, t), allyL = Ally(o, leader), hostileT = hostile.Any(h => h.Pointer == o.Pointer);
                int since;
                if (Wars(o) >= maxWars) { if (hostileT || allyT) dec.Add($"{Nm(o)} neutral (at war elsewhere)"); continue; }
                if (Broke(o, month)) { if (hostileT || allyT) dec.Add($"{Nm(o)} neutral (bankrupt)"); continue; }
                if (Cooling(o, t, month, out since)) { if (hostileT || allyT) dec.Add($"{Nm(o)} neutral (peace with {Nm(t)} {since} months ago)"); continue; }
                // balance of power: take the target's side against the leader
                if (ratio > SideRatio.Value && leader.Pointer != o.Pointer && !allyL && !AtWar(o, leader) && !hostileT
                    && (allyT || attL <= attT - SideGap.Value) && !Cooling(o, leader, month, out since))
                {
                    float ch = SideChance.Value * (allyT ? 2f : 1f);
                    string why = allyT ? $"allied to {Nm(t)}" : $"fears {Nm(leader)} more than {Nm(t)}: attitude {attL:F0} vs {attT:F0}";
                    if (rng.NextDouble() < ch) { Declare(o, leader, "balance", $"takes the side of {Nm(t)} against the coalition led by {Nm(leader)} (coalition fleet power x{ratio:F2} of {Nm(t)}; {why})"); dec.Add($"{Nm(o)} takes {Nm(t)}'s side ({why})"); }
                    else dec.Add($"{Nm(o)} considers {Nm(t)}'s side ({why}; not this month)");
                    continue;
                }
                if (!coalition) continue;
                if (!hostileT)
                {
                    // mixed relations: wait; a clearly losing target draws a late join from a power that dislikes it
                    if (score > LateScore.Value && attT < 0f && attL >= attT && !allyT)
                    {
                        if (rng.NextDouble() < LateChance.Value) { Declare(o, t, "late", $"joins the winning side late against {Nm(t)} (war score {score:+0.00;-0.00}, attitude {attT:F0})"); dec.Add($"{Nm(o)} joins the winner late"); }
                    }
                    else if (attT > 0f && attL > 0f) { /* good relations with both: neutral, not logged */ }
                    continue;
                }
                // opportunist: hostile to the target
                if (of < MinShare.Value * tf)
                {
                    if (score > LateScore.Value && rng.NextDouble() < LateChance.Value) { Declare(o, t, "late", $"joins the winning side late against {Nm(t)} (small navy x{of / tf:F2}; war score {score:+0.00;-0.00})"); dec.Add($"{Nm(o)} joins the winner late (small navy)"); }
                    else dec.Add($"{Nm(o)} waits (small navy x{of / tf:F2}{(score > LateScore.Value ? "" : ", target not losing")})");
                    continue;
                }
                float attF = 0.5f + 0.5f * Math.Min(1f, Math.Max(0f, (EcoPrefs.CoalAttitude.Value - attT) / Math.Max(1f, 100f + EcoPrefs.CoalAttitude.Value)));
                float lossF = 1f + Math.Max(-0.5f, score);
                float satF = ratio < EcoPrefs.CoalRatio.Value ? 1f : Saturated.Value;
                float chance = Math.Min(0.95f, EcoPrefs.CoalJoin.Value * attF * lossF * satF);
                if (rng.NextDouble() < chance) { Declare(o, t, "coalition", $"joins the war against {Nm(t)} ({string.Join(", ", atWar.Select(Nm))} already fighting; coalition fleet power x{ratio:F2}; chance {chance:P0})"); dec.Add($"{Nm(o)} joins (chance {chance:P0})"); }
                else dec.Add($"{Nm(o)} waits (join chance {chance:P0}{(satF < 1f ? ", coalition already stronger" : "")})");
            }
            if (dec.Count > 0) MelonLogger.Msg($"[DIP GG Fixes][War] coalition against {Nm(t)} (at war with {string.Join(", ", atWar.Select(Nm))}; fleet power x{ratio:F2}, leader {Nm(leader)}, war score {score:+0.00;-0.00}): {string.Join("; ", dec)}");
        }
    }
}
