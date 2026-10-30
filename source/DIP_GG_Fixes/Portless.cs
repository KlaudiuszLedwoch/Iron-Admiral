// 1.11.72: great powers that lose all their ports wait for a chance to regain them instead of being dissolved.
// Game code (CampaignController.CheckForCampaignEnd): a great power that controls no province with a port is dissolved
// (Player.CanDissolve -> DisablePlayer) or, for the player, the campaign ends (FinishCampaign, TotalDefeat).
// With PortlessPowersWait the nation stays in the game as long as it still has land provinces or ships:
// - its ships are based in allied ports (the last port's fleet goes to the nearest allied port instead of the conqueror);
// - it builds no ships while it has no port;
// - when it makes peace and has no war left: a won war returns one of its national core ports (peace terms), otherwise
//   its fleet goes to an ally (without an ally - to the controller of the port where the ships lie);
// - a core port taken by an ally is handed back to it;
// - in the next war of the power holding its core ports it joins the war (AI) and its army attacks the core port
//   provinces next to its own provinces (land offensive, player and AI);
// - the risings of occupied core provinces (Independence) keep working.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Portless
    {
        internal static MelonPreferences_Entry<bool> On;
        static readonly HashSet<string> logOnce = new HashSet<string>();
        static readonly List<KeyValuePair<string, string>> pendingReturns = new List<KeyValuePair<string, string>>();
        static int lastMonth = -1; static bool busy;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PortlessPowersWait", true, null, "A great power that loses all its ports is not dissolved: its fleet uses allied ports, it waits for a rising, a land offensive, peace terms or an ally to give it a port back, and joins the next war against the power holding its ports");
        }
        static bool Active { get { return On != null && On.Value; } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Portless] " + m); }
        static void Once(string k, string m) { if (logOnce.Add(k)) Log(m); }
        static string N(Player p) { try { return Economy.Name(p); } catch { return "?"; } }
        static string Key(Player p) { try { return p.data.name; } catch { return "?"; } }
        static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }

        static bool Exists(Ship s)
        {
            if (s == null) return false;
            try
            {
                var st = s.status;
                return !(st == VesselEntity.Status.Building || st == VesselEntity.Status.Commissioning || st == VesselEntity.Status.Sunk || st == VesselEntity.Status.Scrapped || st == VesselEntity.Status.Erased);
            }
            catch { return false; }
        }
        static List<Ship> Ships(Player p)
        {
            var r = new List<Ship>();
            try { var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var s = en.Current; if (Exists(s)) r.Add(s); } } catch { }
            return r;
        }
        static int Provinces(Player p) { try { return p.provinces != null ? p.provinces.Count : 0; } catch { return 0; } }
        static bool HavePorts(Player p) { try { return p.HavePorts; } catch { return true; } }
        static bool Disabled(Player p) { try { return p.isDisabled; } catch { return true; } }
        internal static bool IsWaiting(Player p) { return Active && p != null && p.isMajor && !Disabled(p) && !HavePorts(p); }

        static Player ByName(string name)
        {
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.data != null && p.data.name == name) return p; } } catch { }
            return null;
        }
        static Relation Rel(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return r; } } catch { }
            return null;
        }
        static bool Allied(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isAlliance; } catch { return false; } }
        static bool AtWar(Player a, Player b) { var r = Rel(a, b); try { return r != null && r.isWar; } catch { return false; } }

        // ---------------------------------------------------------------- 1. not dissolved
        // called from Patch_CanDissolve and the FinishCampaign prefix: true = keep the nation
        internal static bool KeepAlive(Player p)
        {
            if (!Active || p == null || !p.isMajor || HavePorts(p)) return false;
            int prov = Provinces(p), ships = Ships(p).Count;
            if (prov == 0 && ships == 0) { Once("gone|" + Key(p), $"{N(p)} has no ports, no provinces and no ships left - the game may dissolve it"); return false; }
            Once("wait|" + Key(p) + "|" + Month(), $"{N(p)} has lost all its ports - not dissolved, waits to regain them ({prov} province(s), {ships} ship(s))");
            return true;
        }
        static void Notice(string h, string m) { try { EconomyUi.Notice(h, m); } catch { } }

        // ---------------------------------------------------------------- 2. the fleet uses allied ports
        static int OwnPortsExcept(Player p, string provinceId)
        {
            int n = 0; string name = Key(p);
            try
            {
                var ports = CampaignMap.PortsDb.Ports;
                for (int i = 0; i < ports.Count; i++)
                {
                    var pe = ports[i]; if (pe == null || pe.ProvinceId == provinceId) continue;
                    var pr = pe.CurrentProvince; if (pr != null && pr.Controller == name) n++;
                }
            }
            catch { }
            return n;
        }
        static PortElement AllyPort(Player p, Vector3 pos, out Player owner)
        {
            owner = null; PortElement best = null; float bestD = float.MaxValue;
            try
            {
                var allies = new HashSet<string>();
                var en = p.InAllianceWith().GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (un.MoveNext()) { var a = en.Current; if (a != null && a.isMajor && !Disabled(a) && a.Pointer != p.Pointer) allies.Add(Key(a)); }
                if (allies.Count == 0) return null;
                var ports = CampaignMap.PortsDb.Ports;
                for (int i = 0; i < ports.Count; i++)
                {
                    var pe = ports[i]; if (pe == null) continue; var pr = pe.CurrentProvince; if (pr == null || pr.Controller == null || !allies.Contains(pr.Controller)) continue;
                    float d = (pe.WorldCoord - pos).sqrMagnitude; if (d < bestD) { bestD = d; best = pe; }
                }
                if (best != null) owner = ByName(best.CurrentProvince.Controller);
            }
            catch { }
            return best;
        }
        // called from Patch_ScrapShip during a conquest: true = the ship was moved to an allied port and must not be scrapped
        internal static bool TryRelocate(Ship ship, Player loser, string lostProvince)
        {
            if (!Active || ship == null || loser == null || !loser.isMajor || !Exists(ship)) return false;
            try
            {
                if (OwnPortsExcept(loser, lostProvince) > 0) return false;
                var at = ship.PortLocation; Vector3 pos = at != null ? at.WorldCoord : ship.transform.position;
                Player ally; var port = AllyPort(loser, pos, out ally); if (port == null) return false;
                ship.SetPortLocation(port, default(Il2CppSystem.Guid), true, true, true, "lost all ports - allied port");
                Log($"{ship.name} ({N(loser)}) lost its last port ({lostProvince}) - based in the allied port {port.Name} ({N(ally)})");
                if (loser.isMain && logOnce.Add("ntc|" + Month())) Notice("All ports lost", $"Your last port is lost. Your ships are now based in allied ports, starting with {port.Name}.");
                return true;
            }
            catch (Exception e) { Log("relocate failed: " + e.Message); return false; }
        }

        // ---------------------------------------------------------------- 3. peace
        static string BestCorePort(Player w, Player holder)
        {
            string best = null; float bestPort = -1f;
            try
            {
                var byId = CampaignMap.Instance.Provinces.ProvinceById; string wn = Key(w), hn = Key(holder);
                foreach (var kv in Independence.Cores)
                {
                    if (kv.Value != wn) continue; Province pr; if (!byId.TryGetValue(kv.Key, out pr) || pr == null) continue;
                    if (pr.Controller != hn || !pr.HavePort) continue;
                    float v = 0f; try { v = pr.Port; } catch { }
                    if (v > bestPort) { bestPort = v; best = kv.Key; }
                }
            }
            catch { }
            return best;
        }
        static void PostPeace(Relation relation, bool agree)
        {
            if (!Active || relation == null || busy) return;
            try
            {
                bool war = true; try { war = relation.isWar; } catch { }
                if (war) return;
                foreach (var w in new[] { relation.a, relation.b })
                {
                    if (!IsWaiting(w)) continue;
                    var other = relation.OppositePlayer(w);
                    Player winner = null; try { winner = relation.WhoWins(); } catch { }
                    if (winner != null && other != null && winner.Pointer == w.Pointer)
                    {
                        var id = BestCorePort(w, other);
                        if (id != null) { Transfer(w, id, "peace terms"); Log($"peace between {N(w)} and {N(other)}: {N(w)} won the war and gets back its port province {id}"); if (w.isMain || other.isMain) Notice("Port returned", $"Peace terms: {N(other)} returns the port province {id} to {N(w)}."); }
                    }
                    bool stillWar = false; try { stillWar = w.IsAtWar(); } catch { }
                    if (!HavePorts(w) && !stillWar) FleetToAlly(w);
                }
            }
            catch (Exception e) { Log("peace: " + e.Message); }
        }
        static void FleetToAlly(Player w)
        {
            var ships = Ships(w); if (ships.Count == 0) return;
            var cc = CampaignController.Instance; int moved = 0; string to = "";
            foreach (var s in ships)
            {
                try
                {
                    var at = s.PortLocation; Vector3 pos = at != null ? at.WorldCoord : s.transform.position;
                    Player rec; var port = AllyPort(w, pos, out rec);
                    if (rec == null && at != null && at.CurrentProvince != null) { rec = ByName(at.CurrentProvince.Controller); port = at; }
                    if (rec == null || rec.Pointer == w.Pointer || Disabled(rec)) continue;
                    var list = new Il2CppSystem.Collections.Generic.List<Ship>();
                    cc.TransferShipToNewOwner(rec, s.id, ref list, port, false);
                    PrizeDesign.Ensure(s, rec, "Portless");
                    moved++; if (!to.Contains(N(rec))) to += (to.Length > 0 ? ", " : "") + N(rec);
                }
                catch (Exception e) { Log("fleet transfer: " + e.Message); }
            }
            if (moved > 0)
            {
                Log($"{N(w)} made peace without a port of its own: {moved} ship(s) handed over to {to}");
                if (w.isMain) Notice("Fleet handed over", $"Your nation made peace without a port of its own. Your fleet ({moved} ships) has been handed over to {to}.");
            }
        }

        // ---------------------------------------------------------------- 4. a core port taken by an ally is handed back
        static void PostTransfer(Player newOwner, string provinceId)
        {
            if (!Active || busy || newOwner == null || provinceId == null) return;
            try
            {
                string nation; if (!Independence.Cores.TryGetValue(provinceId, out nation) || Key(newOwner) == nation) return;
                var w = ByName(nation); if (!IsWaiting(w)) return;
                Province pr; if (!CampaignMap.Instance.Provinces.ProvinceById.TryGetValue(provinceId, out pr) || pr == null || !pr.HavePort) return;
                if (!Allied(newOwner, w)) return;
                pendingReturns.Add(new KeyValuePair<string, string>(nation, provinceId));
            }
            catch { }
        }
        static void Transfer(Player w, string id, string why)
        {
            busy = true;
            try { CampaignController.Instance.TransferProvinceToNewOwner(w, id, true, false, why); }
            catch (Exception e) { Log($"transfer of {id} failed: " + e.Message); }
            finally { busy = false; }
        }

        // ---------------------------------------------------------------- 5. next war and land offensive
        internal static void Tick()
        {
            if (!Active) return;
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (battle) return;
            if (pendingReturns.Count > 0)
            {
                var list = new List<KeyValuePair<string, string>>(pendingReturns); pendingReturns.Clear();
                foreach (var kv in list)
                {
                    var w = ByName(kv.Key); if (!IsWaiting(w)) continue;
                    Province pr; try { if (!CampaignMap.Instance.Provinces.ProvinceById.TryGetValue(kv.Value, out pr) || pr == null) continue; } catch { continue; }
                    var holder = ByName(pr.Controller); if (holder == null || !Allied(holder, w)) continue;
                    Transfer(w, kv.Value, "returned by an ally");
                    Log($"{N(holder)} took {kv.Value}, a national core port of {N(w)}, and handed it back to its ally");
                    if (w.isMain || holder.isMain) Notice("Port returned", $"{N(holder)} has taken {kv.Value} and handed the port back to {N(w)}.");
                }
            }
            int m = Month(); if (m <= 0 || m == lastMonth) return; lastMonth = m;
            try
            {
                var majors = CampaignController.Instance.CampaignData.PlayersMajor;
                for (int i = 0; i < majors.Count; i++)
                {
                    var w = majors[i]; if (!IsWaiting(w)) continue;
                    var holders = Holders(w);
                    Log($"{N(w)} waits without ports: {Provinces(w)} province(s), {Ships(w).Count} ship(s) in allied ports; its core ports are held by {(holders.Count > 0 ? string.Join(", ", holders.Select(N)) : "nobody")}");
                    if (!w.isAi) continue;
                    foreach (var h in holders)
                    {
                        if (AtWar(w, h) || Allied(w, h)) continue;
                        bool hw = false; try { hw = h.IsAtWar(); } catch { }
                        if (!hw) continue;
                        try { Economy.DeclareWarPublic(w, h, $"joins the war against {N(h)} to regain its ports"); } catch (Exception e) { Log("war: " + e.Message); }
                    }
                }
            }
            catch (Exception e) { Log("monthly: " + e.Message); }
        }
        static List<Player> Holders(Player w)
        {
            var r = new List<Player>();
            try
            {
                var byId = CampaignMap.Instance.Provinces.ProvinceById; string wn = Key(w);
                foreach (var kv in Independence.Cores)
                {
                    if (kv.Value != wn) continue; Province pr; if (!byId.TryGetValue(kv.Key, out pr) || pr == null || !pr.HavePort || pr.Controller == wn) continue;
                    var h = ByName(pr.Controller); if (h != null && !r.Any(x => x.Pointer == h.Pointer)) r.Add(h);
                }
            }
            catch { }
            return r;
        }
        // ProvinceBattleManager.TryStartBattle prefix: the waiting power's army attacks a core port province next to its own
        static bool Pre_TryStartBattle(Player player)
        {
            try
            {
                if (!IsWaiting(player)) return true;
                string wn = Key(player); Province from = null, target = null; float best = -1f;
                foreach (var own in player.provinces)
                {
                    if (own == null || ProvinceBattleManager.InBattle(own)) continue;
                    var nb = own.NeighbourProvinces; if (nb == null) continue;
                    foreach (var t in nb)
                    {
                        if (t == null || !t.HavePort) continue;
                        string nation; if (!Independence.Cores.TryGetValue(t.Id, out nation) || nation != wn) continue;
                        var h = ByName(t.Controller); if (h == null || !AtWar(player, h) || ProvinceBattleManager.InBattle(t)) continue;
                        float v = 0f; try { v = t.Port; } catch { }
                        if (v > best) { best = v; from = own; target = t; }
                    }
                }
                if (target == null) return true;
                ProvinceBattleManager.StartBattle(from, target, true);
                Log($"land offensive: {N(player)} attacks {target.Id} ({target.Controller}) from {from.Id} to regain a port");
                return false;
            }
            catch (Exception e) { Log("land offensive: " + e.Message); return true; }
        }
        // no shipbuilding without a port
        static bool Pre_BuildNewShips(Player player)
        {
            try { if (IsWaiting(player)) { Once("build|" + Key(player) + "|" + Month(), $"{N(player)} builds no ships while it has no port"); return false; } } catch { }
            try { if (ShipGenSpeed.HardStopNow(player)) return false; } catch { }   // 1.11.481: end-of-turn time limit
            return true;
        }
        static bool Pre_Finish(Player __0, CampaignController.FinishCampaignType __1)
        {
            try
            {
                if (__1.ToString() != "TotalDefeat" || __0 == null || !__0.isMain) return true;
                if (!KeepAlive(__0)) return true;
                if (logOnce.Add("pn|" + Month())) Notice("All ports lost", "Your nation has lost all its ports. The campaign goes on: your ships use allied ports, and your ports can be regained by a rising, a land offensive, peace terms or an ally.");
                return false;
            }
            catch { return true; }
        }
        static void Post_Peace(Relation relation, bool agree) { PostPeace(relation, agree); }
        static void Post_Transfer(Player newOwner, string provinceId) { PostTransfer(newOwner, provinceId); }

        internal static void Apply(HarmonyLib.Harmony h)
        {
            P(h, AccessTools.Method(typeof(CampaignController), "FinishCampaign"), nameof(Pre_Finish), null);
            P(h, AccessTools.Method(typeof(CampaignController), "PeaceTreaty", new[] { typeof(Relation), typeof(bool) }), null, nameof(Post_Peace));
            P(h, AccessTools.Method(typeof(CampaignController), "TransferProvinceToNewOwner"), null, nameof(Post_Transfer));
            P(h, AccessTools.Method(typeof(ProvinceBattleManager), "TryStartBattle", new[] { typeof(Player) }), nameof(Pre_TryStartBattle), null);
            P(h, AccessTools.Method(typeof(CampaignController), "BuildNewShips", new[] { typeof(Player), typeof(float) }), nameof(Pre_BuildNewShips), null);
        }
        static void P(HarmonyLib.Harmony h, System.Reflection.MethodBase t, string pre, string post)
        {
            try
            {
                if (t == null) throw new Exception("method not found");
                h.Patch(t, pre != null ? new HarmonyMethod(AccessTools.Method(typeof(Portless), pre)) : null, post != null ? new HarmonyMethod(AccessTools.Method(typeof(Portless), post)) : null);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Portless] hook " + (t != null ? t.Name : "?") + " failed: " + e.Message); }
        }
    }
}
