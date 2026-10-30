using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    // 1.11.425 (#85, author 30.10 08:22 and 08:35: "do the aircraft in battle reconnoitre - lift the fog of war?"
    // "the torpedo aircraft too, while they are within the right range"): an enemy ship near an airborne aircraft is
    // detected by the aircraft's side - a spotting aircraft circling at about 900 m sees out to the visibility limit
    // (at most 20 km), a torpedo aircraft low over the sea about 10 km. Daylight only; a shot-down aircraft or one
    // that has lost its ship sees nothing. The game's own check Ship.DoesDetect(spotter, whom) is extended: when the
    // ship does not see the enemy itself, a ship of the same side with aircraft near the enemy does.
    internal static class AirRecon
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("NavalAviationAirRecon", true, null, "Battle: aircraft in the air reconnoitre - an enemy ship near a spotting aircraft (up to the visibility, at most 20 km) or a torpedo aircraft (about 10 km) is seen by that side, in daylight");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirRecon] " + m); }

        const float SpotterCap = 20000f, StrikeCap = 10000f;
        // side (player pointer) -> enemy ships its aircraft see now
        static readonly Dictionary<IntPtr, HashSet<IntPtr>> seen = new Dictionary<IntPtr, HashSet<IntPtr>>();
        static readonly HashSet<string> told = new HashSet<string>();
        static float next; static IntPtr battle; static int logs;

        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }
        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        // visibility in metres: the game gives km (or a share in some battles)
        static float Vis(float cap)
        {
            try
            {
                float day = BattleManager.Instance.GetDaytimeDetection();
                if (day > 1.5f) return Mathf.Min(cap, day * 1000f);
                return cap * Mathf.Clamp(day, 0.3f, 1f);
            }
            catch { return cap; }
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) { if (seen.Count > 0) seen.Clear(); return; }
            var bm = BattleManager.Instance; if (bm == null || bm.CurrentBattle == null) { if (seen.Count > 0) { seen.Clear(); told.Clear(); } return; }
            var b = bm.CurrentBattle;
            if (b.Pointer != battle) { battle = b.Pointer; seen.Clear(); told.Clear(); logs = 0; }
            if (Time.time < next) return; next = Time.time + 0.5f;
            seen.Clear();
            bool night = false; try { night = BattleAi.IsNight(); } catch { }
            if (night) return;
            var eyes = new List<KeyValuePair<Player, Vector3>>(); var kind = new List<float>(); var who = new List<string>();
            try { Spotters.Eyes((p, pos, name) => { eyes.Add(new KeyValuePair<Player, Vector3>(p, pos)); kind.Add(Vis(SpotterCap)); who.Add("spotting aircraft of " + name); }); } catch { }
            try { AirStrike.Eyes((p, pos) => { eyes.Add(new KeyValuePair<Player, Vector3>(p, pos)); kind.Add(Vis(StrikeCap)); who.Add("torpedo aircraft"); }); } catch { }
            if (eyes.Count == 0) return;
            try
            {
                for (int i = 0; i < b.Ships.Count; i++)
                {
                    var s = b.Ships[i]; if (!Alive(s)) continue;
                    var sp = s.transform.position;
                    for (int k = 0; k < eyes.Count; k++)
                    {
                        var side = eyes[k].Key; if (side == null || s.player == null || s.player.Pointer == side.Pointer) continue;
                        float d = Flat(sp - eyes[k].Value); if (d > kind[k]) continue;
                        if (!seen.TryGetValue(side.Pointer, out var set)) { set = new HashSet<IntPtr>(); seen[side.Pointer] = set; }
                        if (set.Add(s.Pointer))
                        {
                            string key = side.Pointer.ToInt64() + ":" + s.Pointer.ToInt64();
                            if (told.Add(key) && logs++ < 30) Log($"{s.name} seen by {who[k]} at {d:0} m ({side.data?.name})");
                        }
                    }
                }
            }
            catch { }
        }

        internal static bool Sees(Ship spotter, Ship whom)
        {
            if (seen.Count == 0 || spotter == null || whom == null) return false;
            try { var p = spotter.player; return p != null && seen.TryGetValue(p.Pointer, out var set) && set.Contains(whom.Pointer); } catch { return false; }
        }
    }

    [HarmonyPatch(typeof(Ship), "DoesDetect")]
    internal static class AirRecon_DoesDetect
    {
        static void Postfix(Ship spotter, Ship whom, ref bool __result)
        {
            if (__result) return;
            try { if (AirRecon.Sees(spotter, whom)) __result = true; } catch { }
        }
    }
}
