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
// 1.11.247 (author's decision 25.10): a lost colony weighs half of a core province, a jewel in the crown (the most developed
// quarter of the world's provinces - India) three quarters; the weights drive the relation loss, the war decision (weight >= 1)
// and the order in which provinces are fought for and returned.
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
        internal static MelonPreferences_Entry<float> ColonyWeight, JewelWeight, JewelShare;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            // 1.11.247 (author's decision 25.10): a colony weighs half of a core province, a "jewel in the crown" (India) three quarters
            ColonyWeight = cat.CreateEntry("LostTerritoryColonyWeight", 0.5f, null, "Lost territory: weight of a lost colony (a province held at the campaign start that is not a national core) against a core province (1)");
            JewelWeight = cat.CreateEntry("LostTerritoryJewelWeight", 0.75f, null, "Lost territory: weight of a lost colony among the most developed provinces of the world (a jewel in the crown such as India)");
            JewelShare = cat.CreateEntry("LostTerritoryJewelShare", 0.25f, null, "Lost territory: share of the world's provinces, by development, that count as jewels (0.25 = the top quarter)");
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
        static int logged;
        // a lost province held by the holder next to the claimant's own land (its army can attack it - Pre_TryStartBattle)
        static bool LandRoad(Player w, List<Province> lost)
        {
            try
            {
                string wn = Key(w);
                foreach (var t in lost)
                {
                    var nb = t?.NeighbourProvinces; if (nb == null) continue;
                    foreach (var n in nb) { string c = null; try { c = n.Controller; } catch { } if (c == wn) return true; }
                }
            }
            catch { }
            return false;
        }
        static IEnumerable<Player> AllMajors()
        {
            var l = new List<Player>();
            try { var m = CampaignController.Instance.CampaignData.PlayersMajor; for (int i = 0; i < m.Count; i++) if (m[i] != null) l.Add(m[i]); } catch { }
            return l;
        }
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
        static float Worth(Province pr) { float v = 0f; try { v = pr.HavePort ? 1000f + pr.Port : 0f; } catch { } try { v += pr.Development; } catch { } return v * Weight(pr, null); }

        // 1.11.247: core province 1, colony 0.5, jewel in the crown 0.75 (the most developed quarter of the world's provinces)
        static float jewelFloor = -1f; static int jewelMonth = -1;
        static float JewelFloor()
        {
            int m = Month(); if (jewelFloor >= 0f && m == jewelMonth) return jewelFloor;
            jewelMonth = m;
            try
            {
                var dev = new List<float>();
                foreach (var kv in CampaignMap.Instance.Provinces.ProvinceById) { var pr = kv.Value; if (pr == null) continue; float d = 0f; try { d = pr.Development; } catch { } dev.Add(d); }
                if (dev.Count == 0) { jewelFloor = float.MaxValue; return jewelFloor; }
                dev.Sort(); float share = Math.Max(0.01f, Math.Min(1f, JewelShare != null ? JewelShare.Value : 0.25f));
                int idx = Math.Max(0, Math.Min(dev.Count - 1, (int)Math.Floor(dev.Count * (1f - share))));
                jewelFloor = dev[idx];
            }
            catch { jewelFloor = float.MaxValue; }
            return jewelFloor;
        }
        internal static float Weight(Province pr, string nation)
        {
            try
            {
                string core; if (Independence.Cores.TryGetValue(pr.Id, out core) && (nation == null || core == nation)) return 1f;
                float d = 0f; try { d = pr.Development; } catch { }
                if (d >= JewelFloor()) return JewelWeight != null ? JewelWeight.Value : 0.75f;
                return ColonyWeight != null ? ColonyWeight.Value : 0.5f;
            }
            catch { return 1f; }
        }
        static string Kind(Province pr, string nation) { float w = Weight(pr, nation); return w >= 1f ? "" : w >= 0.7f ? " (jewel)" : " (colony)"; }

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
                        string wn = Key(w); float weight = 0f; foreach (var pr in kv.Value) weight += Weight(pr, wn);   // 1.11.247: cores 1, colonies 0.5, jewels 0.75
                        float d = -(RelMonthly != null ? RelMonthly.Value : 1.5f) * Math.Min(4f, weight) * (ally ? 0.5f : 1f);
                        var r = Rel(w, h); if (r != null && !war) { try { Occupation.Adjust(r, d, $"{N(h)} holds {kv.Value.Count} province(s) of {N(w)}"); } catch { } }
                        float att = 0f; try { att = r != null ? r.attitude : 0f; } catch { }
                        string state = war ? "at war" : ally ? "ally" : $"attitude {att:F0}";
                        parts.Add($"{N(h)}: {string.Join(", ", kv.Value.Select(x => x.Id + Kind(x, wn)).Take(4))}{(kv.Value.Count > 4 ? " ..." : "")} (weight {weight:0.##}, {state})");
                        if (!w.isAi || war || ally) continue;
                        if (weight < 1f) continue;   // 1.11.247: a single colony is no cause for war; two colonies or one core province are
                        if (Portless.IsWaiting(w)) continue;   // the Portless rule declares its own wars
                        bool hWar = false; try { hWar = h.IsAtWar(); } catch { }
                        bool wWar = false; try { wWar = w.IsAtWar(); } catch { }
                        float fw = Fleet(w), fh = Fleet(h);
                        float need = WarPower != null ? WarPower.Value : 1.2f; float minAtt = Attitude != null ? Attitude.Value : -40f;
                        // 1.11.430 (hurepoix, forum 30.10: "already at war, in recession, with 1/10 my GDP and 1/100 my fleet, it goes to war
                        // anyway"): joining a war the holder is already fighting needs a way to win the territory back - a land road (a
                        // lost province next to its own land, which its army can attack) or, at sea, its fleet together with the
                        // holder's other enemies at least LostTerritoryWarPower times the holder's; a nation already at war
                        // elsewhere joins only by land
                        float coal = fw; try { foreach (var o in AllMajors()) { if (o == null || o.Pointer == w.Pointer || o.Pointer == h.Pointer) continue; if (AtWar(o, h)) coal += Fleet(o); } } catch { }
                        bool road = LandRoad(w, kv.Value);
                        bool canWin = road || (!wWar && fh > 0f && coal >= need * fh);
                        if (hWar && att <= minAtt && fw > 0f && !canWin)
                        {
                            if (logged++ < 20) Log($"{N(w)} does not join the war against {N(h)}: no land road to its lost provinces and its fleet{(coal > fw ? " with the holder's other enemies" : "")} is x{(fh > 0f ? coal / fh : 0f):F2} of {N(h)}'s (needs x{need:F2}{(wWar ? ", and it is at war already" : "")})");
                        }
                        else if (hWar && att <= minAtt && fw > 0f)
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
