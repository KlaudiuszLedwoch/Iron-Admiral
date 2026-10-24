// 1.11.209 (author, 24.10): gun barrels raised to the elevation of the shot. The game points the barrels along its own almost flat path
// (a few degrees at 25 km); with the physical ballistics a long-range shot leaves at 20-30 degrees. Every shell the mod rebuilds records
// the elevation of its gun; after the game has updated the turret (LateUpdate), each barrel of that gun is pitched to that elevation,
// limited to MaxBarrelElevation, and eased back to the game's own angle when the gun has not fired for a while. Visual only - the
// hit itself is decided by the game when the shell is fired. Switch: BarrelElevation (F10, Battle).
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class BarrelElevation
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> MaxDeg;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BarrelElevation", true, null, "Battle: gun barrels are raised to the elevation of the shot from the mod's ballistics (visual only)");
            MaxDeg = cat.CreateEntry("MaxBarrelElevation", 40f, null, "Battle: highest barrel elevation shown (degrees) - most turrets of the period were limited to 13-40 degrees");
        }
        class Rec { public Part P; public float Elev, T, Cur; }
        static readonly Dictionary<IntPtr, Rec> recs = new Dictionary<IntPtr, Rec>();
        static int logged, errors; static IntPtr battlePtr;

        internal static void Record(Part from, float elevDeg)
        {
            if (On == null || !On.Value || from == null || float.IsNaN(elevDeg)) return;
            try
            {
                if (!recs.TryGetValue(from.Pointer, out var r)) { r = new Rec { P = from, Cur = 0f }; recs[from.Pointer] = r; if (recs.Count > 600) recs.Clear(); }
                r.Elev = Mathf.Clamp(elevDeg, 0f, MaxDeg != null ? MaxDeg.Value : 40f); r.T = Time.time;
            }
            catch { }
        }

        internal static void LateTick()
        {
            if (On == null || !On.Value || recs.Count == 0) return;
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (!battle) { recs.Clear(); return; }
            try
            {
                IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
                if (bp != battlePtr) { battlePtr = bp; recs.Clear(); return; }
                float dt = Time.deltaTime; var dead = new List<IntPtr>();
                foreach (var kv in recs)
                {
                    var r = kv.Value; var p = r.P;
                    bool gone = true; try { gone = p == null || p.ship == null || p.ship.isDead || p.ship.isSinking; } catch { }
                    if (gone) { dead.Add(kv.Key); continue; }
                    // target elevation: the last shot's, eased back to the game's angle 20 s (game time) after the last shot
                    float want = Time.time - r.T < 20f ? r.Elev : 0f;
                    r.Cur = Mathf.MoveTowards(r.Cur, want, 6f * dt * Mathf.Max(1f, Ballistics.WS)); // barrels elevate at a few degrees per second
                    if (r.Cur < 0.2f) continue;
                    var barrels = p.gunBarrels; if (barrels == null) continue;
                    for (int i = 0; i < barrels.Count; i++)
                    {
                        var g = barrels[i]; if (g == null) continue;
                        var tr = g.transform; var e = tr.localEulerAngles;
                        if (logged < 6) { logged++; MelonLogger.Msg($"[DIP GG Fixes][Barrels] {p.data?.name} on {p.ship?.name}: barrel local angles before {e.x:0.0}/{e.y:0.0}/{e.z:0.0}, shot elevation {r.Elev:0.0} deg"); }
                        // the game's own pitch stays (its few degrees), ours is added on the barrel's local X axis (negative = up in Unity)
                        tr.localEulerAngles = new Vector3(e.x - r.Cur, e.y, e.z);
                    }
                }
                foreach (var k in dead) recs.Remove(k);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Barrels] " + e.Message); }
        }
    }
}
