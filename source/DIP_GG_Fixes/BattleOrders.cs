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
        internal enum DivOrder { None, Screen, Scout, Torpedo, Free }
        internal class OrderSt { public DivOrder Order; public bool FromVanilla; public bool Issued; public float NextThink; public string Name; public string Reason; public Vector3 LastSeen; public bool Seen; }
        static readonly Dictionary<IntPtr, OrderSt> orders = new Dictionary<IntPtr, OrderSt>();
        static IntPtr ordersBattle;
        internal static bool Issuing;   // our own commands - the Division.MoveTo/MoveDir patch ignores them
        static readonly string[] OrderNames = { "Manual", "Screen", "Scout", "Torpedo attack", "Free action" };

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
        static void Cmd(Division div, OrderSt o, Vector3 to) { Issuing = true; try { div.MoveTo(to); o.Issued = true; } catch { } finally { Issuing = false; } }

        internal static void SetOrder(Division div, DivOrder ord, string how)
        {
            if (div == null) return;
            var o = OrderOf(div, true); o.Order = ord; o.FromVanilla = false; o.Issued = false; o.Reason = null; o.NextThink = 0f;
            try { o.Name = div.leader != null ? div.leader.name : "division"; } catch { o.Name = "division"; }
            Log($"{o.Name}: order {OrderNames[(int)ord]} ({how})");
            if (ord != DivOrder.None) try { Issuing = true; if (div.isRetreating || div.retreat) div.Retreat(false); } catch { } finally { Issuing = false; }
        }
        internal static DivOrder CurrentOrder(Division div) { var o = OrderOf(div, false); return o != null ? o.Order : DivOrder.None; }

        // the player gave the division a move order himself - command goes back to him
        // 1.11.159: only a command right after a click or key of the player counts (2nd auto test: the game itself gave a
        // retreating division a course order 1 s after "Free action" and the order ended at once)
        internal static float lastInput = -100f;
        internal static void PlayerCommand(Division div, string what)
        {
            if (Issuing || div == null || !GameManager.IsBattle) return;
            if (Time.realtimeSinceStartup - lastInput > 0.6f) return;
            if (!orders.TryGetValue(div.Pointer, out var o) || o.Order == DivOrder.None) return;
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

            if (o.Order == DivOrder.Free) { Issuing = true; try { Think(leader, true, true); } finally { Issuing = false; } return true; }
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
            if (main != null && (me - home).magnitude > leash) { Cmd(div, o, home + toEnemy * (leash * 0.6f)); OWhy(o, "too far from the main body - closing up"); return true; }
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
                if (Input.GetKeyDown(KeyCode.Alpha1)) k = 0; else if (Input.GetKeyDown(KeyCode.Alpha2)) k = 1; else if (Input.GetKeyDown(KeyCode.Alpha3)) k = 2; else if (Input.GetKeyDown(KeyCode.Alpha4)) k = 3; else if (Input.GetKeyDown(KeyCode.Alpha5)) k = 4;
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
                float w = 470f, h = 58f; float x = (UiScale.W - w) / 2f, y = 54f;
                if (oFolded)
                {
                    var tab = new Rect(x + w - 150f, y, 150f, 20f);
                    if (GUI.Button(tab, $"Orders: {OrderNames[(int)cur]} ▾")) oFolded = false;
                    return;
                }
                GUI.Box(new Rect(x, y, w, h), "");
                string why = o != null && o.Order != DivOrder.None && o.Reason != null ? " - " + o.Reason : "";
                if (why.Length > 60) why = why.Substring(0, 60) + "...";
                GUI.Label(new Rect(x + 8f, y + 4f, w - 40f, 18f), $"<b>Division orders</b> (Alt+1..5): {OrderNames[(int)cur]}{why}", oTitle);
                if (GUI.Button(new Rect(x + w - 26f, y + 4f, 20f, 18f), "▴")) oFolded = true;
                float bw = (w - 16f - 4f * 4f) / 5f;
                for (int i = 0; i < 5; i++)
                {
                    var r = new Rect(x + 8f + i * (bw + 4f), y + 26f, bw, 26f);
                    if (GUI.Button(r, i == (int)cur ? "[" + OrderNames[i] + "]" : OrderNames[i])) SetOrder(div, (DivOrder)i, "panel");
                }
            }
            catch (Exception e) { if (oErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] orders panel: " + e.Message); }
        }
    }

    // a move order the player gives a division under an autonomous order takes command back
    [HarmonyPatch(typeof(Division), "MoveTo")]
    internal static class Patch_DivisionMoveTo_Orders
    {
        static void Prefix(Division __instance) { try { BattleAi.PlayerCommand(__instance, "move order"); } catch { } }
    }
    [HarmonyPatch(typeof(Division), "MoveDir")]
    internal static class Patch_DivisionMoveDir_Orders
    {
        static void Prefix(Division __instance) { try { BattleAi.PlayerCommand(__instance, "course order"); } catch { } }
    }
}
