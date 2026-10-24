// 1.11.154 (beta): battle orders for the player's divisions with autonomy. A division under such an order is led by the AI,
// but within the limits of the order:
//   Screen        - stays between the main body and the enemy, intercepts torpedo craft and light forces that come at the main
//                   body, never strays further than the leash from it, falls back behind it when badly damaged;
//   Scout         - goes ahead of the main body, finds the enemy and keeps contact just outside the reach of his heavy guns,
//                   falls back to the main body before stronger light forces or when badly damaged;
//   Torpedo attack- the mod's torpedo charge rules (charge, salvo, turn away, reload, return);
//   Free action   - the full battle AI of the mod (the same rules as the AI's own divisions).
// The game's own Screen and Scout orders (division panel) give the same autonomy (DivisionOrdersVanilla). Any move order the
// player gives the division himself takes command back. Orders: panel in battle for the selected ship's division, or
// Alt+1 (manual) ... Alt+5 (free action).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static partial class BattleAi
    {
        internal enum DivOrder { None, Screen, Scout, Torpedo, Free, Hunt }
        internal class OrderSt { public DivOrder Order; public bool FromVanilla; public bool Issued; public float NextThink; public string Name; public string Reason; public Vector3 LastSeen; public bool Seen; public Ship Leader; public float LastCmd; public Vector3 LastTarget; public bool ClosingUp; }
        static readonly Dictionary<IntPtr, OrderSt> orders = new Dictionary<IntPtr, OrderSt>();
        static IntPtr ordersBattle;
        internal static bool Issuing;   // our own commands - the Division.MoveTo/MoveDir patch ignores them
        static readonly string[] OrderNames = { "Manual", "Screen", "Scout", "Torpedo attack", "Free action", "Hunt" };

        internal static MelonPreferences_Entry<bool> OrdersOn, OrdersVanilla;
        internal static MelonPreferences_Entry<float> Leash, BreakOff, ScoutSearch;
        internal static void CreateOrderPrefs(MelonPreferences_Category cat)
        {
            OrdersOn = cat.CreateEntry("DivisionOrders", true, null, "Battle: orders for the player's divisions with autonomy (screen, scout, torpedo attack, free action) - the division is led by the AI within the limits of the order; panel in battle or Alt+1..Alt+5");
            OrdersVanilla = cat.CreateEntry("DivisionOrdersVanilla", true, null, "Battle: the game's own Screen and Scout orders also give the division autonomy");
            Leash = cat.CreateEntry("DivisionLeash", 6000f, null, "Battle: how far (m) a screening division may move away from the main body (scouts: 2.5 times as far)");
            ScoutSearch = cat.CreateEntry("ScoutSearchKm", 20f, null, "Battle: how far (km) ahead of the main body a scouting division ranges while searching for the enemy (sweeping left and right of the expected bearing)"); // 1.11.201
            BreakOff = cat.CreateEntry("DivisionBreakOffDamage", 0.5f, null, "Battle: average damage (0-1) at which a screening or scouting division breaks off and falls back behind the main body");
        }

        static void OrdersReset()
        {
            IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
            if (bp != ordersBattle) { ordersBattle = bp; orders.Clear(); }
        }
        static OrderSt OrderOf(Division div, bool create)
        {
            OrdersReset();
            if (div == null) return null;
            if (!orders.TryGetValue(div.Pointer, out var o) && create) { o = new OrderSt(); orders[div.Pointer] = o; }
            return o;
        }
        static void OWhy(OrderSt o, string r) { if (o.Reason != r) { o.Reason = r; Log($"{o.Name} [{OrderNames[(int)o.Order]}]: {r}"); } }
        static void Cmd(Division div, OrderSt o, Vector3 to) { Issuing = true; try { div.MoveTo(to); o.Issued = true; o.LastCmd = Time.time; o.LastTarget = to; } catch { } finally { Issuing = false; } }

        internal static void SetOrder(Division div, DivOrder ord, string how)
        {
            if (div == null) return;
            var o = OrderOf(div, true); o.Order = ord; o.FromVanilla = false; o.Issued = false; o.Reason = null; o.NextThink = 0f;
            try { o.Name = div.leader != null ? div.leader.name : "division"; } catch { o.Name = "division"; }
            Log($"{o.Name}: order {OrderNames[(int)ord]} ({how})");
            if (ord != DivOrder.None) try { Issuing = true; if (div.isRetreating || div.retreat) div.Retreat(false); } catch { } finally { Issuing = false; }
        }
        internal static DivOrder CurrentOrder(Division div) { var o = OrderOf(div, false); return o != null ? o.Order : DivOrder.None; }
        internal static string OrderLabel(DivOrder o) { return OrderNames[(int)o]; } // 1.11.216

        // the player gave the division a move order himself - command goes back to him
        // 1.11.159: only a command right after a click or key of the player counts (2nd auto test: the game itself gave a
        // retreating division a course order 1 s after "Free action" and the order ended at once)
        internal static float lastInput = -100f;
        internal static void PlayerCommand(Division div, string what, bool needInput = true)
        {
            if (Issuing || div == null || !GameManager.IsBattle) return;
            // 1.11.203: a destination the player sets (MoveTo) always takes command back - 24.10: a damaged cruiser could not be sent home
            //           because the click was not seen as input and the order kept setting its own course every 1.5 s
            if (needInput && Time.realtimeSinceStartup - lastInput > 0.6f) return;
            if (!orders.TryGetValue(div.Pointer, out var o) || o.Order == DivOrder.None) return;
            if (!needInput && o.FromVanilla) return; // the game's own Screen/Scout logic moves the division itself
            Log($"{o.Name}: {what} by the player - order {OrderNames[(int)o.Order]} ends, manual command");
            o.Order = DivOrder.None; o.FromVanilla = false;
        }

        // the main body: the division of the player's heaviest ship (largest main calibre, capital ships first) other than this one
        static Ship MainBody(Player player, Division own, CampaignBattle battle)
        {
            Ship best = null; float bs = -1f;
            try
            {
                var mine = battle.ShipsOf(player, true); if (mine == null) return null;
                for (int i = 0; i < mine.Count; i++)
                {
                    var s = mine[i]; if (s == null || s.isSinking || s.isDead || !s.isDivLeader) continue;
                    var d = s.division; if (d == null || d.Pointer == own.Pointer) continue;
                    float sc = MainCaliber(s) + (IsCapital(s) ? 1000f : 0f);
                    if (sc > bs) { bs = sc; best = s; }
                }
            }
            catch { }
            return best;
        }
        static Vector3 P(Ship s) { var p = s.transform.position; p.y = 0f; return p; }

        // called from the per-ship tick for the player's division leaders; true = the division is under an order (handled)
        internal static bool OrdersThink(Ship leader)
        {
            if (OrdersOn == null || !OrdersOn.Value || !GameManager.IsBattle) return false;
            var div = leader.division; if (div == null) return false;
            var o = OrderOf(div, true);
            if (o.Name == null) o.Name = leader.name ?? "division";
            o.Leader = leader;
            // the game's own Screen / Scout orders give autonomy too
            if (o.Order == DivOrder.None && OrdersVanilla.Value)
            {
                bool scr = false, sct = false; try { scr = div.IsScreen(); sct = div.IsScout(); } catch { }
                if (scr || sct) { o.Order = scr ? DivOrder.Screen : DivOrder.Scout; o.FromVanilla = true; o.Issued = false; Log($"{o.Name}: game's {(scr ? "Screen" : "Scout")} order - autonomy within the order"); }
            }
            else if (o.FromVanilla && !o.Issued)
            {
                bool scr = false, sct = false; try { scr = div.IsScreen(); sct = div.IsScout(); } catch { }
                if (!scr && !sct) { Log($"{o.Name}: game's order cleared - manual command"); o.Order = DivOrder.None; o.FromVanilla = false; }
            }
            if (o.Order == DivOrder.None) return false;

            float now = Time.time;
            if (now < o.NextThink) return true;
            o.NextThink = now + Mathf.Max(1f, DipGgFixesMod.BattleThinkSeconds.Value);

            var player = leader.player; var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (player == null || battle == null) return true;
            var enemies = battle.ShipsOf(battle.OppositePlayer(player), true); if (enemies == null) return true;

            // 1.11.203: enemy torpedoes are always evaded, whatever the order (24.10: a cruiser under orders ran into a torpedo)
            if (!States.TryGetValue(div.Pointer, out var es)) { es = new DivState { name = o.Name }; States[div.Pointer] = es; }
            if (now < es.evadeUntil) { Cmd(div, o, P(leader) + es.evadeDir * 3000f); OWhy(o, "turning away from torpedoes"); return true; }
            bool evading = false; Issuing = true; try { evading = TorpedoEvasion.Check(leader, div, es, player, battle, enemies); } finally { Issuing = false; }
            if (evading) { o.Issued = true; o.LastCmd = Time.time; OWhy(o, "turning away from torpedoes"); return true; }

            if (o.Order == DivOrder.Free)
            {
                // 1.11.220 (author, 24.10): the mod's AI logic (torpedo charges, retreats) first; when it only "waits" (no capital target
                //           within the charge distance - 24.10: two CLs with 178 mm guns sat still under Free action), the division fights
                //           on its own: it engages the nearest enemy warship it can face on gun terms, else hunts what cannot hurt it
                Issuing = true; try { Think(leader, true, true); } finally { Issuing = false; }
                DivState ts; bool busy = States.TryGetValue(div.Pointer, out ts) && (ts.charging || ts.closing || ts.fighting);
                string why = ts != null ? ts.reason : null;
                if (busy || (why != null && why != "waiting" && !why.StartsWith("nearest capital") && !why.StartsWith("not a scout"))) { OWhy(o, why ?? "free action"); return true; }
                var mainF = MainBody(player, div, battle); Vector3 meF = P(leader); Vector3 homeF = mainF != null ? P(mainF) : meF;
                Vector3 toF = o.Seen ? (o.LastSeen - homeF) : leader.transform.forward; toF.y = 0f; if (toF.sqrMagnitude < 1f) toF = leader.transform.forward; toF.Normalize();
                if (!Engage(div, o, leader, enemies, mainF, meF, toF)) Hunt(div, o, leader, enemies, mainF, meF, homeF, toF);
                return true;
            }
            if (o.Order == DivOrder.Torpedo)
            {
                if (!leader.haveTorpedoes || !HasTorpedoAmmo(leader)) { OWhy(o, "no torpedoes - free action instead"); Issuing = true; try { Think(leader, true, true); } finally { Issuing = false; } return true; }
                if (!States.TryGetValue(div.Pointer, out var ds)) { ds = new DivState { name = o.Name }; States[div.Pointer] = ds; }
                Issuing = true; try { TorpedoCharge(leader, div, ds, Doctrines.For(player), enemies, false); } finally { Issuing = false; }
                OWhy(o, ds.reason ?? "torpedo attack"); return true;
            }

            var main = MainBody(player, div, battle);
            Vector3 me = P(leader);
            Vector3 home = main != null ? P(main) : me;
            // visible enemies: nearest light, nearest heavy, their centre
            Ship nearLight = null, nearHeavy = null; float dl = float.MaxValue, dh = float.MaxValue; Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                bool vis = false; try { vis = e.IsVisibleFor(leader) || (main != null && e.IsVisibleFor(main)); } catch { }
                if (!vis) continue;
                var ep = P(e); sum += ep; n++;
                bool unarmed = false; try { var stn = e.shipType != null ? e.shipType.name : ""; unarmed = stn == "tr" || stn == "amc" || GunRange(e) <= 0f; } catch { }
                if (unarmed) continue; // 1.11.201: transports are reported, not shadowed (24.10: the scout sat 12 km from a convoy "at the edge of his gun range")
                if (IsLight(e)) { float d = (ep - home).magnitude; if (d < dl) { dl = d; nearLight = e; } }
                else { float d = (ep - me).magnitude; if (d < dh) { dh = d; nearHeavy = e; } }
            }
            if (n > 0) { o.LastSeen = sum / n; o.Seen = true; }
            Vector3 toEnemy = o.Seen ? (o.LastSeen - home) : (main != null ? main.transform.forward : leader.transform.forward);
            toEnemy.y = 0f; if (toEnemy.sqrMagnitude < 1f) toEnemy = leader.transform.forward; toEnemy.Normalize();
            float leash = o.Order == DivOrder.Scout ? Mathf.Max(Leash.Value * 2.5f, ScoutSearch.Value * 1000f + 3000f) : Leash.Value; // 1.11.201: the scout leash covers the search distance
            float dmg = DivDamage(div);

            // badly damaged - fall back behind the main body
            if (dmg >= BreakOff.Value && main != null)
            {
                var safe = home - toEnemy * 2500f;
                if ((me - safe).magnitude > 600f) Cmd(div, o, safe);
                OWhy(o, "damaged - falls back behind the main body"); return true;
            }

            if (o.Order == DivOrder.Hunt) { Hunt(div, o, leader, enemies, main, me, home, toEnemy); return true; }

            if (o.Order == DivOrder.Screen)
            {
                // torpedo craft and light forces coming at the main body: intercept them within the leash
                float threatR = 9000f;
                if (nearLight != null && dl <= threatR && (P(nearLight) - home).magnitude <= leash)
                {
                    Cmd(div, o, P(nearLight)); OWhy(o, $"intercepting {nearLight.name} ({nearLight.shipType.name}) coming at the main body"); return true; // 1.11.158: no distance in the reason (logged every 1.5 s)
                }
                if (o.FromVanilla && !o.Issued) { OWhy(o, "on station (game's screen position)"); return true; }
                // own station: between the main body and the enemy, closer in when enemy heavy guns reach it
                float st = 2500f;
                var station = home + toEnemy * st;
                if (nearHeavy != null) { float hr = GunRange(nearHeavy); if (hr > 0f && (P(nearHeavy) - station).magnitude < hr * 0.8f) station = home + toEnemy * 1200f; }
                if ((me - station).magnitude > 700f) Cmd(div, o, station);
                OWhy(o, main != null ? $"screening {main.name}" : "screening - no main body, holding position"); return true;
            }

            // Scout. 1.11.158: the leash always leaves room for contact at the edge of the enemy's gun range (auto test 23.10:
            // the scout swung between "too far from the main body" and "opening the range" - leash 15 km, contact at 21 km)
            float wantH = 0f; if (nearHeavy != null) { float hr0 = GunRange(nearHeavy); wantH = Mathf.Clamp(hr0 > 0f ? hr0 * 1.05f : 12000f, 7000f, 25000f); }
            leash = Mathf.Max(leash, wantH + 6000f);
            // 1.11.209: hysteresis and contact - in contact with enemy heavy ships the scout may stay out to 1.5 x the leash (shadowing is its
            //           job); once it closes up it keeps closing until it is inside 0.7 x the leash (24.10: it switched between "too far" and
            //           "shadowing" every 15-30 s)
            float fromHome = (me - home).magnitude; float limit = nearHeavy != null ? leash * 1.5f : leash;
            if (main != null && (fromHome > limit || o.ClosingUp && fromHome > leash * 0.7f))
            { o.ClosingUp = true; Cmd(div, o, home + toEnemy * (leash * 0.6f)); OWhy(o, "too far from the main body - closing up"); return true; }
            o.ClosingUp = false;
            if (nearLight != null && (P(nearLight) - me).magnitude < 5000f && MainCaliber(nearLight) > MainCaliber(leader) * 1.2f && main != null)
            { Cmd(div, o, home + toEnemy * 1500f); OWhy(o, $"stronger light force ({nearLight.name}) - falls back to the main body"); return true; }
            if (nearHeavy != null)
            {
                float hr = GunRange(nearHeavy); float want = Mathf.Clamp(hr > 0f ? hr * 1.05f : 12000f, 7000f, 25000f);
                Vector3 hp = P(nearHeavy); Vector3 away = me - hp; away.y = 0f; if (away.sqrMagnitude < 1f) away = -toEnemy; away.Normalize();
                if (dh < want * 0.9f) { Cmd(div, o, me + away * 3000f); OWhy(o, $"keeping contact with {nearHeavy.name} - opening the range"); }
                else if (dh > want * 1.35f) { Cmd(div, o, hp + away * want); OWhy(o, $"keeping contact with {nearHeavy.name} - closing to the edge of his gun range"); }
                else { var side = Vector3.Cross(Vector3.up, away); Cmd(div, o, me + side * 2000f); OWhy(o, $"shadowing {nearHeavy.name} at the edge of his gun range"); }
                return true;
            }
            if (o.FromVanilla && !o.Issued && !o.Seen) { OWhy(o, "searching (game's scout position)"); return true; }
            // 1.11.201: the scout ranges out - up to the scout leash ahead of the main body (ScoutSearchKm), sweeping left and right of the
            //           expected enemy bearing in turns of a few minutes (24.10: the scout stayed a few km from the flagship)
            float reach = Mathf.Min(leash * 0.9f, ScoutSearch.Value * 1000f);
            int legIdx = (int)(Time.time / 180f); float sweep = (legIdx % 3 == 1 ? 1f : legIdx % 3 == 2 ? -1f : 0f) * 35f * Mathf.Deg2Rad;
            Vector3 dirS = new Vector3(toEnemy.x * Mathf.Cos(sweep) - toEnemy.z * Mathf.Sin(sweep), 0f, toEnemy.x * Mathf.Sin(sweep) + toEnemy.z * Mathf.Cos(sweep));
            var ahead = home + dirS * reach;
            if ((me - ahead).magnitude > 1500f) Cmd(div, o, ahead);
            OWhy(o, (o.Seen ? "searching towards the last known enemy position" : "searching ahead of the main body") + (sweep != 0f ? (sweep > 0f ? " - sweeping to port" : " - sweeping to starboard") : "")); return true;
        }

        // ---- 1.11.220 (author, 24.10): Free action - a gun fight the division can take: the nearest visible enemy warship whose main
        //      calibre is not above 1.25 x ours (a light division never picks a capital ship); it closes to 0.7 x its own gun range on the
        //      side away from the enemy main body and fights there, keeping outside the torpedo reach of torpedo craft. Returns false
        //      when there is no such target (the caller falls back to Hunt). ----
        static bool Engage(Division div, OrderSt o, Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, Ship main, Vector3 me, Vector3 toEnemy)
        {
            float myCal = MainCaliber(leader), myR = GunRange(leader); if (myR <= 0f) return false;
            bool light = IsLight(leader);
            Ship target = null; float best = float.MaxValue; var torpCraft = new List<Ship>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                bool vis = false; try { vis = e.IsVisibleFor(leader) || (main != null && e.IsVisibleFor(main)); } catch { }
                if (!vis) continue;
                bool tr = false; try { var stn = e.shipType != null ? e.shipType.name : ""; tr = stn == "tr" || stn == "amc"; } catch { }
                if (tr || GunRange(e) <= 0f) continue;                       // unarmed: Hunt's business
                try { if (IsLight(e) && e.haveTorpedoes && HasTorpedoAmmo(e)) torpCraft.Add(e); } catch { }
                if (light && IsCapital(e)) continue;                           // a light division does not pick a gun fight with a capital ship
                float ec = MainCaliber(e); if (ec > myCal * 1.25f) continue;  // outgunned
                float d = (P(e) - me).magnitude; float dmg = 0f; try { dmg = e.DamageEstimation(); } catch { }
                float score = d * (1f - 0.4f * Mathf.Clamp01(dmg));            // damaged ships first
                if (score < best) { best = score; target = e; }
            }
            if (target == null) return false;
            float want = myR * 0.7f;
            Vector3 tp = P(target); Vector3 away = me - tp; away.y = 0f; if (away.sqrMagnitude < 1f) away = -toEnemy; away.Normalize();
            Vector3 goal = tp + away * want;
            foreach (var t in torpCraft) { if (t.Pointer == target.Pointer) continue; float tr2 = 0f; try { tr2 = TorpedoRange(t); } catch { } Vector3 tq = P(t); if ((goal - tq).magnitude < tr2 + 1500f) { Vector3 off = goal - tq; off.y = 0f; if (off.sqrMagnitude < 1f) off = away; goal = tq + off.normalized * (tr2 + 1500f); } }
            float dt = (tp - me).magnitude;
            if ((me - goal).magnitude > 700f) Cmd(div, o, goal);
            OWhy(o, $"engaging {target.name} ({target.shipType.name}, {MainCaliber(target):0} mm) - {(dt > want * 1.3f ? "closing to gun range" : dt < want * 0.6f ? "opening to the fighting range" : "fighting at the chosen range")}");
            return true;
        }

        // ---- 1.11.210 (author, 24.10): Hunt - the division goes for enemies that cannot hurt it (transports, ships whose guns are too
        //      small for it and that carry no torpedoes), closes in for quick results, and keeps outside the reach of every enemy that can
        //      hurt it (gun range of ships with guns that matter, torpedo range of ships with torpedoes, plus a margin) ----
        static bool Threat(Ship e, Ship me)
        {
            try
            {
                if (e.haveTorpedoes && HasTorpedoAmmo(e)) return true;
                float ec = MainCaliber(e), mc = MainCaliber(me);
                if (GunRange(e) <= 0f || ec <= 0f) return false;
                return ec >= mc * 0.6f || ec >= 150f; // guns of 6 in and more, or within 60% of our own calibre, can hurt us
            }
            catch { return true; }
        }
        static float Reach(Ship e) { float r = 0f; try { if (e.haveTorpedoes && HasTorpedoAmmo(e)) r = TorpedoRange(e); } catch { } return Math.Max(r, GunRange(e)); }
        static void Hunt(Division div, OrderSt o, Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, Ship main, Vector3 me, Vector3 home, Vector3 toEnemy)
        {
            Ship prey = null; float bestScore = float.MaxValue; var threats = new List<Ship>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                bool vis = false; try { vis = e.IsVisibleFor(leader) || (main != null && e.IsVisibleFor(main)); } catch { }
                if (!vis) continue;
                if (Threat(e, leader)) { threats.Add(e); continue; }
                bool tr = false; try { var stn = e.shipType != null ? e.shipType.name : ""; tr = stn == "tr" || stn == "amc"; } catch { }
                float d = (P(e) - me).magnitude; float score = d * (tr ? 0.7f : 1f); // transports first, then the nearest
                if (score < bestScore) { bestScore = score; prey = e; }
            }
            // safe distance from a threat: its reach plus a margin of 1.5 km (torpedo craft: 2 km)
            Func<Vector3, Ship> danger = pos => { foreach (var t in threats) { float m = 1500f; try { if (t.haveTorpedoes) m = 2000f; } catch { } if ((P(t) - pos).magnitude < Reach(t) + m) return t; } return null; };
            var inside = danger(me);
            if (inside != null)
            {
                Vector3 away = me - P(inside); away.y = 0f; if (away.sqrMagnitude < 1f) away = -toEnemy; away.Normalize();
                Cmd(div, o, me + away * 4000f); OWhy(o, $"inside the reach of {inside.name} - opening the range"); return;
            }
            if (prey == null)
            {
                var st = home + toEnemy * 2500f; if (danger(st) != null) st = home;
                if ((me - st).magnitude > 800f) Cmd(div, o, st);
                OWhy(o, threats.Count > 0 ? "no prey that cannot hurt us - waiting by the main body" : "searching for prey"); return;
            }
            // close in: just outside the prey's own guns (it cannot hurt us, but there is no reason to take hits), at least 2.5 km
            float preyR = GunRange(prey); float want = Mathf.Max(2500f, preyR > 0f ? preyR + 500f : 0f);
            Vector3 pp = P(prey), dirToMe = me - pp; dirToMe.y = 0f; if (dirToMe.sqrMagnitude < 1f) dirToMe = -toEnemy; dirToMe.Normalize();
            Vector3 goal = pp + dirToMe * want;
            // never into the reach of a threat: walk the goal back towards our own position until it is safe
            var blocker = danger(goal);
            for (int k = 0; blocker != null && k < 8; k++) { goal = Vector3.Lerp(goal, me, 0.25f); blocker = danger(goal); }
            if (blocker != null) { OWhy(o, $"{prey.name} is covered by {blocker.name} - holding outside his reach"); return; }
            if ((me - goal).magnitude > 600f) Cmd(div, o, goal);
            float dp = (pp - me).magnitude;
            OWhy(o, $"hunting {prey.name} ({prey.shipType.name}) - {(dp > want + 800f ? "closing in" : "engaging")}");
        }

        // ---- panel and keys ----
        static GUIStyle oTitle; static int oErrors; static bool oFolded;
        internal static void OrdersTick()
        {
            try
            {
                if (OrdersOn == null || !OrdersOn.Value || !GameManager.IsBattle) return;
                if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) lastInput = Time.realtimeSinceStartup;
                if (!(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return;
                int k = -1;
                if (Input.GetKeyDown(KeyCode.Alpha1)) k = 0; else if (Input.GetKeyDown(KeyCode.Alpha2)) k = 1; else if (Input.GetKeyDown(KeyCode.Alpha3)) k = 2; else if (Input.GetKeyDown(KeyCode.Alpha4)) k = 3; else if (Input.GetKeyDown(KeyCode.Alpha5)) k = 4; else if (Input.GetKeyDown(KeyCode.Alpha6)) k = 5;
                if (k < 0) return;
                var div = SelectedDivision(); if (div != null) SetOrder(div, (DivOrder)k, "Alt+" + (k + 1));
            }
            catch (Exception e) { if (oErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] orders keys: " + e.Message); }
        }
        static Division SelectedDivision()
        {
            try
            {
                var ui = G.ui; if (ui == null) return null; var s = ui.selectedShipMain;
                if (s == null || s.player == null || !s.player.isMain || s.isSinking || s.isDead) return null;
                return s.division;
            }
            catch { return null; }
        }
        internal static void OrdersOnGUI()
        {
            if (OrdersOn == null || !OrdersOn.Value || !GameManager.IsBattle) return;
            try { OrderTags(); } catch (Exception e) { if (oErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] order tags: " + e.Message); }
            try
            {
                var div = SelectedDivision(); if (div == null) return;
                if (oTitle == null)
                {
                    oTitle = new GUIStyle(); oTitle.fontSize = 12; oTitle.richText = true; oTitle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                }
                var cur = CurrentOrder(div);
                var o = OrderOf(div, false);
                // 1.11.197 (author, 24.10): the panel sat over the sea in front of the ship cards - now a strip at the top of the
                //          screen under the time controls, and it folds to a small tab (the Alt keys work either way)
                float w = 700f, h = 58f; float x = (UiScale.W - w) / 2f, y = 54f; // 1.11.202: wider - "Torpedo attack" was cut off
                if (oFolded)
                {
                    var tab = new Rect(x + w - 150f, y, 150f, 20f);
                    if (GUI.Button(tab, $"Orders: {OrderNames[(int)cur]} ▾")) oFolded = false;
                    return;
                }
                GUI.Box(new Rect(x, y, w, h), "");
                string why = o != null && o.Order != DivOrder.None && o.Reason != null ? " - " + o.Reason : "";
                if (why.Length > 60) why = why.Substring(0, 60) + "...";
                GUI.Label(new Rect(x + 8f, y + 4f, w - 40f, 18f), $"<b>Division orders</b> (Alt+1..6): {OrderNames[(int)cur]}{why}", oTitle);
                if (GUI.Button(new Rect(x + w - 26f, y + 4f, 20f, 18f), "▴")) oFolded = true;
                float bw = (w - 16f - 4f * 5f) / 6f;
                for (int i = 0; i < 6; i++)
                {
                    var r = new Rect(x + 8f + i * (bw + 4f), y + 26f, bw, 26f);
                    bool active = i == (int)cur;
                    var oldBg = GUI.backgroundColor; if (active) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f); // 1.11.202: the active order stands out
                    if (GUI.Button(r, active ? ">> " + OrderNames[i] + " <<" : OrderNames[i])) SetOrder(div, (DivOrder)i, "panel");
                    GUI.backgroundColor = oldBg;
                }
            }
            catch (Exception e) { if (oErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] orders panel: " + e.Message); }
        }
        // 1.11.202: a tag above every player's division under an order - the order and what the division is doing right now
        static GUIStyle oTag;
        static void OrderTags()
        {
            if (orders.Count == 0) return;
            var cam = Camera.main; if (cam == null) return;
            if (oTag == null) { oTag = new GUIStyle(); oTag.fontSize = 12; oTag.richText = true; oTag.wordWrap = true; oTag.alignment = TextAnchor.UpperCenter; oTag.normal.textColor = new Color(0.75f, 1f, 0.8f); }
            float W = UiScale.W, H = UiScale.H;
            foreach (var kv in orders)
            {
                var o = kv.Value; if (o == null || o.Order == DivOrder.None || o.Leader == null) continue;
                Ship s = o.Leader; bool gone = true; try { gone = s.isDead || s.isSinking; } catch { }
                if (gone) continue;
                Vector3 wp; try { wp = s.transform.position; } catch { continue; }
                var sp = cam.WorldToScreenPoint(wp + Vector3.up * 30f); if (sp.z <= 0f) continue;
                float w = 260f, h = 34f;
                var r = new Rect(sp.x / UiScale.F - w / 2f, H - sp.y / UiScale.F - h - 34f, w, h);
                if (r.yMax < 0 || r.y > H || r.xMax < 0 || r.x > W) continue;
                string doing = o.Reason ?? "awaiting the first decision";
                float since = Time.time - o.LastCmd; string cmd = o.LastCmd > 0f && since < 120f ? $" (course set {since:0} s ago)" : "";
                string txt = $"<b>{OrderNames[(int)o.Order]}</b>: {doing}{cmd}";
                GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 4f, r.y + 3f, r.width - 8f, r.height - 6f), txt, oTag);
            }
        }
    }

    // a move order the player gives a division under an autonomous order takes command back
    [HarmonyPatch(typeof(Division), "MoveTo")]
    internal static class Patch_DivisionMoveTo_Orders
    {
        static void Prefix(Division __instance) { try { BattleAi.PlayerCommand(__instance, "move order", false); } catch { } }
    }
    [HarmonyPatch(typeof(Division), "Retreat")]
    internal static class Patch_DivisionRetreat_Orders
    {
        static void Prefix(Division __instance, bool __0) { try { if (__0) BattleAi.PlayerCommand(__instance, "retreat order", false); } catch { } } // 1.11.203
    }
    [HarmonyPatch(typeof(Division), "MoveDir")]
    internal static class Patch_DivisionMoveDir_Orders
    {
        static void Prefix(Division __instance) { try { BattleAi.PlayerCommand(__instance, "course order"); } catch { } }
    }

    // 1.11.216 (author, 24.10): the division card at the bottom of the battle screen names the mod's order ("Screen", "Hunt", ...)
    // instead of the game's "Battle Line" while an order is in force. The card's status text is found once by its game text.
    internal static class DivisionCardLabel
    {
        // 1.11.218: the status text may be a TextMeshPro or a legacy UI.Text; both are searched, and the texts found are logged once
        static readonly Dictionary<IntPtr, Component> labels = new Dictionary<IntPtr, Component>();
        static readonly Dictionary<IntPtr, string> written = new Dictionary<IntPtr, string>();
        static HashSet<string> gameTexts; static int errors, dumped;
        static void Keys()
        {
            if (gameTexts != null) return; gameTexts = new HashSet<string>();
            foreach (var k in new[] { "$Ui_Division_BattleLine", "$Ui_ShipControls_Follow", "$Ui_ShipControls_Scout", "$Ui_ShipControls_Screen", "$Ui_ShipControls_Retreat", "$Ui_Skirmish_AI" })
            { try { var t = LocalizeManager.Localize(k); if (!string.IsNullOrEmpty(t)) gameTexts.Add(t.Trim()); } catch { } }
        }
        static string TextOf(Component c) { try { var tm = c.TryCast<Il2CppTMPro.TMP_Text>(); if (tm != null) return tm.text; var ut = c.TryCast<UnityEngine.UI.Text>(); if (ut != null) return ut.text; } catch { } return null; }
        static void SetText(Component c, string s) { try { var tm = c.TryCast<Il2CppTMPro.TMP_Text>(); if (tm != null) { tm.text = s; return; } var ut = c.TryCast<UnityEngine.UI.Text>(); if (ut != null) ut.text = s; } catch { } }
        internal static void Apply(UIDivision u)
        {
            try
            {
                if (u == null || !GameManager.IsBattle) return;
                var dv = u.CurrentDivision; if (dv == null) return;
                var ord = BattleAi.CurrentOrder(dv);
                Component lbl;
                if (!labels.TryGetValue(u.Pointer, out lbl) || lbl == null)
                {
                    Keys();
                    var all = new List<Component>();
                    try { var a = u.GetComponentsInChildren<Il2CppTMPro.TMP_Text>(true); if (a != null) for (int i = 0; i < a.Count; i++) if (a[i] != null) all.Add(a[i]); } catch { }
                    try { var a = u.GetComponentsInChildren<UnityEngine.UI.Text>(true); if (a != null) for (int i = 0; i < a.Count; i++) if (a[i] != null) all.Add(a[i]); } catch { }
                    string last; written.TryGetValue(u.Pointer, out last);
                    var seen = new List<string>();
                    for (int i = 0; i < all.Count; i++)
                    {
                        var t = all[i]; if (u.Name != null && t.Pointer == u.Name.Pointer) continue;
                        string s = TextOf(t); s = s != null ? s.Trim() : ""; seen.Add(t.gameObject.name + "='" + s + "'");
                        if (gameTexts.Contains(s) || (last != null && s == last)) { lbl = t; break; }
                    }
                    if (dumped++ < 2) MelonLogger.Msg("[DIP GG Fixes][Battle] division card texts: " + string.Join(", ", seen) + (lbl != null ? " - status text found" : " - status text NOT found; game texts: " + string.Join("|", gameTexts)));
                    if (lbl == null) return;
                    labels[u.Pointer] = lbl; if (labels.Count > 200) labels.Clear();
                }
                if (ord == BattleAi.DivOrder.None) return;
                string want = BattleAi.OrderLabel(ord);
                if (TextOf(lbl) != want) { SetText(lbl, want); written[u.Pointer] = want; }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Battle] division card label: " + e.Message); }
        }
    }
    [HarmonyPatch(typeof(UIDivision), "RefreshUI", new Type[] { typeof(bool) })]
    internal static class Patch_UIDivisionRefresh_OrderLabel { static void Postfix(UIDivision __instance) { DivisionCardLabel.Apply(__instance); } }
    [HarmonyPatch(typeof(UIDivision), "LateUpdate")]
    internal static class Patch_UIDivisionLate_OrderLabel { static void Postfix(UIDivision __instance) { DivisionCardLabel.Apply(__instance); } }
}
