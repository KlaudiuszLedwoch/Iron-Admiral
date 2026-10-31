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
        // 1.11.494 (author 31.10, 09:27: "a button for keeping a set distance from the enemy"): Keep range - the division holds the
        // distance chosen in the panel from the nearest enemy warship it can see: it steers round him on that circle (broadside on,
        // the side it is already turning to), closes in when it is further out and opens the range when it is closer. The distance
        // starts at the range to that enemy when the order is given (2-30 km, 0.5 km steps) and is changed with the panel's
        // -1 km / +1 km buttons. Torpedo evasion and the damaged break-off work as under the other orders. Alt+8.
        internal enum DivOrder { None, Screen, Scout, Torpedo, Free, Hunt, Finish, Keep }
        internal class OrderSt { public DivOrder Order; public bool FromVanilla; public bool Issued; public float NextThink; public string Name; public string Reason; public Vector3 LastSeen; public bool Seen; public Ship Leader; public float LastCmd; public Vector3 LastTarget; public bool ClosingUp; }
        static readonly Dictionary<IntPtr, OrderSt> orders = new Dictionary<IntPtr, OrderSt>();
        static IntPtr ordersBattle;
        internal static bool Issuing;   // our own commands - the Division.MoveTo/MoveDir patch ignores them
        static readonly string[] OrderNames = { "Manual", "Screen", "Scout", "Torpedo attack", "Free action", "Hunt", "Finish off", "Keep range" };
        static readonly Dictionary<IntPtr, float> keepRange = new Dictionary<IntPtr, float>();   // division -> distance to hold (m)
        static readonly HashSet<IntPtr> keepCircle = new HashSet<IntPtr>();   // divisions steering round the enemy on the circle (1.11.495)
        internal static float KeepRangeOf(Division div) { return div != null && keepRange.TryGetValue(div.Pointer, out var r) ? r : 0f; }

        internal static MelonPreferences_Entry<bool> OrdersOn, OrdersVanilla;
        internal static MelonPreferences_Entry<float> Leash, BreakOff, ScoutSearch;
        internal static MelonPreferences_Entry<bool> ScoutSmoke; // 1.11.256
        internal static MelonPreferences_Entry<float> FinishDamage; // 1.11.285 (#1)
        internal static MelonPreferences_Entry<bool> TorpSplit; // 1.11.361
        static readonly Dictionary<IntPtr, bool> splitChoice = new Dictionary<IntPtr, bool>(); // division -> split chosen in the panel
        static bool SplitWanted(Division div) { if (div != null && splitChoice.TryGetValue(div.Pointer, out var v)) return v; return TorpSplit == null || TorpSplit.Value; }
        internal static void CreateOrderPrefs(MelonPreferences_Category cat)
        {
            OrdersOn = cat.CreateEntry("DivisionOrders", true, null, "Battle: orders for the player's divisions with autonomy (screen, scout, torpedo attack, free action) - the division is led by the AI within the limits of the order; panel in battle or Alt+1..Alt+5");
            OrdersVanilla = cat.CreateEntry("DivisionOrdersVanilla", true, null, "Battle: the game's own Screen and Scout orders also give the division autonomy");
            Leash = cat.CreateEntry("DivisionLeash", 6000f, null, "Battle: how far (m) a screening division may move away from the main body (scouts: 2.5 times as far)");
            ScoutSearch = cat.CreateEntry("ScoutSearchKm", 20f, null, "Battle: how far (km) ahead of the main body a scouting division ranges while searching for the enemy (sweeping left and right of the expected bearing)"); // 1.11.201
            ScoutSmoke = cat.CreateEntry("ScoutTowardsSmoke", true, null, "Battle: a scouting division heads for smoke reported by the lookouts (an enemy ship sighted but not identified yet) and closes until she can be made out"); // 1.11.256
            FinishDamage = cat.CreateEntry("DivisionFinishDamage", 0.4f, null, "Battle: estimated damage (0-1) from which an enemy ship counts as crippled for the Finish off order (Alt+7)"); // 1.11.285
            // 1.11.361 (author 29.10): the split for an attack from two sides can be switched off - default here, per division in the panel
            TorpSplit = cat.CreateEntry("DivisionTorpedoSplit", true, null, "Battle: under the Torpedo attack order a division of two or more ships splits in two and attacks from two sides (the detached part joins again after its salvo); the orders panel switches it for each division");
            BreakOff = cat.CreateEntry("DivisionBreakOffDamage", 0.5f, null, "Battle: average damage (0-1) at which a screening or scouting division breaks off and falls back behind the main body");
            // 1.11.330 (#69): the panel starts folded to a small tab, a key folds / unfolds it, the state is kept between battles
            PanelFolded = cat.CreateEntry("DivisionOrdersPanelFolded", true, null, "Battle: the division orders panel is folded to a small tab (kept between battles; written by the mod)");
            PanelKey = cat.CreateEntry("DivisionOrdersPanelKey", "F5", null, "Battle: key that folds / unfolds the division orders panel (Unity KeyCode name)");
            KeyCode k; if (Enum.TryParse(PanelKey.Value, true, out k)) panelKey = k;
        }
        internal static MelonPreferences_Entry<bool> PanelFolded;
        internal static MelonPreferences_Entry<string> PanelKey;
        static KeyCode panelKey = KeyCode.F5;
        static bool oFolded
        {
            get { return PanelFolded == null || PanelFolded.Value; }
            set { if (PanelFolded == null || PanelFolded.Value == value) return; PanelFolded.Value = value; try { MelonPreferences.Save(); } catch { } }
        }

        static void OrdersReset()
        {
            IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
            if (bp != ordersBattle) { ordersBattle = bp; orders.Clear(); keepRange.Clear(); keepCircle.Clear(); }
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
            if (ord != DivOrder.Hunt) HuntShells(div, false, null); // 1.11.301: shells back after Hunt
            if (ord != DivOrder.None) try { Issuing = true; if (div.isRetreating || div.retreat) div.Retreat(false); } catch { } finally { Issuing = false; }
            if (ord == DivOrder.Keep && !keepRange.ContainsKey(div.Pointer))
            {
                float r = 12000f;
                try { var l = div.leader; var e = l != null ? KeepTarget(l, null) : null; if (e != null) r = (P(e) - P(l)).magnitude; } catch { }
                keepRange[div.Pointer] = Mathf.Clamp(Mathf.Round(r / 500f) * 500f, 2000f, 30000f);
                Log($"{o.Name}: keep range {keepRange[div.Pointer] / 1000f:0.0} km");
            }
        }
        internal static void KeepRangeStep(Division div, float step)
        {
            if (div == null) return;
            float r = KeepRangeOf(div); if (r <= 0f) r = 12000f;
            keepRange[div.Pointer] = Mathf.Clamp(r + step, 2000f, 30000f);
            var o = OrderOf(div, false); Log($"{(o != null ? o.Name : "division")}: keep range {keepRange[div.Pointer] / 1000f:0.0} km (panel)");
        }
        // 1.11.496 (#117): the "IZ" button of Keep range - the distance is set to the middle of the leader's immune zone against the main
        // battery of the enemy it keeps its distance from (beam-on); a zone that reaches to the enemy gun's maximum range is held 1.5 km
        // inside its inner limit's far side, never at the edge of his reach
        internal static void KeepRangeImmune(Division div)
        {
            if (div == null) return;
            var o = OrderOf(div, false); string name = o != null ? o.Name : "division";
            try
            {
                var l = div.leader; var e = l != null ? KeepTarget(l, null) : null;
                if (e == null) { Log($"{name}: keep range IZ - no enemy in sight"); return; }
                var z = ImmuneZone.Battle(l, e);
                if (!z.Any) { Log($"{name}: keep range IZ - no immune zone against {e.name} ({ImmuneZone.Text(z)}); distance unchanged"); if (o != null) OWhy(o, $"no immune zone against {e.name} - distance unchanged"); return; }
                float r = z.ToMax ? Mathf.Min(z.Outer, z.Inner + 1500f) : (z.Inner + z.Outer) / 2f;
                keepRange[div.Pointer] = Mathf.Clamp(Mathf.Round(r / 500f) * 500f, 2000f, 30000f);
                Log($"{name}: keep range {keepRange[div.Pointer] / 1000f:0.0} km - inside the immune zone {ImmuneZone.Text(z)} against {e.name}");
            }
            catch (Exception ex) { Log($"{name}: keep range IZ: {ex.Message}"); }
        }
        // the enemy the division keeps its distance from: the nearest visible enemy warship (transports and unarmed ships do not count)
        static Ship KeepTarget(Ship leader, Ship main)
        {
            Ship best = null; float bd = float.MaxValue;
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null || leader.player == null) return null;
                var enemies = battle.ShipsOf(battle.OppositePlayer(leader.player), true); if (enemies == null) return null;
                Vector3 me = P(leader);
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                    bool vis = false; try { vis = e.IsVisibleFor(leader) || (main != null && e.IsVisibleFor(main)); } catch { }
                    if (!vis) continue;
                    bool unarmed = false; try { var stn = e.shipType != null ? e.shipType.name : ""; unarmed = stn == "tr" || stn == "amc" || GunRange(e) <= 0f; } catch { }
                    if (unarmed) continue;
                    float d = (P(e) - me).magnitude; if (d < bd) { bd = d; best = e; }
                }
            }
            catch { }
            return best;
        }
        static int keepLogs; static float keepLogAt = -100f;
        // 1.11.497 (author's test 31.10, 10:24: three divisions on Keep range bunched together - they steered for points on the same
        // line from the same enemy): the goal is pushed away from every other own division leader within 2.5 km (and from its goal
        // under Keep range), so the divisions keep about 2 km apart
        static Vector3 Apart(Division div, Ship leader, Vector3 goal)
        {
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null || leader.player == null) return goal;
                var mine = battle.ShipsOf(leader.player, true); if (mine == null) return goal;
                Vector3 me = P(leader), push = Vector3.zero;
                for (int i = 0; i < mine.Count; i++)
                {
                    var s = mine[i]; if (s == null || s.isDead || s.isSinking || !s.isDivLeader) continue;
                    var d2 = s.division; if (d2 == null || d2.Pointer == div.Pointer) continue;
                    Vector3 other = P(s);
                    if (orders.TryGetValue(d2.Pointer, out var oo) && oo.Order == DivOrder.Keep && oo.LastCmd > 0f) other = (other + oo.LastTarget) / 2f;
                    Vector3 away = goal - other; away.y = 0f; float d = away.magnitude;
                    if (d < 2500f) { if (d < 1f) away = me - P(s); away.y = 0f; if (away.sqrMagnitude < 1f) away = Vector3.right; push += away.normalized * (2500f - d); }
                }
                return goal + push;
            }
            catch { return goal; }
        }
        static void KeepRange(Division div, OrderSt o, Ship leader, Ship main, Vector3 me, Vector3 home, Vector3 toEnemy)
        {
            float R = KeepRangeOf(div); if (R <= 0f) { R = 12000f; keepRange[div.Pointer] = R; }
            var e = KeepTarget(leader, main);
            if (e == null)
            {
                // nobody in sight: towards the last seen enemy until he is at the distance, else wait by the main body
                if (o.Seen && (o.LastSeen - me).magnitude > R + 1000f) { Cmd(div, o, Apart(div, leader, o.LastSeen - (o.LastSeen - me).normalized * R)); OWhy(o, $"no enemy in sight - closing to {R / 1000f:0.#} km of his last position"); return; }
                // 1.11.497 (author's test 31.10, 10:24: "the ships bunch together"): with nobody in sight every division went to the same
                // point by the main body - now it holds its own course, kept apart from the other divisions
                Vector3 fw = leader.transform.forward; fw.y = 0f; if (fw.sqrMagnitude < 0.01f) fw = toEnemy; fw.Normalize();
                Cmd(div, o, Apart(div, leader, me + fw * 3000f));
                OWhy(o, $"no enemy in sight - keep range {R / 1000f:0.#} km"); return;
            }
            Vector3 ep = P(e), dir = me - ep; dir.y = 0f; float d = dir.magnitude; if (d < 1f) dir = -toEnemy; dir.Normalize();
            float err = d - R;
            Vector3 goal; string doing;
            // 1.11.495 (author's test 31.10, 09:48: "closing in" and "edging in" alternated every 2-3 s at about 3 km off the circle):
            // hysteresis - the division takes the circle within 2.5 km of it and leaves it only beyond 4 km
            bool onCircle = keepCircle.Contains(div.Pointer);
            if (onCircle && Mathf.Abs(err) > 4000f) { keepCircle.Remove(div.Pointer); onCircle = false; }
            else if (!onCircle && Mathf.Abs(err) < 2500f) { keepCircle.Add(div.Pointer); onCircle = true; }
            if (!onCircle)
            {
                goal = ep + dir * R;   // far off the circle: straight to the point on it
                doing = err > 0f ? "closing in" : "opening the range";
            }
            else
            {
                // on the circle: steer round the enemy, broadside on, on the side the division is already turning to
                Vector3 tan = new Vector3(-dir.z, 0f, dir.x);
                Vector3 fwd = leader.transform.forward; fwd.y = 0f;
                if (Vector3.Dot(fwd, tan) < 0f) tan = -tan;
                goal = ep + dir * R + tan * 3000f;
                doing = Mathf.Abs(err) < 750f ? "holding the distance" : err > 0f ? "edging in" : "edging out";
            }
            Cmd(div, o, Apart(div, leader, goal));
            OWhy(o, $"keep {R / 1000f:0.#} km from {e.name} - {doing}");
            if (keepLogs++ < 200 && Time.time - keepLogAt > 20f) { keepLogAt = Time.time; Log($"{o.Name} [Keep range]: at {d / 1000f:0.0} km from {e.name} (wanted {R / 1000f:0.0} km)"); }
        }
        internal static DivOrder CurrentOrder(Division div) { var o = OrderOf(div, false); return o != null ? o.Order : DivOrder.None; }
        internal static string OrderLabel(DivOrder o) { return OrderNames[(int)o]; } // 1.11.216

        // the player gave the division a move order himself - command goes back to him
        // 1.11.159: only a command right after a click or key of the player counts (2nd auto test: the game itself gave a
        // retreating division a course order 1 s after "Free action" and the order ended at once)
        internal static float lastInput = -100f;
        internal static bool PlayerInputRecent() { try { return Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1) || Input.anyKey || Time.realtimeSinceStartup - lastInput < 0.6f; } catch { return false; } } // 1.11.250
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
            HuntShells(div, false, null); // 1.11.301
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
                // 1.11.303 (author 26.10, 20:29): a division of two or more ships splits by itself for a cross attack and joins again
                // once the detached part has fired its torpedoes
                if (!splitDone.Contains(div.Pointer)) { splitDone.Add(div.Pointer); if (SplitWanted(div)) SplitForCross(div, leader, o); else Log($"{o.Name}: torpedo attack as one group (split off)"); }
                Issuing = true; OrderedCharge = true; try { TorpedoCharge(leader, div, ds, Doctrines.For(player), enemies, false); } finally { Issuing = false; OrderedCharge = false; }
                if (splitFrom.TryGetValue(div.Pointer, out var orig) && ds.reason == "tubes empty - opening the range") Rejoin(div, orig, o);
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
            if (o.Order == DivOrder.Finish) { FinishOff(div, o, leader, enemies, main, me, home, toEnemy); return true; } // 1.11.285 (#1)
            if (o.Order == DivOrder.Keep) { KeepRange(div, o, leader, main, me, home, toEnemy); return true; } // 1.11.494

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
            // 1.11.256 (author 25.10, 12:40): smoke reported by the lookouts ("Smoke on the horizon", sighted, not identified) - while it
            //           shadows no enemy heavy ship, the scout heads for the nearest smoke within its search range and closes to just
            //           inside the identification range, so the ship can be made out (before: it kept sweeping its search legs)
            if (nearHeavy == null && ScoutSmoke != null && ScoutSmoke.Value)
            {
                Vector3 sp; float idr;
                if (Fog.NearestSmoke(player, home, Mathf.Max(leash, ScoutSearch.Value * 1000f) + 5000f, out sp, out idr))
                {
                    Vector3 to = sp - me; to.y = 0f; float ds = to.magnitude;
                    float stand = Mathf.Clamp(idr * 0.85f, 2000f, 15000f);
                    if (ds > stand + 500f) Cmd(div, o, sp - to.normalized * stand);
                    OWhy(o, $"heading for smoke reported {ds / 1000f:0.0} km away - closing to make her out"); return true;
                }
            }
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
        static readonly Dictionary<IntPtr, float> huntHold = new Dictionary<IntPtr, float>();
        // 1.11.303: cross-attack split - divisions already considered, and detached division -> the division it came from
        static readonly HashSet<IntPtr> splitDone = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, Division> splitFrom = new Dictionary<IntPtr, Division>();
        static void SplitForCross(Division div, Ship leader, OrderSt o)
        {
            try
            {
                var all = new List<Ship>(); var ships = div.ships;
                for (int i = 0; ships != null && i < ships.Count; i++) { var s = ships[i]; if (s != null && !s.isDead && !s.isSinking && !s.IsSurrendered && s.Pointer != leader.Pointer) all.Add(s); }
                if (all.Count < 1) return;                                   // a single ship cannot split
                int take = (all.Count + 1) / 2;                              // the leader keeps the first half, the rest go
                var go = all.GetRange(all.Count - take, take);
                var il = new Il2CppSystem.Collections.Generic.List<Ship>(); foreach (var s in go) il.Add(s);
                Issuing = true;
                try { Ship.DetachShips(il); } catch (Exception e) { Log($"{o.Name}: split - DetachShips failed ({e.Message}), detaching one by one"); foreach (var s in go) { try { if (s.CanDetach()) s.Detach(); } catch { } } }
                finally { Issuing = false; }
                Division nd = null; try { nd = go[0].division; } catch { }
                if (nd == null || nd.Pointer == div.Pointer) { Log($"{o.Name}: split for a cross attack did not happen - the division attacks as one"); return; }
                splitDone.Add(nd.Pointer); splitFrom[nd.Pointer] = div;
                SetOrder(nd, DivOrder.Torpedo, "cross attack - detached part");
                Log($"{o.Name}: splits for a cross torpedo attack - {string.Join(", ", go.ConvertAll(s => s.name))} detached as a second division; they join again after their salvo");
            }
            catch (Exception e) { if (shellErr++ < 3) Log($"{o.Name}: split: {e.Message}"); }
        }
        static void Rejoin(Division nd, Division orig, OrderSt o)
        {
            try
            {
                splitFrom.Remove(nd.Pointer);
                bool origAlive = false; try { origAlive = orig != null && orig.ships != null && orig.ships.Count > 0; } catch { }
                if (!origAlive) { Log($"{o.Name}: the division it split from is gone - stays on its own"); return; }
                var back = new List<Ship>(); var ships = nd.ships;
                for (int i = 0; ships != null && i < ships.Count; i++) { var s = ships[i]; if (s != null && !s.isDead && !s.isSinking) back.Add(s); }
                Issuing = true;
                try { foreach (var s in back) { try { orig.AddShip(s, nd); } catch (Exception e) { Log($"{o.Name}: rejoin of {s.name} failed: {e.Message}"); } } }
                finally { Issuing = false; }
                Log($"{o.Name}: salvo away - {back.Count} ship(s) join their division again to withdraw and reload together");
            }
            catch (Exception e) { if (shellErr++ < 3) Log($"{o.Name}: rejoin: {e.Message}"); }
        }
        // 1.11.301: shells the division used before Hunt switched it to HE against transports (ship -> main, secondary)
        static readonly Dictionary<IntPtr, Ship.ShellType[]> huntShells = new Dictionary<IntPtr, Ship.ShellType[]>();
        static void HuntShells(Division div, bool he, Ship prey)
        {
            try
            {
                var ships = div.ships; if (ships == null) return;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null || s.isDead || s.isSinking) continue;
                    bool saved = huntShells.TryGetValue(s.Pointer, out var prev);
                    if (he)
                    {
                        if (!saved) { huntShells[s.Pointer] = new[] { s.MainShellType, s.SecShellType }; }
                        if (s.MainShellType != Ship.ShellType.He) { s.MainShellType = Ship.ShellType.He; try { s.OnChangeMainShellType(Ship.ShellType.He); } catch { } }
                        if (s.SecShellType != Ship.ShellType.He) { s.SecShellType = Ship.ShellType.He; try { s.OnChangeSecShellType(Ship.ShellType.He); } catch { } }
                        if (!saved) { var was = huntShells[s.Pointer]; Log($"{s.name} [Hunt]: HE in main and secondary guns against the transport {prey.name} (was {was[0]}/{was[1]})"); }
                    }
                    else if (saved)
                    {
                        s.MainShellType = prev[0]; try { s.OnChangeMainShellType(prev[0]); } catch { }
                        s.SecShellType = prev[1]; try { s.OnChangeSecShellType(prev[1]); } catch { }
                        huntShells.Remove(s.Pointer);
                        Log($"{s.name} [Hunt]: prey is no longer a transport - shells back to {prev[0]}/{prev[1]}");
                    }
                }
            }
            catch (Exception e) { if (shellErr++ < 3) Log("[Hunt] shell switch: " + e.Message); }
        }
        static int shellErr;
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
            // 1.11.300 (author 26.10, 20:16, "Hunt works oddly": CA Limburg switched every 2-3 s between "covered - holding outside his
            // reach", "inside the reach - opening the range" and "closing in"): the goal must be safe with a further 1.5 km to spare,
            // after opening the range the division waits 30 s before it tries again, and while the prey is covered it is sent to a
            // point outside the threat's reach instead of drifting on its last course
            Func<Vector3, float, Ship> dangerM = (pos, extra) => { foreach (var t in threats) { float m = 1500f; try { if (t.haveTorpedoes) m = 2000f; } catch { } if ((P(t) - pos).magnitude < Reach(t) + m + extra) return t; } return null; };
            Func<Vector3, Ship> danger = pos => dangerM(pos, 0f);
            float now = Time.time; float holdUntil; huntHold.TryGetValue(div.Pointer, out holdUntil);
            var inside = danger(me);
            if (inside != null)
            {
                Vector3 away = me - P(inside); away.y = 0f; if (away.sqrMagnitude < 1f) away = -toEnemy; away.Normalize();
                Cmd(div, o, me + away * 4000f); OWhy(o, $"inside the reach of {inside.name} - opening the range"); huntHold[div.Pointer] = now + 30f; return;
            }
            if (prey == null)
            {
                var st = home + toEnemy * 2500f; if (danger(st) != null) st = home;
                if ((me - st).magnitude > 800f) Cmd(div, o, st);
                OWhy(o, threats.Count > 0 ? "no prey that cannot hurt us - waiting by the main body" : "searching for prey"); return;
            }
            // close in: just outside the prey's own guns (it cannot hurt us, but there is no reason to take hits), at least 2.5 km
            float preyR = GunRange(prey); float want = Mathf.Max(2500f, preyR > 0f ? preyR + 500f : 0f);
            // 1.11.301 (author 26.10, 20:21: "close in on the transports to 2-3 km and shoot them like ducks, with HE"): transports are
            // approached to 2.5 km whatever they carry, and the division fires HE at them; the earlier shell choice comes back when the
            // prey is no longer a transport
            bool preyTr = false; try { var pst = prey.shipType != null ? prey.shipType.name : ""; preyTr = pst == "tr" || pst == "amc"; } catch { }
            if (preyTr) want = 2500f;
            HuntShells(div, preyTr, prey);
            Vector3 pp = P(prey), dirToMe = me - pp; dirToMe.y = 0f; if (dirToMe.sqrMagnitude < 1f) dirToMe = -toEnemy; dirToMe.Normalize();
            Vector3 goal = pp + dirToMe * want;
            // never into the reach of a threat: walk the goal back towards our own position until it is safe
            var blocker = dangerM(goal, 1500f);
            for (int k = 0; blocker != null && k < 8; k++) { goal = Vector3.Lerp(goal, me, 0.25f); blocker = dangerM(goal, 1500f); }
            if (blocker == null && now < holdUntil) blocker = dangerM(me, 3000f);   // still near the threat it just left
            if (blocker != null)
            {
                Vector3 aw = me - P(blocker); aw.y = 0f; if (aw.sqrMagnitude < 1f) aw = -toEnemy; aw.Normalize();
                Vector3 safe = P(blocker) + aw * (Reach(blocker) + 3500f);
                if ((me - safe).magnitude > 800f) Cmd(div, o, safe);
                OWhy(o, $"{prey.name} is covered by {blocker.name} - holding outside his reach"); return;
            }
            if ((me - goal).magnitude > 600f) Cmd(div, o, goal);
            float dp = (pp - me).magnitude;
            OWhy(o, $"hunting {prey.name} ({prey.shipType.name}) - {(dp > want + 800f ? "closing in" : "engaging")}");
        }

        // ---- 1.11.285 (#1): Finish off - the division goes for enemy ships that are already badly damaged (estimated damage at or above
        //      DivisionFinishDamage) and still afloat: the nearest crippled ship, worse damage first. With torpedoes it closes to torpedo
        //      reach against ships it cannot sink with its guns (capital ships, bigger calibre), otherwise to half its gun range. It keeps
        //      outside the reach of every undamaged enemy that can hurt it (as Hunt does); with no crippled enemy in sight it waits by
        //      the main body. Transports are left to Hunt. ----
        static void FinishOff(Division div, OrderSt o, Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, Ship main, Vector3 me, Vector3 home, Vector3 toEnemy)
        {
            float myCal = MainCaliber(leader), myR = GunRange(leader);
            bool torp = false; float tR = 0f; try { torp = leader.haveTorpedoes && HasTorpedoAmmo(leader); if (torp) tR = TorpedoRange(leader); } catch { }
            bool light = IsLight(leader); float lim = FinishDamage != null ? FinishDamage.Value : 0.4f;
            Ship target = null; float best = float.MaxValue; var threats = new List<Ship>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                bool vis = false; try { vis = e.IsVisibleFor(leader) || (main != null && e.IsVisibleFor(main)); } catch { }
                if (!vis) continue;
                bool tr = false; try { var stn = e.shipType != null ? e.shipType.name : ""; tr = stn == "tr" || stn == "amc"; } catch { }
                float dmg = 0f; try { dmg = e.DamageEstimation(); } catch { }
                bool crippled = !tr && dmg >= lim;
                // a light division without torpedoes does not go for a crippled capital ship unless she is nearly done for
                if (crippled && light && IsCapital(e) && !torp && dmg < 0.75f) crippled = false;
                if (!crippled) { if (!tr && Threat(e, leader)) threats.Add(e); continue; }
                float d = (P(e) - me).magnitude; float score = d * (1.3f - Mathf.Clamp01(dmg));   // worse damage first, then the nearest
                if (score < best) { best = score; target = e; }
            }
            Func<Vector3, Ship> danger = pos => { foreach (var t in threats) { float m = 1500f; try { if (t.haveTorpedoes) m = 2000f; } catch { } if ((P(t) - pos).magnitude < Reach(t) + m) return t; } return null; };
            if (target == null)
            {
                var inside0 = danger(me);
                if (inside0 != null) { Vector3 aw = me - P(inside0); aw.y = 0f; if (aw.sqrMagnitude < 1f) aw = -toEnemy; aw.Normalize(); Cmd(div, o, me + aw * 4000f); OWhy(o, $"no crippled enemy - inside the reach of {inside0.name}, opening the range"); return; }
                var st = home + toEnemy * 2500f; if (danger(st) != null) st = home;
                if ((me - st).magnitude > 800f) Cmd(div, o, st);
                OWhy(o, "no crippled enemy in sight - waiting by the main body"); return;
            }
            bool useTorp = torp && tR > 0f && (IsCapital(target) || MainCaliber(target) > myCal * 1.25f || myR <= 0f);
            float want = useTorp ? Mathf.Max(2000f, tR * 0.7f) : Mathf.Max(2000f, myR > 0f ? myR * 0.5f : 3000f);
            Vector3 tp = P(target), dirToMe = me - tp; dirToMe.y = 0f; if (dirToMe.sqrMagnitude < 1f) dirToMe = -toEnemy; dirToMe.Normalize();
            Vector3 goal = tp + dirToMe * want;
            var blocker = danger(goal);
            for (int k = 0; blocker != null && k < 8; k++) { goal = Vector3.Lerp(goal, me, 0.25f); blocker = danger(goal); }
            if (blocker != null)
            {
                if (danger(me) != null) { Vector3 aw = me - P(blocker); aw.y = 0f; if (aw.sqrMagnitude < 1f) aw = -toEnemy; aw.Normalize(); Cmd(div, o, me + aw * 4000f); OWhy(o, $"{target.name} is covered by {blocker.name} - opening the range"); return; }
                OWhy(o, $"{target.name} is covered by {blocker.name} - holding outside his reach"); return;
            }
            if ((me - goal).magnitude > 600f) Cmd(div, o, goal);
            float dt = (tp - me).magnitude;
            OWhy(o, $"finishing off {target.name} ({target.shipType.name}, badly damaged) - {(dt > want + 800f ? "closing in" : useTorp ? "in torpedo reach" : "engaging")}{(useTorp ? " with torpedoes" : "")}");
        }


        static GUIStyle oTitle; static int oErrors;
        internal static void OrdersTick()
        {
            try
            {
                if (!GameManager.IsBattle) return;
                ShootInfoDiag.Tick(); // 1.11.250
                if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButton(1) || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1)) lastInput = Time.realtimeSinceStartup;
                if (OrdersOn == null || !OrdersOn.Value || BattleScreen.Hidden) return;
                if (Input.GetKeyDown(panelKey)) oFolded = !oFolded; // 1.11.330 (#69)
                if (!(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))) return;
                int k = -1;
                if (Input.GetKeyDown(KeyCode.Alpha1)) k = 0; else if (Input.GetKeyDown(KeyCode.Alpha2)) k = 1; else if (Input.GetKeyDown(KeyCode.Alpha3)) k = 2; else if (Input.GetKeyDown(KeyCode.Alpha4)) k = 3; else if (Input.GetKeyDown(KeyCode.Alpha5)) k = 4; else if (Input.GetKeyDown(KeyCode.Alpha6)) k = 5; else if (Input.GetKeyDown(KeyCode.Alpha7)) k = 6; else if (Input.GetKeyDown(KeyCode.Alpha8)) k = 7;
                if (k < 0) return;
                var div = SelectedDivision(); if (div != null) SetOrder(div, (DivOrder)k, "Alt+" + (k + 1));
            }
            catch (Exception e) { if (oErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] orders keys: " + e.Message); }
        }
        // 1.11.348 (author 28.10, 17:26, campaign battle: "there are no orders" - an enemy battleship was selected, so the panel had
        // no division and disappeared): with an enemy ship or nothing selected the panel shows the last own division selected, or
        // the first own division of the battle
        static Ship lastOwn;
        static bool OwnAlive(Ship s) { try { return s != null && s.player != null && s.player.isMain && !s.isSinking && !s.isDead && s.division != null; } catch { return false; } }
        static Division SelectedDivision()
        {
            try
            {
                var s = BattleSel.Ship(); // 1.11.261: kept after a click on a strip
                if (OwnAlive(s)) { lastOwn = s; return s.division; }
                if (OwnAlive(lastOwn)) return lastOwn.division;
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; var ships = b != null ? b.Ships : null;
                if (ships != null) for (int i = 0; i < ships.Count; i++) { var x = ships[i]; if (OwnAlive(x)) { lastOwn = x; return x.division; } }
                return null;
            }
            catch { return null; }
        }
        // 1.11.362 (author 29.10: "when the player moves the camera the order buttons hide - as long as he holds the camera, even when
        // it is not moving at that moment"): a camera button (right or middle mouse) or a camera key (WASD, Q/E, arrows) held, and
        // 0.4 s after it is let go
        static float camBusyUntil;
        static readonly KeyCode[] camKeys = { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow };
        internal static bool CameraBusy()
        {
            bool held = false;
            try
            {
                if (!(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                {
                    held = Input.GetMouseButton(1) || Input.GetMouseButton(2);
                    if (!held) foreach (var k in camKeys) if (Input.GetKey(k)) { held = true; break; }
                }
            }
            catch { }
            if (held) camBusyUntil = Time.realtimeSinceStartup + 0.4f;
            return Time.realtimeSinceStartup < camBusyUntil;
        }
        internal static void OrdersOnGUI()
        {
            if (OrdersOn == null || !OrdersOn.Value || !GameManager.IsBattle || BattleScreen.Hidden) return; // 1.11.250: not over the result screen / "Play again?"
            if (CameraBusy()) return; // 1.11.362 (author 29.10): panel and order tags hidden while the player works the camera
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
                float w = 900f, h = 44f; /* 1.11.494: 8 orders */ /* 1.11.285: 7 orders */ /* 1.11.266: smaller buttons (author 25.10, 15:02) */ float x = (UiScale.W - w) / 2f, y = 54f; // 1.11.202: wider - "Torpedo attack" was cut off
                if (oFolded)
                {
                    var tab = new Rect(x + w - 190f, y, 190f, 20f);
                    if (GUI.Button(tab, $"Orders: {OrderNames[(int)cur]} ▾ ({panelKey})")) oFolded = false;
                    return;
                }
                GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
                string why = o != null && o.Order != DivOrder.None && o.Reason != null ? " - " + o.Reason : "";
                if (why.Length > 60) why = why.Substring(0, 60) + "...";
                bool keep = cur == DivOrder.Keep;
                GUI.Label(new Rect(x + 8f, y + 4f, w - (keep ? 290f : 170f), 18f), $"<b>Division orders</b> (Alt+1..8, {panelKey} folds): {OrderNames[(int)cur]}{why}", oTitle);
                // 1.11.494: the distance of the Keep range order, for this division
                if (keep)
                {
                    TorpedoSettings.BeginSmall();
                    try
                    {
                        float kr = KeepRangeOf(div);
                        if (GUI.Button(new Rect(x + w - 282f, y + 3f, 38f, 18f), "-1 km")) KeepRangeStep(div, -1000f);
                        GUI.Label(new Rect(x + w - 242f, y + 4f, 40f, 18f), $"{kr / 1000f:0.#} km", oTitle);
                        if (GUI.Button(new Rect(x + w - 202f, y + 3f, 38f, 18f), "+1 km")) KeepRangeStep(div, 1000f);
                        if (GUI.Button(new Rect(x + w - 322f, y + 3f, 36f, 18f), "IZ")) KeepRangeImmune(div); // 1.11.496 (#117)
                    }
                    finally { TorpedoSettings.EndSmall(); }
                }
                // 1.11.361 (author 29.10): split or not for the Torpedo attack order, for this division (default: F10 DivisionTorpedoSplit)
                bool sw = SplitWanted(div);
                TorpedoSettings.BeginSmall();
                try
                {
                    if (GUI.Button(new Rect(x + w - 160f, y + 3f, 128f, 18f), sw ? "Torpedo: split in two" : "Torpedo: one group"))
                    { splitChoice[div.Pointer] = !sw; Log($"{(o != null ? o.Name : div.Pointer.ToString())}: torpedo attack {(!sw ? "splits in two" : "as one group")} (panel)"); }
                }
                finally { TorpedoSettings.EndSmall(); }
                if (GUI.Button(new Rect(x + w - 26f, y + 4f, 20f, 18f), "▴")) oFolded = true;
                int nOrd = OrderNames.Length; float bw = (w - 16f - 4f * (nOrd - 1)) / nOrd; TorpedoSettings.BeginSmall(); try {
                for (int i = 0; i < OrderNames.Length; i++)
                {
                    var r = new Rect(x + 8f + i * (bw + 4f), y + 22f, bw, 19f);
                    bool active = i == (int)cur;
                    var oldBg = GUI.backgroundColor; if (active) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f); // 1.11.202: the active order stands out
                    if (GUI.Button(r, active ? ">> " + OrderNames[i] + " <<" : OrderNames[i])) SetOrder(div, (DivOrder)i, "panel");
                    GUI.backgroundColor = oldBg;
                }
                } finally { TorpedoSettings.EndSmall(); }
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

    // 1.11.250 (film of 25.10, points B1/B2): the mod's battle panels (division orders, order tags, range tags, dispatch bubbles)
    // stayed on the screen after the battle - over the result screen and the "Battle won - Play again?" window. They are drawn only
    // while no game window is open over the battle: the battle result / ship statistics window or a message box.
    // The leave-battle path of the game is logged ([Battle] leaving the battle ...) so that a hang after "Play again? - No" shows
    // where it stops.
    internal static class BattleScreen
    {
        static float next; static bool hidden; static int errors;
        internal static bool Hidden { get { Refresh(); return hidden; } }
        static void Refresh()
        {
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 0.2f;
            bool h = false; string why = null;
            try { if (MessageBoxUI.IsAnyActive()) { h = true; why = "a message box is open"; } } catch { }
            if (!h)
            {
                try
                {
                    var ui = G.ui; var rw = ui != null ? ui.m_BattleResultWindow : null;
                    if (rw != null && rw.gameObject != null && rw.gameObject.activeInHierarchy &&
                        ((rw.resultWindow != null && rw.resultWindow.activeInHierarchy) || (rw.shipStatsWindow != null && rw.shipStatsWindow.activeInHierarchy)))
                    { h = true; why = "the battle result window is open"; }
                }
                catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Battle] result window check: " + e.Message); }
            }
            if (h != hidden) { hidden = h; MelonLogger.Msg("[DIP GG Fixes][Battle] mod battle panels " + (h ? "hidden: " + why : "shown again")); }
        }
    }
    // 1.11.250 (film 25.10, B3: "Shoot Info" empty): once per battle, when the player first points at an enemy, the state of the game's
    // Shoot Info panel is written to the log (active, its parents, the text) - the panel is the game's own; this finds what hides it.
    internal static class ShootInfoDiag
    {
        static IntPtr battle; static bool done; static float since = -1f;
        internal static void Tick()
        {
            try
            {
                if (!GameManager.IsBattle) return;
                IntPtr b = IntPtr.Zero; var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer;
                if (b != battle) { battle = b; done = false; since = -1f; }
                if (done) return;
                var ui = G.ui; if (ui == null) return;
                Ship t = null; try { t = ui.attackTarget; } catch { }
                if (t == null) { since = -1f; return; }
                if (since < 0f) { since = Time.unscaledTime; return; }
                if (Time.unscaledTime - since < 1.5f) return;
                done = true;
                var p = ui.shootInfoPanel; var ig = ui.shootInfoIngame;
                string chain = "";
                try { var tr = p != null ? p.transform : null; int n = 0; while (tr != null && n++ < 8) { chain += tr.name + (tr.gameObject.activeSelf ? "(on)" : "(OFF)") + " < "; tr = tr.parent; } } catch { }
                string txt = ""; try { txt = ui.shootInfoText != null ? ui.shootInfoText.text : "(no text)"; } catch { }
                if (txt != null && txt.Length > 160) txt = txt.Substring(0, 160);
                string vals = ""; try { vals = $"range '{ui.shootInfoRangeValue}', accuracy '{ui.shootInfoAccuracyValue}', penetration '{ui.shootInfoPenetrationValue}', reasons '{ui.shootInfoReasons}'"; } catch { }
                MelonLogger.Msg($"[DIP GG Fixes][Battle] Shoot Info check (target {t.name}): panel {(p == null ? "missing" : (p.activeInHierarchy ? "visible" : "hidden"))}, in-game label {(ig == null ? "missing" : (ig.activeInHierarchy ? "visible" : "hidden"))}; parents {chain}; text '{txt}'; {vals}");
            }
            catch (Exception e) { done = true; MelonLogger.Warning("[DIP GG Fixes][Battle] Shoot Info check: " + e.Message); }
        }
    }
    [HarmonyPatch(typeof(BattleManager), "LeaveBattle")]
    internal static class Patch_LeaveBattle_Log
    {
        static void Prefix() { MelonLogger.Msg("[DIP GG Fixes][Battle] leaving the battle (LeaveBattle) ..."); }
        static void Postfix() { MelonLogger.Msg("[DIP GG Fixes][Battle] ... LeaveBattle done"); }
        static Exception Finalizer(Exception __exception) { if (__exception != null) MelonLogger.Warning("[DIP GG Fixes][Battle] LeaveBattle threw: " + __exception); return __exception; }
    }
    // 1.11.250 (B2): leaving a custom battle back to its setup screen, TweaksAndFixes' own postfix of Ui.SkirmishSetupInit throws
    // (NullReferenceException in SkirmishSetupMod.ApplyToSK, seen in every AutoTest of 25.10). The exception left the game's setup
    // screen half built - the likely cause of the hang after "Play again? - No". It is caught here and logged once.
    [HarmonyPatch(typeof(Ui), "SkirmishSetupInit")]
    internal static class Patch_SkirmishSetupInit_Guard
    {
        static int n;
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null && n++ < 3) MelonLogger.Warning("[DIP GG Fixes][Battle] custom battle setup screen: an exception from another mod's hook was caught so the screen finishes building: " + __exception.GetType().Name + ": " + __exception.Message);
            return null;
        }
    }
    [HarmonyPatch(typeof(BattleManager), "RestartCustomBattle")]
    internal static class Patch_RestartCustom_Log
    {
        static void Prefix() { MelonLogger.Msg("[DIP GG Fixes][Battle] custom battle restart requested"); }
        static Exception Finalizer(Exception __exception) { if (__exception != null) MelonLogger.Warning("[DIP GG Fixes][Battle] RestartCustomBattle threw: " + __exception); return __exception; }
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
