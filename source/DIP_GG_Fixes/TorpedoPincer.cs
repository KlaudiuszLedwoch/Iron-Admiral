// 1.11.343 (#71, author 28.10, 12:34: "the torpedo attack should come from two directions at once, to split the fire - in practice the
// easiest target is the last ship of the line"). Test 28.10 11:05: AI flotillas charging two battleships abeam from 9 km lost 5 of 6
// ships in about 4 minutes, all under the fire of the whole line. Two rules for AI light divisions:
//  - the charge goes at the last ship of the enemy column (the capital ships within 4 km of the nearest one, on a course within 35 deg),
//    where fewer guns of the line bear on the attackers;
//  - a division of 4 or more ships splits in two when the charge starts; both halves attack the same ship and take approach bearings
//    either side of its bow (BattleAi.CrossSlot), so the target cannot turn away from both and the defending fire is divided.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoPincer
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiTorpedoPincer", true, null, "Battle AI: AI torpedo flotillas attack the last ship of the enemy line, and a flotilla of 4 or more ships splits in two at the start of the charge to attack from both bows at once (the defending fire is divided)");
        }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }

        // the last ship of the column that 'nearest' belongs to (nearest itself when it sails alone)
        internal static Ship RearOfColumn(Ship nearest, Ship from, Il2CppSystem.Collections.Generic.List<Ship> enemies, out float dist)
        {
            dist = BattleAi.Dist(from, nearest);
            if (On == null || !On.Value || nearest == null || enemies == null) return nearest;
            try
            {
                var nt = nearest.transform; if (nt == null) return nearest;
                var f = nt.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) return nearest; f.Normalize();
                var p0 = nt.position; Ship rear = nearest; float back = 0f;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (!Alive(e) || e.Pointer == nearest.Pointer || !BattleAi.IsCapital(e)) continue;
                    bool vis = false; try { vis = e.IsVisibleFor(from); } catch { }
                    if (!vis) continue;
                    var et = e.transform; if (et == null) continue;
                    var d = et.position - p0; d.y = 0f; if (d.magnitude > 4000f) continue;
                    var ef = et.forward; ef.y = 0f; if (ef.sqrMagnitude < 0.01f || UnityEngine.Vector3.Angle(ef, f) > 35f) continue;
                    float along = UnityEngine.Vector3.Dot(d, f);
                    if (along < back) { back = along; rear = e; }
                }
                dist = BattleAi.Dist(from, rear);
                return rear;
            }
            catch { return nearest; }
        }

        static readonly HashSet<IntPtr> split = new HashSet<IntPtr>();
        static IntPtr battle;
        // called once when an AI light division starts its charge; true when the division was split
        internal static bool Split(Division div, Ship leader, string name, Ship target)
        {
            if (On == null || !On.Value || div == null || leader == null) return false;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                IntPtr bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != battle) { battle = bp; split.Clear(); }
                if (!split.Add(div.Pointer)) return false;
                var ships = div.ships; if (ships == null) return false;
                var alive = new List<Ship>();
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (Alive(s)) alive.Add(s); }
                if (alive.Count < 4) return false;
                // the leader stays; the ships furthest from it (the rear of the formation) form the second group
                var lp = leader.transform.position;
                alive.Sort((x, y) => (x.transform.position - lp).sqrMagnitude.CompareTo((y.transform.position - lp).sqrMagnitude));
                var il = new Il2CppSystem.Collections.Generic.List<Ship>(); var names = new List<string>();
                for (int i = alive.Count / 2 + (alive.Count % 2); i < alive.Count; i++)
                {
                    var s = alive[i]; if (s.Pointer == leader.Pointer) continue;
                    bool can = false; try { can = s.CanDetach(); } catch { }
                    if (!can) return false;
                    il.Add(s); names.Add(s.name);
                }
                if (il.Count < 2) return false;
                Ship.DetachShips(il);
                var nd = il[0].division;
                if (nd == null || nd.Pointer == div.Pointer) { BattleAi.Log($"{name}: split for a pincer attack had no effect"); return false; }
                split.Add(nd.Pointer);
                BattleAi.Log($"{name}: pincer torpedo attack on {(target != null ? target.name : "?")} - {names.Count} ship(s) ({string.Join(", ", names)}) detached to attack from the other bow");
                return true;
            }
            catch (Exception e) { BattleAi.Log($"{name}: pincer split failed: {e.Message}"); return false; }
        }
    }
}
