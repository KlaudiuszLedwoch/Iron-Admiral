// 1.11.343 (#71). Test 28.10 11:05: AI flotillas charging two battleships abeam from 9 km lost 5 of 6 ships in about 4 minutes, all
// under the broadside of the whole line. Author 28.10, 12:34 and 12:39: "attack from two directions at once to split the fire"; "attack
// the first or the last ship depending on the side we come from"; "attack from ahead, from the bow - a narrow target, the fastest closing,
// the shortest time under fire; a battleship attacked abeam is suicide for TB, DD and CL - they should use their speed advantage and
// attack from the bow while checking from the stern; the more ships in the battle, the less chance the battleship has to get out".
// Rules for AI light divisions charging a capital ship:
//  - role BOW: a division faster than the enemy column (speed ratio 1.15) works ahead of it and attacks the LEADING ship from fine on
//    its bow (20 deg off the bow, on the side the division is on) - closing at the sum of both speeds, against a narrow target, with
//    only the forward guns of the line bearing;
//  - role STERN: a division that is not fast enough, or the second half of a split division, takes the LAST ship from its quarter
//    (150 deg off the bow) - it checks a turn-away of the line and draws the after guns;
//  - a division of 4 or more ships splits in two when the charge starts: the half with the leader takes the bow role (when fast
//    enough), the detached half the stern role.
// The column = enemy capital ships within 4 km of the nearest one, on a course within 35 deg of it.
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
            On = cat.CreateEntry("AiTorpedoPincer", true, null, "Battle AI: AI torpedo flotillas attack from ahead - a faster flotilla takes the leading ship of the enemy line from fine on its bow (narrow target, fastest closing), the rest or a slower one takes the last ship from its quarter; a flotilla of 4 or more ships splits in two to do both at once");
        }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }
        static void Log(string m) { BattleAi.Log(m); }

        enum Role { None, Bow, Stern }
        static readonly Dictionary<IntPtr, Role> roles = new Dictionary<IntPtr, Role>();
        static readonly HashSet<IntPtr> split = new HashSet<IntPtr>();
        static IntPtr battle;
        static void Reset()
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            IntPtr bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != battle) { battle = bp; split.Clear(); roles.Clear(); }
        }

        // the column 'nearest' belongs to, ordered from the leading ship to the last one
        static List<Ship> Column(Ship nearest, Ship from, Il2CppSystem.Collections.Generic.List<Ship> enemies, out UnityEngine.Vector3 f)
        {
            var list = new List<Ship> { nearest }; f = UnityEngine.Vector3.zero;
            var nt = nearest.transform; if (nt == null) return list;
            f = nt.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) return list; f.Normalize();
            var p0 = nt.position;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i]; if (!Alive(e) || e.Pointer == nearest.Pointer || !BattleAi.IsCapital(e)) continue;
                bool vis = false; try { vis = e.IsVisibleFor(from); } catch { }
                if (!vis) continue;
                var et = e.transform; if (et == null) continue;
                var d = et.position - p0; d.y = 0f; if (d.magnitude > 4000f) continue;
                var ef = et.forward; ef.y = 0f; if (ef.sqrMagnitude < 0.01f || UnityEngine.Vector3.Angle(ef, f) > 35f) continue;
                list.Add(e);
            }
            var ff = f;
            list.Sort((a, b) => UnityEngine.Vector3.Dot(b.transform.position - p0, ff).CompareTo(UnityEngine.Vector3.Dot(a.transform.position - p0, ff)));
            return list;
        }

        static float ColumnSpeed(List<Ship> col) { float v = 0f; foreach (var s in col) v = Math.Max(v, BattleAi.Speed(s)); return v; }

        // the target of an AI light division: the leading ship (bow role) or the last ship (stern role) of the column of 'nearest'
        internal static Ship Target(Division div, Ship leader, Ship nearest, Il2CppSystem.Collections.Generic.List<Ship> enemies, out float dist)
        {
            dist = BattleAi.Dist(leader, nearest);
            if (On == null || !On.Value || div == null || nearest == null || enemies == null) return nearest;
            try
            {
                Reset();
                var col = Column(nearest, leader, enemies, out var f);
                if (!roles.TryGetValue(div.Pointer, out var role) || role == Role.None)
                {
                    // 1.11.354 (campaign 28.10: Japanese destroyers 13.1 vs 12.1 speed units - 8 % faster - all got the stern role and a
                    // long chase under fire): the bow role needs only a 5 % speed edge, and a division already ahead of the leading
                    // ship's beam takes it when it is not more than 10 % slower - the target comes to it
                    float vs = BattleAi.DivSpeed(div, leader), vt = ColumnSpeed(col);
                    bool ahead = false;
                    try { var lt = col[0].transform; var d = leader.transform.position - lt.position; d.y = 0f; ahead = f.sqrMagnitude > 0.01f && UnityEngine.Vector3.Dot(d, f) > 0f; } catch { }
                    role = (vs >= vt * 1.05f || (ahead && vs >= vt * 0.9f)) ? Role.Bow : Role.Stern;
                    roles[div.Pointer] = role;
                    Log($"{leader.name}: torpedo attack {(role == Role.Bow ? "from ahead on the leading ship" : "from the quarter on the last ship")} of a column of {col.Count} (speed {vs:0.0} vs {vt:0.0}{(ahead ? ", already ahead of her beam" : "")})");
                }
                var t = role == Role.Bow ? col[0] : col[col.Count - 1];
                dist = BattleAi.Dist(leader, t);
                return t;
            }
            catch { return nearest; }
        }

        // approach bearing for the division: fine on the target's bow (bow role) or on its quarter (stern role), on the division's side
        internal static bool Approach(Division div, Ship leader, Ship target, out UnityEngine.Vector3 dir)
        {
            dir = UnityEngine.Vector3.zero;
            if (On == null || !On.Value || div == null || target == null || !roles.TryGetValue(div.Pointer, out var role) || role == Role.None) return false;
            try
            {
                var tt = target.transform; var lt = leader.transform; if (tt == null || lt == null) return false;
                var f = tt.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) return false; f.Normalize();
                float side = tt.InverseTransformPoint(lt.position).x >= 0f ? 1f : -1f;   // + : the division is on the target's starboard side
                float ang = role == Role.Bow ? 20f : 150f;
                dir = UnityEngine.Quaternion.Euler(0f, side * ang, 0f) * f;
                return true;
            }
            catch { return false; }
        }

        // called once when an AI light division starts its charge; splits a division of 4 or more ships
        internal static bool Split(Division div, Ship leader, string name, Ship target)
        {
            if (On == null || !On.Value || div == null || leader == null) return false;
            try
            {
                Reset();
                if (!split.Add(div.Pointer)) return false;
                var ships = div.ships; if (ships == null) return false;
                var alive = new List<Ship>();
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (Alive(s)) alive.Add(s); }
                if (alive.Count < 4) return false;
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
                if (nd == null || nd.Pointer == div.Pointer) { Log($"{name}: split for a two-way attack had no effect"); return false; }
                split.Add(nd.Pointer);
                roles[nd.Pointer] = Role.Stern;
                Log($"{name}: two-way torpedo attack - {names.Count} ship(s) ({string.Join(", ", names)}) detached to take the last ship of the line from the quarter");
                return true;
            }
            catch (Exception e) { Log($"{name}: two-way split failed: {e.Message}"); return false; }
        }
    }
}
