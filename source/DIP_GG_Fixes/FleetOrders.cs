// 1.11.109 (beta line, off by default): standing orders for task forces on the campaign map - a mechanic shared by the
// player and the AI, not only a move target.
//   Readiness - the force holds its station and goes for a visible enemy force inside its reaction radius; its zone of
//               interception is larger (TaskForce.GetZoneRadius postfix); it joins the defence of a port strike inside the
//               reaction radius as reinforcements; prolonged readiness tires the crews (training falls after some turns).
//   Shadow    - the force follows a visible enemy force at a distance; its interception zone is much smaller, so the game's
//               own encounter check (zones of both forces must overlap) rarely produces a battle with the shadowed force;
//               contact lost -> last known position. It keeps the enemy in sight for the fog of war (nearby own force).
//   Escort    - the force follows a friendly force (convoy, invasion transport, any force); it joins battles of its side
//               within the escort radius when the battle is a convoy battle or involves the escorted force, and fewer
//               transports are exposed (merchant count of that side reduced), also in submarine raids on transports.
// AI commander (FleetOrdersAI): AI great powers at war give idle forces orders each turn - convoy and invasion forces get an
// escort, light forces shadow visible enemy forces, idle forces near own ports hold readiness (only intercepting forces they
// can match). AI readiness never pulls a force back to a station, so the game's own fleet moves are not undone.
// Orders are applied once per turn (CampaignController.ProcessTaskForces prefix). Window: F6. Stored per campaign in
// UserData\IronAdmiral_Economy\orders_<fingerprint>.txt. Log: "[Orders]".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class FleetOrders
    {
        internal static MelonPreferences_Entry<bool> On, AiOn;
        internal static MelonPreferences_Entry<string> Key;
        internal static MelonPreferences_Entry<float> DefaultRadiusKm, ReadyZoneMult, ShadowZoneMult, EscortJoinKm, EscortTrFactor, FatiguePerTurn;
        internal static MelonPreferences_Entry<int> FatigueAfterTurns;
        static KeyCode key = KeyCode.F6;
        internal enum Kind { None, Readiness, Shadow, Escort }
        class Order
        {
            public Kind K; public string Target; public float RadiusKm; public Vector3 Station; public bool HasStation;
            public Vector3 LastSeen; public bool HasLastSeen; public bool Ai; public int ReadyTurns;
        }
        static readonly Dictionary<string, Order> orders = new Dictionary<string, Order>();
        static readonly Dictionary<IntPtr, float> zoneMult = new Dictionary<IntPtr, float>();
        static readonly HashSet<string> joinedThisTurn = new HashSet<string>();
        static string loadedFor, lastDay; static bool visible; static int page; static int errors;
        static string lastSummary = "";
        static GUIStyle sTxt, sBtn, sTitle, sDim;
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>();
        static readonly List<KeyValuePair<Rect, Action>> next = new List<KeyValuePair<Rect, Action>>();

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("FleetOrders", false, null, "Campaign (beta): standing orders for task forces - readiness, shadowing, escort - with their own effects on interception, battles and convoy losses; window on the key below");
            AiOn = cat.CreateEntry("FleetOrdersAI", true, null, "Campaign (beta): AI great powers at war give their idle task forces the same standing orders (needs FleetOrders)");
            Key = cat.CreateEntry("OrdersKey", "F6", null, "Key that opens the task force orders window");
            DefaultRadiusKm = cat.CreateEntry("OrdersReactionRadiusKm", 600f, null, "Default reaction radius of a readiness order (km)");
            ReadyZoneMult = cat.CreateEntry("OrdersReadinessZone", 1.5f, null, "Interception zone of a force in readiness (x the game's zone)");
            ShadowZoneMult = cat.CreateEntry("OrdersShadowZone", 0.25f, null, "Interception zone of a shadowing force (x the game's zone; small = it rarely ends up in battle with the shadowed force)");
            EscortJoinKm = cat.CreateEntry("OrdersEscortJoinKm", 300f, null, "An escort joins battles of its side within this distance (km)");
            EscortTrFactor = cat.CreateEntry("OrdersEscortTransports", 0.6f, null, "Share of transports still exposed in a battle or submarine raid when an escort is present");
            FatigueAfterTurns = cat.CreateEntry("OrdersReadinessFatigueTurns", 3, null, "Turns of continuous readiness before the crews start to tire");
            FatiguePerTurn = cat.CreateEntry("OrdersReadinessFatigue", 1.5f, null, "Crew training lost per further turn of readiness (points of 100)");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
        }
        static bool Active => On != null && On.Value;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Orders] " + m); }
        static float Kpu() { try { if (CampaignMap.UnityDistanceToWorldKmMultiplier > 0f) return CampaignMap.UnityDistanceToWorldKmMultiplier; } catch { } return 1f; }
        static float Km(Vector3 a, Vector3 b) { var d = a - b; d.y = 0f; return d.magnitude * Kpu(); }

        // ------------------------------------------------------------------ data
        static Player Main() { try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.isMain) return p; } } catch { } return null; }
        static List<Player> Players() { var l = new List<Player>(); try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) if (ps[i] != null) l.Add(ps[i]); } catch { } return l; }
        static List<CampaignController.TaskForce> All()
        {
            var l = new List<CampaignController.TaskForce>();
            try { var a = CampaignController.Instance.CampaignData.TaskForces; for (int i = 0; a != null && i < a.Count; i++) if (a[i] != null) l.Add(a[i]); } catch { }
            return l;
        }
        static string Id(CampaignController.TaskForce tf) { try { return tf.Id.ToString(); } catch { return null; } }
        static bool OwnedBy(CampaignController.TaskForce tf, Player p) { try { return p != null && tf.Controller != null && tf.Controller.Pointer == p.Pointer; } catch { return false; } }
        static int Count(CampaignController.TaskForce tf) { try { return tf.Vessels != null ? tf.Vessels.Count : 0; } catch { return 0; } }
        static List<Ship> Ships(CampaignController.TaskForce tf)
        {
            var l = new List<Ship>();
            try { var v = tf.Vessels; for (int i = 0; v != null && i < v.Count; i++) { var s = v[i] != null ? v[i].TryCast<Ship>() : null; if (s != null) l.Add(s); } } catch { }
            return l;
        }
        static float Tons(CampaignController.TaskForce tf) { float t = 0f; foreach (var s in Ships(tf)) { try { t += s.Tonnage(); } catch { } } return t; }
        static bool Light(CampaignController.TaskForce tf)
        {
            var sh = Ships(tf); if (sh.Count == 0 || sh.Count > 4) return false;
            foreach (var s in sh) { string n = ""; try { n = s.shipType.name.ToLowerInvariant(); } catch { } if (n != "dd" && n != "cl" && n != "tb") return false; }
            return true;
        }
        static bool Surface(CampaignController.TaskForce tf) { return Ships(tf).Count > 0; }
        static bool Convoy(CampaignController.TaskForce tf) { try { return tf.IsConvoyMission; } catch { return false; } }
        static bool Moving(CampaignController.TaskForce tf) { try { return tf.IsMoving(); } catch { return false; } }
        static float PortKm(CampaignController.TaskForce tf) { try { float sq = tf.SqrDistanceToNearestFriendlyPort; return sq >= 0f ? Mathf.Sqrt(sq) * Kpu() : 99999f; } catch { return 99999f; } }
        static string Name(CampaignController.TaskForce tf)
        {
            string n = "?"; try { var sh = Ships(tf); if (sh.Count > 0) n = Dispatch.Ship(sh[0].name); } catch { }
            string who = "?"; try { who = tf.Controller.data.nameUi; } catch { }
            int c = Count(tf); return $"{who}: {n}{(c > 1 ? " +" + (c - 1) : "")}";
        }
        // an enemy force counts as seen when the fog of war lets the observer locate it (or the fog is off)
        static bool Seen(Player obs, CampaignController.TaskForce tf) { try { if (!Fog.MapActive) return true; return Fog.Q(obs, tf) >= 0.35f; } catch { return true; } }
        static bool Busy(CampaignController.TaskForce tf)
        {
            try { if (tf.GroupMovingToBattle) return true; } catch { }
            try { var g = tf.BusyForEvent.ToString(); return !string.IsNullOrEmpty(g) && g != "00000000-0000-0000-0000-000000000000"; } catch { return false; }
        }
        static bool AtWar(Player a, Player b)
        {
            if (a == null || b == null || a.Pointer == b.Pointer) return false;
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.isWar && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return true; } } catch { }
            return false;
        }
        static bool Enemy(Player obs, CampaignController.TaskForce tf) { try { return AtWar(obs, tf.Controller); } catch { return false; } }

        // ------------------------------------------------------------------ persistence
        static string FilePath() { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral_Economy", "orders_" + Economy.Fingerprint() + ".txt"); }
        static void EnsureLoaded()
        {
            string fp = null; try { fp = Economy.Fingerprint(); } catch { }
            if (fp == null || fp == loadedFor) return;
            loadedFor = fp; orders.Clear(); zoneMult.Clear();
            try
            {
                var f = FilePath(); if (!File.Exists(f)) return;
                foreach (var l in File.ReadAllLines(f))
                {
                    var c = l.Split('|'); if (c.Length < 9) continue;
                    if (!Enum.TryParse(c[1], out Kind k)) continue;
                    var o = new Order { K = k, Target = c[2] == "" ? null : c[2], RadiusKm = P(c[3]), HasStation = c[4] == "1", Station = new Vector3(P(c[5]), P(c[6]), P(c[7])) };
                    if (c.Length > 9) o.Ai = c[9] == "ai"; if (c.Length > 10) int.TryParse(c[10], out o.ReadyTurns);
                    orders[c[0]] = o;
                }
                Log($"{orders.Count} order(s) loaded");
            }
            catch (Exception e) { Log("load: " + e.Message); }
        }
        static float P(string s) { float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v); return v; }
        static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
        static void Save()
        {
            try
            {
                var f = FilePath(); Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllLines(f, orders.Where(kv => kv.Value.K != Kind.None).Select(kv => $"{kv.Key}|{kv.Value.K}|{kv.Value.Target}|{F(kv.Value.RadiusKm)}|{(kv.Value.HasStation ? 1 : 0)}|{F(kv.Value.Station.x)}|{F(kv.Value.Station.y)}|{F(kv.Value.Station.z)}|0|{(kv.Value.Ai ? "ai" : "player")}|{kv.Value.ReadyTurns}"));
            }
            catch (Exception e) { Log("save: " + e.Message); }
        }
        static void RebuildZones(List<CampaignController.TaskForce> all)
        {
            zoneMult.Clear();
            foreach (var tf in all)
            {
                var id = Id(tf); if (id == null || !orders.TryGetValue(id, out var o)) continue;
                if (o.K == Kind.Readiness) zoneMult[tf.Pointer] = Mathf.Max(0.1f, ReadyZoneMult.Value);
                else if (o.K == Kind.Shadow) zoneMult[tf.Pointer] = Mathf.Max(0.05f, ShadowZoneMult.Value);
            }
        }

        // ------------------------------------------------------------------ mechanics (game hooks)
        internal static void Zone(CampaignController.TaskForce tf, ref float r)
        {
            if (!Active || zoneMult.Count == 0 || tf == null) return;
            try { if (zoneMult.TryGetValue(tf.Pointer, out var m)) r *= m; } catch { }
        }
        static Kind KindOf(CampaignController.TaskForce tf, out Order o) { o = null; var id = Id(tf); if (id != null && orders.TryGetValue(id, out o)) return o.K; return Kind.None; }
        static bool Contains(Il2CppSystem.Collections.Generic.List<Ship> l, Ship s) { try { for (int i = 0; l != null && i < l.Count; i++) if (l[i] != null && l[i].Pointer == s.Pointer) return true; } catch { } return false; }
        static bool InBattle(CampaignBattle b, Ship s) { return Contains(b.AttackerShips, s) || Contains(b.DefenderShips, s) || Contains(b.ShipsAdditionalAttacker, s) || Contains(b.ShipsAdditionalDefender, s); }

        // a battle is about to be added (MissionGenerator.AddBattle prefix): escorts and forces in readiness join their side
        internal static void OnBattle(CampaignBattle b)
        {
            if (!Active || b == null || orders.Count == 0) return;
            try
            {
                EnsureLoaded();
                BattleTypeEx.EBattleType bt = BattleTypeEx.EBattleType.Battle; try { bt = b.Type.CurrentType; } catch { }
                Vector3 pos = Vector3.zero; try { pos = b.BattleWorldPos; } catch { return; }
                Player att = null, def = null; try { att = b.Attacker; def = b.Defender; } catch { }
                var all = All(); var byId = new Dictionary<string, CampaignController.TaskForce>(); foreach (var t in all) { var i = Id(t); if (i != null) byId[i] = t; }
                foreach (var side in new[] { true, false })
                {
                    Player sp = side ? att : def; if (sp == null) continue;
                    int joined = 0; var addList = side ? b.ShipsAdditionalAttacker : b.ShipsAdditionalDefender; if (addList == null) continue;
                    foreach (var tf in all)
                    {
                        if (joined >= 2) break;
                        if (!OwnedBy(tf, sp) || Busy(tf)) continue;
                        var id = Id(tf); if (id == null || joinedThisTurn.Contains(id)) continue;
                        var k = KindOf(tf, out var o); if (k != Kind.Escort && k != Kind.Readiness) continue;
                        var ships = Ships(tf); if (ships.Count == 0 || ships.Any(s => InBattle(b, s))) continue;
                        float d = Km(tf.WorldPos, pos); bool join = false; string why = "";
                        if (k == Kind.Escort && d <= EscortJoinKm.Value)
                        {
                            if (bt == BattleTypeEx.EBattleType.Convoy) { join = true; why = "escort joins the convoy battle"; }
                            else if (o.Target != null && byId.TryGetValue(o.Target, out var tg) && Ships(tg).Any(s => InBattle(b, s))) { join = true; why = "escort follows its force into battle"; }
                        }
                        else if (k == Kind.Readiness && !side && bt == BattleTypeEx.EBattleType.Strike && Km(o.HasStation ? o.Station : tf.WorldPos, pos) <= (o.RadiusKm > 0f ? o.RadiusKm : DefaultRadiusKm.Value))
                        { join = true; why = "force in readiness defends the port"; }
                        if (!join) continue;
                        foreach (var s in ships) { try { addList.Add(s); } catch { } }
                        joinedThisTurn.Add(id); joined++;
                        Log($"battle ({bt}) near {Name(tf)}: {why} as reinforcements ({ships.Count} ship(s), {d:0} km){(o.Ai ? " [AI]" : "")}");
                        if (k == Kind.Escort) Merchants(b, side, $"{Name(tf)}");
                    }
                }
                if (joinedThisTurn.Count > 0) { try { b.ReserveShips(); } catch { } }
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] battle: " + e.Message); }
        }
        static void Merchants(CampaignBattleBase b, bool attackerSide, string who)
        {
            try
            {
                int m = attackerSide ? b.AttackerMerchantsCount : b.DefenderMerchantsCount; if (m <= 0) return;
                int n = Mathf.Max(1, Mathf.RoundToInt(m * Mathf.Clamp01(EscortTrFactor.Value)));
                if (n >= m) return;
                if (attackerSide) b.AttackerMerchantsCount = n; else b.DefenderMerchantsCount = n;
                Log($"escort {who}: transports exposed {m} -> {n}");
            }
            catch { }
        }
        // submarine raid on transports (MissionGenerator.AddBattle(CampaignBattleWithSubmarine) prefix): an escort near the raid
        // keeps part of the transports out of reach
        internal static void OnSubBattle(CampaignBattleBase b)
        {
            if (!Active || b == null || orders.Count == 0) return;
            try
            {
                EnsureLoaded();
                Vector3 pos = b.BattleWorldPos; Player att = null, def = null; try { att = b.Attacker; def = b.Defender; } catch { }
                foreach (var side in new[] { true, false })
                {
                    Player sp = side ? att : def; if (sp == null) continue;
                    foreach (var tf in All())
                    {
                        if (!OwnedBy(tf, sp) || KindOf(tf, out var o) != Kind.Escort) continue;
                        if (Km(tf.WorldPos, pos) > EscortJoinKm.Value * 1.5f) continue;
                        Merchants(b, side, Name(tf)); break;
                    }
                }
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] submarine raid: " + e.Message); }
        }

        // ------------------------------------------------------------------ turn
        internal static void BeforeTurn()
        {
            if (!Active) return;
            // ProcessTaskForces may run more than once per turn: apply orders once per campaign date
            string day = null; try { day = CampaignController.Instance.CurrentDate.AsDate().ToString("yyyy-MM-dd"); } catch { }
            if (day != null && day == lastDay) return; lastDay = day; joinedThisTurn.Clear();
            try
            {
                EnsureLoaded();
                var all = All(); var byId = new Dictionary<string, CampaignController.TaskForce>();
                foreach (var t in all) { var i = Id(t); if (i != null) byId[i] = t; }
                // forces that no longer exist
                foreach (var kv in orders.ToList()) if (!byId.ContainsKey(kv.Key)) orders.Remove(kv.Key);
                int aiCount = 0; if (AiOn != null && AiOn.Value) { try { aiCount = AiCommander(all, byId); } catch (Exception e) { if (errors++ < 10) Log("AI commander: " + e.Message); } }
                int moves = 0, tired = 0; var kinds = new Dictionary<Kind, int>();
                foreach (var kv in orders.ToList())
                {
                    var o = kv.Value; if (o.K == Kind.None) continue;
                    if (!byId.TryGetValue(kv.Key, out var tf)) continue;
                    kinds.TryGetValue(o.K, out var kc); kinds[o.K] = kc + 1;
                    Player owner = null; try { owner = tf.Controller; } catch { }
                    if (o.K == Kind.Readiness)
                    {
                        o.ReadyTurns++;
                        if (o.ReadyTurns > FatigueAfterTurns.Value && FatiguePerTurn.Value > 0f)
                        {
                            foreach (var s in Ships(tf)) { try { float tr = s.CrewTrainingAmount; if (tr > 25f) s.CrewTrainingAmount = Mathf.Max(25f, tr - FatiguePerTurn.Value); } catch { } }
                            tired++;
                        }
                    }
                    else o.ReadyTurns = 0;
                    if (Busy(tf)) continue;
                    if (Move(tf, o, owner, all, byId)) moves++;
                }
                RebuildZones(all);
                Save();
                lastSummary = $"{day}: {orders.Count(x => x.Value.K != Kind.None && !x.Value.Ai)} player order(s), {orders.Count(x => x.Value.K != Kind.None && x.Value.Ai)} AI order(s) ({string.Join(", ", kinds.Select(x => x.Key + " " + x.Value))}); {moves} move(s), {tired} force(s) tiring in readiness{(aiCount > 0 ? $", {aiCount} new AI order(s)" : "")}";
                Log(lastSummary);
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] turn: " + e.Message); }
        }

        // sets the destination of one force by its order; true when the force was sent somewhere
        static bool Move(CampaignController.TaskForce tf, Order o, Player owner, List<CampaignController.TaskForce> all, Dictionary<string, CampaignController.TaskForce> byId)
        {
            Vector3 pos = tf.WorldPos, dest = pos; string why = null;
            if (o.K == Kind.Readiness)
            {
                // the AI keeps its own fleet moves: its station follows the force, and it only reacts while idle
                if (o.Ai && (Moving(tf) || !o.HasStation)) { o.Station = pos; o.HasStation = true; }
                if (!o.HasStation) { o.Station = pos; o.HasStation = true; }
                float radius = o.RadiusKm > 0f ? o.RadiusKm : DefaultRadiusKm.Value;
                CampaignController.TaskForce best = null; float bd = float.MaxValue; float myT = o.Ai ? Tons(tf) : 0f;
                foreach (var e in all)
                {
                    if (!Enemy(owner, e) || !Seen(owner, e) || !Surface(e)) continue;
                    float dKm = Km(e.WorldPos, o.Station); if (dKm > radius) continue;
                    if (o.Ai && myT < 0.8f * Tons(e)) continue;   // the AI only goes for forces it can match
                    if (dKm < bd) { bd = dKm; best = e; }
                }
                if (best != null) { dest = best.WorldPos; why = $"intercepts {Name(best)} ({bd:0} km from station)"; }
                else if (!o.Ai) { dest = o.Station; why = Km(pos, o.Station) > 30f ? "returns to station" : null; }
            }
            else if (o.K == Kind.Shadow || o.K == Kind.Escort)
            {
                byId.TryGetValue(o.Target ?? "", out var tg);
                bool ok = tg != null && (o.K == Kind.Escort ? OwnedBy(tg, owner) : (Enemy(owner, tg) && Seen(owner, tg)));
                if (ok)
                {
                    var tp = tg.WorldPos; o.LastSeen = tp; o.HasLastSeen = true;
                    if (o.K == Kind.Escort) { dest = tp; try { if (tg.IsMoving()) dest = tg.ToPosition; } catch { } why = $"escorts {Name(tg)}"; }
                    else { var dir = (pos - tp); dir.y = 0f; float keep = 150f / Kpu(); dest = dir.sqrMagnitude > 1e-6f ? tp + dir.normalized * keep : tp; why = $"shadows {Name(tg)}"; }
                }
                else if (o.HasLastSeen) { dest = o.LastSeen; o.HasLastSeen = false; why = o.K == Kind.Shadow ? "contact lost - to the last known position" : "escorted force gone"; }
                else if (o.Ai) o.K = Kind.None;   // an AI order without its target lapses; the commander gives a new one
                if (tg == null && o.K == Kind.Escort) o.K = Kind.None;
            }
            if (why == null) return false;
            if (Km(dest, pos) < 20f) return false;
            try
            {
                tf.ToPosition = dest; tf.recalculatePathRequest = 1; tf.RecalculatePath();
                Log($"{Name(tf)}: {o.K}{(o.Ai ? " [AI]" : "")} - {why}; destination set ({Km(dest, pos):0} km)");
                return true;
            }
            catch (Exception e) { if (errors++ < 10) Log($"{Name(tf)}: moving failed - {e.Message}"); return false; }
        }

        // ------------------------------------------------------------------ AI commander
        static int AiCommander(List<CampaignController.TaskForce> all, Dictionary<string, CampaignController.TaskForce> byId)
        {
            int made = 0;
            var players = Players();
            foreach (var p in players)
            {
                bool ai = false, major = false, off = true; try { ai = p.isAi; major = p.isMajor; off = p.isDisabled; } catch { }
                if (!ai || !major || off) continue;
                var foes = players.Where(x => AtWar(p, x)).ToList(); if (foes.Count == 0) { foreach (var kv in orders.ToList()) if (kv.Value.Ai && byId.TryGetValue(kv.Key, out var t0) && OwnedBy(t0, p)) orders.Remove(kv.Key); continue; }
                var mine = all.Where(t => OwnedBy(t, p) && Surface(t)).ToList(); if (mine.Count == 0) continue;
                bool Free(CampaignController.TaskForce t) { var id = Id(t); if (id == null || Busy(t) || Convoy(t)) return false; if (orders.TryGetValue(id, out var oo) && oo.K != Kind.None && (!oo.Ai || oo.K != Kind.Readiness)) return false; return true; }
                Order Give(CampaignController.TaskForce t, Kind k, string target)
                {
                    var id = Id(t); var o = new Order { K = k, Target = target, RadiusKm = DefaultRadiusKm.Value, Ai = true };
                    if (orders.TryGetValue(id, out var old) && old.K == k) o.ReadyTurns = old.ReadyTurns;
                    if (k == Kind.Readiness) { o.Station = t.WorldPos; o.HasStation = true; }
                    orders[id] = o; made++; return o;
                }
                float myT = mine.Sum(Tons), foeT = all.Where(t => t.Controller != null && foes.Any(f => OwnedBy(t, f)) && Surface(t)).Sum(Tons);
                bool weaker = myT < 0.8f * foeT;
                // 1. escort for convoy and invasion forces
                foreach (var cv in all.Where(t => OwnedBy(t, p) && Convoy(t)))
                {
                    var cid = Id(cv); if (cid == null) continue;
                    if (orders.Any(x => x.Value.K == Kind.Escort && x.Value.Target == cid)) continue;
                    var esc = mine.Where(t => t.Pointer != cv.Pointer && Free(t) && !Moving(t)).OrderBy(t => Km(t.WorldPos, cv.WorldPos)).FirstOrDefault();
                    if (esc != null && Km(esc.WorldPos, cv.WorldPos) <= 1500f) { Give(esc, Kind.Escort, cid); Log($"AI {p.data.nameUi}: {Name(esc)} escorts {Name(cv)}"); }
                }
                // 2. light forces shadow visible enemy forces (at most two per nation)
                int shadows = orders.Count(x => x.Value.Ai && x.Value.K == Kind.Shadow && byId.TryGetValue(x.Key, out var st) && OwnedBy(st, p));
                foreach (var e in all.Where(t => Enemy(p, t) && Surface(t) && Seen(p, t)).OrderBy(t => mine.Min(m => Km(m.WorldPos, t.WorldPos))))
                {
                    if (shadows >= 2) break;
                    var eid = Id(e); if (eid == null || orders.Any(x => x.Value.K == Kind.Shadow && x.Value.Target == eid && x.Value.Ai)) continue;
                    var sh = mine.Where(t => Free(t) && !Moving(t) && Light(t)).OrderBy(t => Km(t.WorldPos, e.WorldPos)).FirstOrDefault();
                    if (sh != null && Km(sh.WorldPos, e.WorldPos) <= 800f) { Give(sh, Kind.Shadow, eid); shadows++; Log($"AI {p.data.nameUi}: {Name(sh)} shadows {Name(e)}"); }
                }
                // 3. idle forces near own ports hold readiness (a weaker navy keeps a tighter radius)
                foreach (var t in mine)
                {
                    if (!Free(t) || Moving(t)) continue;
                    var id = Id(t); if (orders.TryGetValue(id, out var cur) && cur.K == Kind.Readiness) continue;
                    if (PortKm(t) > 400f) continue;
                    var o = Give(t, Kind.Readiness, null); o.RadiusKm = DefaultRadiusKm.Value * (weaker ? 0.6f : 1f);
                }
            }
            return made;
        }

        // ------------------------------------------------------------------ window
        internal static void Tick()
        {
            if (!Active) { visible = false; return; }
            try
            {
                bool map = false; try { map = GameManager.IsCampaign && GameManager.IsWorldMap && !GameManager.IsLoadingAny; } catch { }
                if (!map) { visible = false; return; }
                if (Input.GetKeyDown(key)) { visible = !visible; if (visible) { EnsureLoaded(); Log("orders window opened"); } }
                if (visible && Input.GetMouseButtonDown(0))
                {
                    var p = UiScale.Mouse();
                    foreach (var kv in clicks.ToList()) if (kv.Key.Contains(p)) { try { kv.Value(); } catch (Exception e) { Log("click: " + e.Message); } break; }
                }
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] " + e.Message); }
        }
        static void Styles()
        {
            if (sTxt != null) return;
            sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
            sTxt = new GUIStyle(); sTxt.fontSize = 13; sTxt.richText = true; sTxt.normal.textColor = Color.white; sTxt.clipping = TextClipping.Clip;
            sDim = new GUIStyle(); sDim.fontSize = 12; sDim.richText = true; sDim.wordWrap = true; sDim.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            sBtn = new GUIStyle(); sBtn.fontSize = 12; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.richText = true; sBtn.normal.textColor = Color.white;
        }
        static void Button(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }
        static Order Get(string id) { if (!orders.TryGetValue(id, out var o) || o.Ai) { o = new Order { K = Kind.None, RadiusKm = DefaultRadiusKm.Value }; orders[id] = o; } return o; }

        internal static void OnGUI()
        {
            if (!Active || !visible) return;
            try
            {
                var main = Main(); if (main == null) return;
                Styles(); next.Clear();
                var all = All();
                var own = all.Where(t => OwnedBy(t, main)).ToList();
                var enemies = all.Where(t => Enemy(main, t) && Seen(main, t)).ToList();
                float w = Math.Min(900f, UiScale.W - 40f), h = Math.Min(640f, UiScale.H - 120f);
                var r = new Rect((UiScale.W - w) / 2f, 80f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 120f, 24f), $"<b>Task force orders</b> (beta)   ({Key.Value} closes)", sTitle);
                Button(new Rect(r.xMax - 40f, r.y + 8f, 30f, 26f), "X", () => visible = false);
                GUI.Label(new Rect(r.x + 14f, r.y + 36f, w - 28f, 50f), "Readiness: holds its station, intercepts a visible enemy in the radius, wider interception zone, joins the defence of a port in the radius; crews tire after a few turns. Shadow: follows a visible enemy at a distance and rarely gets drawn into battle with it. Escort: follows one of your forces, joins convoy battles and its battles nearby, fewer transports exposed. Orders apply at the start of each turn; a standing order replaces your own move orders.", sDim);
                float y = r.y + 92f, rowH = 30f; int per = Mathf.Max(1, (int)((r.yMax - 70f - y) / rowH));
                int pages = Mathf.Max(1, (own.Count + per - 1) / per); page = Mathf.Clamp(page, 0, pages - 1);
                if (own.Count == 0) GUI.Label(new Rect(r.x + 14f, y, w - 28f, 24f), "You have no task forces at sea.", sTxt);
                foreach (var tf in own.Skip(page * per).Take(per))
                {
                    var id = Id(tf); if (id == null) continue; var o = Get(id);
                    GUI.Label(new Rect(r.x + 14f, y + 5f, 300f, 22f), Name(tf) + $" <color=#AAAAAA>({Count(tf)} ships)</color>", sTxt);
                    float bx = r.x + 320f;
                    foreach (Kind k in Enum.GetValues(typeof(Kind)))
                    {
                        var kk = k; bool cur = o.K == k;
                        Button(new Rect(bx, y + 2f, 82f, 24f), (cur ? "<color=#FFD95A><b>" : "") + k + (cur ? "</b></color>" : ""), () =>
                        {
                            o.K = kk; o.HasLastSeen = false; o.ReadyTurns = 0; o.Ai = false;
                            if (kk == Kind.Readiness) { o.Station = tf.WorldPos; o.HasStation = true; }
                            if (kk == Kind.Shadow) o.Target = enemies.Count > 0 ? Id(enemies[0]) : null;
                            if (kk == Kind.Escort) { var f = own.FirstOrDefault(x => x.Pointer != tf.Pointer); o.Target = f != null ? Id(f) : null; }
                            RebuildZones(All()); Save(); Log($"{Name(tf)}: order {kk}{(o.Target != null && kk >= Kind.Shadow ? " target " + o.Target : "")}");
                        });
                        bx += 86f;
                    }
                    if (o.K == Kind.Readiness)
                    {
                        Button(new Rect(bx, y + 2f, 26f, 24f), "-", () => { o.RadiusKm = Mathf.Max(100f, o.RadiusKm - 100f); Save(); });
                        GUI.Label(new Rect(bx + 30f, y + 5f, 70f, 22f), $"{o.RadiusKm:0} km", sTxt);
                        Button(new Rect(bx + 100f, y + 2f, 26f, 24f), "+", () => { o.RadiusKm = Mathf.Min(3000f, o.RadiusKm + 100f); Save(); });
                    }
                    else if (o.K == Kind.Shadow || o.K == Kind.Escort)
                    {
                        var cands = o.K == Kind.Shadow ? enemies : own.Where(x => x.Pointer != tf.Pointer).ToList();
                        int ci = cands.FindIndex(x => Id(x) == o.Target);
                        string lab = ci >= 0 ? Name(cands[ci]) : (o.Target != null ? "(not in sight)" : "(none)");
                        Button(new Rect(bx, y + 2f, 230f, 24f), lab + "  >", () => { if (cands.Count == 0) return; int n = (ci + 1) % cands.Count; o.Target = Id(cands[n]); Save(); });
                    }
                    y += rowH;
                }
                if (pages > 1)
                {
                    Button(new Rect(r.x + 14f, r.yMax - 60f, 30f, 24f), "<", () => page--);
                    GUI.Label(new Rect(r.x + 50f, r.yMax - 57f, 100f, 22f), $"page {page + 1}/{pages}", sTxt);
                    Button(new Rect(r.x + 150f, r.yMax - 60f, 30f, 24f), ">", () => page++);
                }
                GUI.Label(new Rect(r.x + 14f, r.yMax - 30f, w - 28f, 22f), "<color=#AAAAAA>Last turn: " + (lastSummary.Length > 0 ? lastSummary : "-") + "</color>", sTxt);
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] window: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CampaignController), "ProcessTaskForces")]
    internal static class Patch_FleetOrders
    {
        private static void Prefix() { FleetOrders.BeforeTurn(); }
    }
    [HarmonyPatch(typeof(CampaignController.TaskForce), "GetZoneRadius")]
    internal static class Patch_FleetOrdersZone
    {
        private static void Postfix(CampaignController.TaskForce __instance, ref float __result) { FleetOrders.Zone(__instance, ref __result); }
    }
    [HarmonyPatch(typeof(MissionGenerator), "AddBattle", new Type[] { typeof(CampaignBattle) })]
    internal static class Patch_FleetOrdersBattle
    {
        private static void Prefix(CampaignBattle __0) { FleetOrders.OnBattle(__0); }
    }
    [HarmonyPatch(typeof(MissionGenerator), "AddBattle", new Type[] { typeof(CampaignBattleWithSubmarine) })]
    internal static class Patch_FleetOrdersSubBattle
    {
        private static void Prefix(CampaignBattleWithSubmarine __0) { FleetOrders.OnSubBattle(__0); }
    }
}
