// 1.11.209 (author, 24.10): gun barrels raised to the elevation of the shot. The game points the barrels along its own almost flat path
// (a few degrees at 25 km); with the physical ballistics a long-range shot leaves at 20-30 degrees. Every shell the mod rebuilds records
// the elevation of its gun; after the game has updated the turret (LateUpdate), each barrel of that gun is pitched to that elevation,
// limited to the mount elevation of the gun's generation (MountElevation), and eased back to the game's own angle when the gun has not
// fired for a while. Visual only - the hit itself is decided by the game when the shell is fired. Switch: BarrelElevation (F10, Battle).
// 1.11.216: the offset was added to the barrel's current angle every frame and accumulated (a turret stood at 90 deg in the 24.10
//           battle). Now the game's own angle is remembered per barrel and the pitch is written as an absolute value, never above the mount.
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
            MaxDeg = cat.CreateEntry("MaxBarrelElevation", 45f, null, "Battle: highest barrel elevation shown (degrees) - the mount elevation of the gun's generation applies below this");
        }
        class Rec { public Part P; public float Elev, T, Cur, Max; }
        static readonly Dictionary<IntPtr, Rec> recs = new Dictionary<IntPtr, Rec>();
        struct BW { public float Base, Written; }
        static readonly Dictionary<IntPtr, BW> bases = new Dictionary<IntPtr, BW>();
        static int logged, errors; static IntPtr battlePtr;

        internal static void Record(Part from, float elevDeg)
        {
            if (On == null || !On.Value || from == null || float.IsNaN(elevDeg)) return;
            try
            {
                if (!recs.TryGetValue(from.Pointer, out var r))
                {
                    r = new Rec { P = from, Cur = 0f, Max = MaxDeg != null ? MaxDeg.Value : 45f };
                    try { var sh = from.ship; if (sh != null && from.data != null) r.Max = Mathf.Min(r.Max, MountElevation.Elev(sh.TechGunGrade(from.data, false))); } catch { }
                    recs[from.Pointer] = r; if (recs.Count > 600) recs.Clear();
                }
                r.Elev = Mathf.Clamp(elevDeg, 0f, r.Max); r.T = Time.time;
            }
            catch { }
        }

        internal static void LateTick()
        {
            if (On == null || !On.Value || recs.Count == 0) return;
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (!battle) { recs.Clear(); bases.Clear(); return; }
            try
            {
                IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
                if (bp != battlePtr) { battlePtr = bp; recs.Clear(); bases.Clear(); return; }
                if (bases.Count > 3000) bases.Clear();
                float dt = Time.deltaTime; var dead = new List<IntPtr>();
                foreach (var kv in recs)
                {
                    var r = kv.Value; var p = r.P;
                    bool gone = true; try { gone = p == null || p.ship == null || p.ship.isDead || p.ship.isSinking; } catch { }
                    if (gone) { dead.Add(kv.Key); continue; }
                    // target elevation: the last shot's, eased back to the game's angle 20 s (game time) after the last shot
                    float want = Time.time - r.T < 20f ? r.Elev : 0f;
                    r.Cur = Mathf.MoveTowards(r.Cur, want, 6f * dt * Mathf.Max(1f, Ballistics.WS)); // barrels elevate at a few degrees per second
                    var barrels = p.gunBarrels; if (barrels == null) continue;
                    for (int i = 0; i < barrels.Count; i++)
                    {
                        var g = barrels[i]; if (g == null) continue;
                        var tr = g.transform; var e = tr.localEulerAngles;
                        if (logged < 6) { logged++; MelonLogger.Msg($"[DIP GG Fixes][Barrels] {p.data?.name} on {p.ship?.name}: barrel local angles before {e.x:0.0}/{e.y:0.0}/{e.z:0.0}, shot elevation {r.Elev:0.0} deg, mount limit {r.Max:0} deg"); }
                        // the game's own pitch (negative X = up in Unity) is remembered per barrel; when the barrel still holds what we wrote
                        // last frame, the game has not moved it and the remembered angle is its own. The pitch is written as an absolute value.
                        var key = g.Pointer; float gameX = Mathf.DeltaAngle(0f, e.x);
                        if (bases.TryGetValue(key, out var bw) && Mathf.Abs(Mathf.DeltaAngle(e.x, bw.Written)) < 0.05f) gameX = bw.Base;
                        if (gameX < -r.Max) gameX = -r.Max; if (gameX > 20f) gameX = 20f;
                        if (r.Cur < 0.2f) { bases[key] = new BW { Base = gameX, Written = e.x }; continue; }
                        float nx = gameX - r.Cur; if (nx < -r.Max) nx = -r.Max;
                        tr.localEulerAngles = new Vector3(nx, e.y, e.z);
                        bases[key] = new BW { Base = gameX, Written = tr.localEulerAngles.x };
                    }
                }
                foreach (var k in dead) recs.Remove(k);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Barrels] " + e.Message); }
        }
    }
}
