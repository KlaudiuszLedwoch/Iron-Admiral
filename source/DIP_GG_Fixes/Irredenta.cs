// 1.11.235 (author's decision 24.10): lost territory is not forgotten. In the 1923 campaign Austria-Hungary came back with two
// provinces and no port and sat there; the author: a restored great power - any great power - must try to win back the
// territory it held, as every state of the period did (Italy's irredenta, France and Alsace-Lorraine, Bulgaria 1913-15).
// Lost territory = the provinces a nation controlled at the campaign start (Province.InitialController) plus its national
// cores (Independence.Cores) that another nation holds now. Every month, for every great power with lost territory:
//  - relations: the holder is thought worse of (a national grievance; half as much when the holder is an ally);
//  - war (AI): it joins a war the holder is already fighting, or - when its fleet power is at least LostTerritoryWarPower
//    times the holder's and the attitude is already hostile - it declares war itself (Economy.DeclareWar);
//  - land offensive (AI and player): at war with the holder, its army attacks a lost province next to its own land
//    (ProvinceBattleManager.TryStartBattle prefix, after the Portless rule for powers without ports);
//  - peace terms: a won war returns up to LostTerritoryPeaceMax lost provinces (ports first) from the loser.
// Switch: LostTerritoryReconquest (F10, Campaign AI). Works beside Portless (powers without ports) and Occupation.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Irredenta
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> RelMonthly, WarPower, Attitude;
        internal static MelonPreferences_Entry<int> PeaceMax;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("LostTerritoryReconquest", true, null, "Campaign: a great power tries to win back the provinces it held at the campaign start and its national cores - worse relations with the holder, war when it is strong enough or the holder is at war already, land offensives into lost provinces, lost provinces returned at peace");
            RelMonthly = cat.CreateEntry("LostTerritoryRelationMonthly", 1.5f, null, "Attitude lost every month towards a nation holding one's lost province (per province, at most 4 provinces counted; half towards an ally)");
            WarPower = cat.CreateEntry("LostTerritoryWarPower", 1.2f, null, "An AI great power declares war on the holder of its lost territory when its fleet power is at least this many times the holder's (and the attitude is below LostTerritoryWarAttitude)");
            Attitude = cat.CreateEntry("LostTerritoryWarAttitude", -40f, null, "Attitude below which the war for lost territory may be declared");
            PeaceMax = cat.CreateEntry("LostTerritoryPeaceMax", 2, null, "Lost provinces returned by a won war at the peace treaty (ports first)");
        }
        static bool Active { get { return On != null && On.Value; } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][LostTerritory] " + m); }
        static string N(Player p) { try { return Economy.Name(p); } catch { return "?"; } }
        static string Key(Player p) { try { return p.data.name; } catch { return "?"; } }
        static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }
        static Player ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.data != null && p.data.name == name) return p; } } catch { }
            return null;
        }
        static Relation Rel(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b) && a.Pointer != b.Pointer) return r; } } catch { }
            return null;
        }
        static bool Allied(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isAlliance; } catch { return false; } }
        static bool AtWar(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isWar; } catch { return false; } }
        static float Fleet(Player p) { try { return p.TotalPowerProjection(); } catch { return 0f; } }

        // lost provinces of a nation: held by another nation now, were the nation's at the campaign start or are its national cores
        internal static List<Province> Lost(Player w)
        {
            var r = new List<Province>(); string wn = Key(w);
            try
            {
                var byId = CampaignMap.Instance.Provinces.ProvinceById;
                foreach (var kv in byId)
                {
                    var pr = kv.Value; if (pr == null) continue;
                    string ctrl = null; try { ctrl = pr.Controller; } catch { }
                    if (string.IsNullOrEmpty(ctrl) || ctrl == wn) continue;
                    bool mine = false;
                    try { mine = pr.InitialController != null && pr.InitialController.name == wn; } catch { }
                    if (!mine) { string core; mine = Independence.Cores.TryGetValue(kv.Key, out core) && core == wn; }
                    if (!mine) continue;
                    var h = ByName(ctrl); if (h == null || h.isDisabled) continue;   // ungoverned land goes to Successors
                    r.Add(pr);
                }
            }
            catch (Exception e) { Log("lost: " + e.Message); }
            return r;
        }
        static float Worth(Province pr) { float v = 0f; try { v = pr.HavePort ? 1000f + pr.Port : 0f; } catch { } try { v += pr.Development; } catch { } return v; }

        // ---- monthly: relations and war ----
        static int lastMonth = -1;
        internal static void Tick()
        {
            if (!Active) return;
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (battle) return;
            int m = Month(); if (m <= 0 || m == lastMonth) return; lastMonth = m;
            try
            {
                var majors = CampaignController.Instance.CampaignData.PlayersMajor;
                for (int i = 0; i < majors.Count; i++)
                {
                    var w = majors[i]; if (w == null || w.isDisabled) continue;
                    var lost = Lost(w); if (lost.Count == 0) continue;
                    var byHolder = new Dictionary<string, List<Province>>();
                    foreach (var pr in lost) { List<Province> l; if (!byHolder.TryGetValue(pr.Controller, out l)) { l = new List<Province>(); byHolder[pr.Controller] = l; } l.Add(pr); }
                    var parts = new List<string>();
                    foreach (var kv in byHolder)
                    {
                        var h = ByName(kv.Key); if (h == null) continue;
                        bool ally = Allied(w, h), war = AtWar(w, h);
                        float d = -(RelMonthly != null ? RelMonthly.Value : 1.5f) * Math.Min(4, kv.Value.Count) * (ally ? 0.5f : 1f);
                        var r = Rel(w, h); if (r != null && !war) { try { Occupation.Adjust(r, d, $"{N(h)} holds {kv.Value.Count} province(s) of {N(w)}"); } catch { } }
                        float att = 0f; try { att = r != null ? r.attitude : 0f; } catch { }
                        string state = war ? "at war" : ally ? "ally" : $"attitude {att:F0}";
                        parts.Add($"{N(h)}: {string.Join(", ", kv.Value.Select(x => x.Id).Take(4))}{(kv.Value.Count > 4 ? " ..." : "")} ({state})");
                        if (!w.isAi || war || ally) continue;
                        if (Portless.IsWaiting(w)) continue;   // the Portless rule declares its own wars
                        bool hWar = false; try { hWar = h.IsAtWar(); } catch { }
                        bool wWar = false; try { wWar = w.IsAtWar(); } catch { }
                        float fw = Fleet(w), fh = Fleet(h);
                        float need = WarPower != null ? WarPower.Value : 1.2f; float minAtt = Attitude != null ? Attitude.Value : -40f;
                        if (hWar && att <= minAtt && fw > 0f)
                            Economy.DeclareWarPublic(w, h, $"joins the war against {N(h)} to win back {kv.Value[0].Id}{(kv.Value.Count > 1 ? $" and {kv.Value.Count - 1} more province(s)" : "")}");
                        else if (!wWar && att <= minAtt && fh > 0f && fw >= need * fh)
                            Economy.DeclareWarPublic(w, h, $"declares war on {N(h)} to win back {kv.Value[0].Id}{(kv.Value.Count > 1 ? $" and {kv.Value.Count - 1} more province(s)" : "")} (fleet power x{fw / fh:F2})");
                    }
                    Log($"{N(w)} claims {lost.Count} lost province(s) - {string.Join("; ", parts)}");
                }
            }
            catch (Exception e) { Log("monthly: " + e.Message); }
        }

        // ---- land offensive: at war with the holder, attack a lost province next to own land ----
        static bool Pre_TryStartBattle(Player player)
        {
            if (!Active || player == null) return true;
            try
            {
                if (!player.isMajor || player.isDisabled || Portless.IsWaiting(player)) return true;
                string wn = Key(player); Province from = null, target = null; float best = -1f;
                foreach (var own in player.provinces)
                {
                    if (own == null || ProvinceBattleManager.InBattle(own)) continue;
                    var nb = own.NeighbourProvinces; if (nb == null) continue;
                    foreach (var t in nb)
                    {
                        if (t == null || ProvinceBattleManager.InBattle(t)) continue;
                        string ctrl = null; try { ctrl = t.Controller; } catch { }
                        if (string.IsNullOrEmpty(ctrl) || ctrl == wn) continue;
                        bool mine = false; try { mine = t.InitialController != null && t.InitialController.name == wn; } catch { }
                        if (!mine) { string core; mine = Independence.Cores.TryGetValue(t.Id, out core) && core == wn; }
                        if (!mine) continue;
                        var h = ByName(ctrl); if (h == null || !AtWar(player, h)) continue;
                        float v = Worth(t); if (v > best) { best = v; from = own; target = t; }
                    }
                }
                if (target == null) return true;
                ProvinceBattleManager.StartBattle(from, target, true);
                Log($"land offensive: {N(player)} attacks {target.Id} ({target.Controller}) from {from.Id} to win back lost territory");
                return false;
            }
            catch (Exception e) { Log("land offensive: " + e.Message); return true; }
        }

        // ---- peace: a won war returns lost provinces ----
        static bool busy;
        static void Post_Peace(Relation relation, bool agree)
        {
            if (!Active || relation == null || busy) return;
            try
            {
                bool war = true; try { war = relation.isWar; } catch { }
                if (war) return;
                Player winner = null; try { winner = relation.WhoWins(); } catch { }
                if (winner == null || !winner.isMajor) return;
                var loser = relation.OppositePlayer(winner); if (loser == null) return;
                if (Portless.IsWaiting(winner)) return;   // Portless returns the core port itself
                string ln = Key(loser);
                var take = Lost(winner).Where(p => p.Controller == ln).OrderByDescending(Worth).Take(Math.Max(0, PeaceMax != null ? PeaceMax.Value : 2)).ToList();
                if (take.Count == 0) return;
                busy = true;
                try
                {
                    foreach (var pr in take)
                    {
                        try { CampaignController.Instance.TransferProvinceToNewOwner(winner, pr.Id, true, false, "peace terms - lost territory"); }
                        catch (Exception e) { Log($"transfer of {pr.Id} failed: " + e.Message); }
                    }
                }
                finally { busy = false; }
                string ids = string.Join(", ", take.Select(x => x.Id));
                Log($"peace between {N(winner)} and {N(loser)}: {N(winner)} won and gets back {ids}");
                if (winner.isMain || loser.isMain) { try { EconomyUi.Notice("Lost territory returned", $"Peace terms: {N(loser)} returns {ids} to {N(winner)}."); } catch { } }
            }
            catch (Exception e) { Log("peace: " + e.Message); }
        }

        internal static void Apply(HarmonyLib.Harmony h)
        {
            try { h.Patch(AccessTools.Method(typeof(CampaignController), "PeaceTreaty", new[] { typeof(Relation), typeof(bool) }), null, new HarmonyMethod(typeof(Irredenta), nameof(Post_Peace))); } catch (Exception e) { Log("patch peace: " + e.Message); }
            try { h.Patch(AccessTools.Method(typeof(ProvinceBattleManager), "TryStartBattle", new[] { typeof(Player) }), new HarmonyMethod(typeof(Irredenta), nameof(Pre_TryStartBattle)) { priority = HarmonyLib.Priority.Low }, null); } catch (Exception e) { Log("patch battle: " + e.Message); }
        }
    }
}
