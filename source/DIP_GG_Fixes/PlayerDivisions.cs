// 1.11.442 (author 30.10 16:10: "add torpedo avoidance on by default and changing the squadron leader off by default" and "I would
// also like a slider deciding how spread out the formation is"):
// - every division of the player gets the game's torpedo avoidance on and the automatic change of the division leader off, once,
//   when it first appears in a battle (also a division split off later); the player's own clicks afterwards are kept
//   (switches PlayerAvoidTorpedoes, PlayerKeepDivisionLeader);
// - "Spacing" row under the gun orders: a slider for the selected division's distance between ships, x0.5 (closed up) to x3
//   (wide open) of the game's following distance; Shift = all the player's divisions. Default for new divisions:
//   FormationSpacing (1.0 = the game's own).
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class PlayerDivisions
    {
        internal static MelonPreferences_Entry<bool> Avoid, KeepLeader; internal static MelonPreferences_Entry<float> Spacing;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Avoid = cat.CreateEntry("PlayerAvoidTorpedoes", true, null, "Battle: the player's divisions start with torpedo avoidance on (the game starts them with it off); a click on the button still turns it off");
            KeepLeader = cat.CreateEntry("PlayerKeepDivisionLeader", true, null, "Battle: the player's divisions start with the automatic change of the division leader off - the leader stays the ship the player put at the head");
            Spacing = cat.CreateEntry("FormationSpacing", 1f, null, "Battle: the default distance between the ships of the player's divisions, as a multiple of the game's (0.5 closed up - 3 wide open); the 'Spacing' slider under the gun orders changes it for a division during the battle");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Divisions] " + m); }
        static readonly HashSet<IntPtr> done = new HashSet<IntPtr>(); static readonly Dictionary<IntPtr, int> tries = new Dictionary<IntPtr, int>();
        static readonly Dictionary<IntPtr, float> mult = new Dictionary<IntPtr, float>();
        static float next; static bool inBattle; static int logged, errors;

        static bool Mine(Ship s) { try { return s != null && s.player != null && s.player.isMain; } catch { return false; } }
        internal static float Mult(Division d)
        {
            if (d == null) return 1f;
            if (mult.TryGetValue(d.Pointer, out float m)) return m;
            return Spacing != null ? Mathf.Clamp(Spacing.Value, 0.5f, 3f) : 1f;
        }

        internal static void Tick()
        {
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (!b) { if (inBattle) { inBattle = false; done.Clear(); tries.Clear(); mult.Clear(); logged = 0; } return; }
            inBattle = true;
            try { SingleEvasion(); } catch (Exception e) { if (errors++ < 5) Log("single-ship evasion: " + e.Message); }
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 1f;
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null || battle.Ships == null) return;
                var ships = battle.Ships;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (!Mine(s)) continue;
                    Division d = null; try { d = s.division; } catch { }
                    if (d == null || done.Contains(d.Pointer)) continue;
                    done.Add(d.Pointer);
                    string what = "";
                    if (Avoid != null && Avoid.Value)
                    {
                        bool can = true; try { can = d.CanAvoidTorpedoes(); } catch { }
                        if (!can) { try { int n0; tries.TryGetValue(d.Pointer, out n0); if (!d.avoidTorpedoes && n0 < 60) { tries[d.Pointer] = n0 + 1; done.Remove(d.Pointer); continue; } } catch { } }   // 1.11.454: not possible yet (start of the battle) - try again next second
                        try { if (can && !d.avoidTorpedoes) { d.AvoidTorpedoes(true); what += "torpedo avoidance on"; } } catch (Exception e) { what += "avoid: " + e.Message; }
                    }
                    if (KeepLeader != null && KeepLeader.Value)
                    {
                        try { if (d.autoChangeGroupLeader) { d.autoChangeGroupLeader = false; what += (what.Length > 0 ? ", " : "") + "leader change off"; } } catch (Exception e) { what += " leader: " + e.Message; }
                    }
                    bool av = false, ch = false; try { av = d.avoidTorpedoes; ch = d.autoChangeGroupLeader; } catch { }
                    if (logged++ < 12) Log($"division of {s.name}: {(what.Length > 0 ? what : "nothing to change")} - now torpedo avoidance {(av ? "on" : "off")}, leader change {(ch ? "on" : "off")}");
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        // 1.11.458: the mod's own torpedo evasion for the player's divisions the game will not let avoid (one ship)
        class Ev { public BattleAi.DivState St; public bool On; public Vector3? To, Dir; }
        static readonly Dictionary<IntPtr, Ev> ev = new Dictionary<IntPtr, Ev>();
        static float nextEv; static int evLogged;
        static void SingleEvasion()
        {
            if (Avoid == null || !Avoid.Value || Time.unscaledTime < nextEv) return; nextEv = Time.unscaledTime + 0.5f;
            var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null || battle.Ships == null) return;
            Player me = null; var seen = new HashSet<IntPtr>();
            for (int i = 0; i < battle.Ships.Count; i++)
            {
                var s = battle.Ships[i]; if (!Mine(s)) continue; if (me == null) me = s.player;
                Division d = null; try { d = s.division; } catch { }
                if (d == null || !seen.Add(d.Pointer)) continue;
                bool can = true; try { can = d.CanAvoidTorpedoes(); } catch { }
                if (can) { ev.Remove(d.Pointer); continue; }                      // the game's own avoidance works here
                Ship leader = null; try { leader = d.leader; } catch { } if (leader == null) leader = s;
                bool dead = false; try { dead = leader.isSinking || leader.isDead; } catch { } if (dead) continue;
                if (!ev.TryGetValue(d.Pointer, out var e)) { e = new Ev { St = new BattleAi.DivState { name = leader.name } }; ev[d.Pointer] = e; }
                if (e.On)
                {
                    if (Time.time < e.St.evadeUntil) continue;
                    e.On = false;                                                   // turn-away over: back to the course or point of before
                    try { if (e.To.HasValue) d.MoveTo(e.To.Value); else if (e.Dir.HasValue) d.MoveDir(e.Dir.Value, false); } catch { }
                    if (evLogged++ < 20) Log($"{leader.name}: torpedo turn-away over - back on her {(e.To.HasValue ? "way to her point" : e.Dir.HasValue ? "course" : "own")}");
                    continue;
                }
                var enemies = battle.ShipsOf(battle.OppositePlayer(me), true); if (enemies == null) continue;
                Vector3? to = null, dir = null;
                try { var mp = d.MovingPosition(); if (mp.HasValue) to = mp.Value; } catch { }
                try { if (!to.HasValue && d.IsMoving()) dir = d.MovingDirection(); } catch { }
                bool turned = false; try { turned = BattleAi.TorpedoEvasion.Check(leader, d, e.St, me, battle, enemies); } catch { }
                if (turned) { e.On = true; e.To = to; e.Dir = dir; if (evLogged++ < 20) Log($"{leader.name} (division of one): the game's torpedo avoidance does not work for her - the mod's evasion turns her away"); }
            }
        }

        // the slider row under the gun orders
        static GUIStyle sT;
        internal static void SpacingRow(Ship s, float x, float y, float w, float h, GUIStyle sm)
        {
            Division d = null; try { d = s.division; } catch { }
            if (d == null) return;
            if (sT == null) { sT = new GUIStyle(); sT.fontSize = 12; sT.richText = true; sT.normal.textColor = new Color(0.9f, 0.9f, 0.9f); }
            GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), "<b>Spacing</b>", sT);
            float cur = Mult(d);
            float v = GUI.HorizontalSlider(new Rect(x + 98f, y + 7f, 250f, 14f), cur, 0.5f, 3f);
            v = Mathf.Round(v * 10f) / 10f;
            if (Math.Abs(v - cur) > 0.001f)
            {
                bool all = Event.current != null && Event.current.shift;
                if (all) { try { var ships = BattleManager.Instance.CurrentBattle.Ships; for (int i = 0; i < ships.Count; i++) { var o = ships[i]; if (!Mine(o)) continue; var od = o.division; if (od != null) mult[od.Pointer] = v; } } catch { } }
                else mult[d.Pointer] = v;
                Log($"spacing slider x{v:0.0} for {(all ? "all divisions" : "the division of " + s.name)}");
            }
            string word = v < 0.8f ? "closed up" : v <= 1.2f ? "normal" : v <= 2f ? "open" : "wide open";
            GUI.Label(new Rect(x + 356f, y + 4f, w - 360f, 18f), $"x{v:0.0} {word} <color=#C0C0C0>division; Shift = all divisions</color>", sT);
        }

        // 1.11.457 (author 30.10 19:21: "the distance slider does not work"): the static FollowingDistance was not where the game took
        // the distance; the division's GetDesiredFollowingDistance(ship, following ship) is multiplied now, and both are logged
        static int applied, unchanged;
        [HarmonyPatch(typeof(Division), "GetDesiredFollowingDistance", new Type[] { typeof(Ship), typeof(Ship) })]
        static class DesiredPatch
        {
            static void Postfix(Division __instance, Ship __0, ref float __result)
            {
                try
                {
                    if (!Mine(__0)) return;
                    float m = Mult(__instance ?? __0.division);
                    if (Math.Abs(m - 1f) > 0.001f) { float was = __result; __result *= m; if (applied++ < 6) Log($"spacing x{m:0.0}: {__0.name} keeps {__result:0} m behind the ship ahead (game {was:0} m)"); }
                    else if (unchanged++ < 2) Log($"spacing x1.0: {__0.name} follows at the game's {__result:0} m");
                }
                catch { }
            }
        }
    }
}
