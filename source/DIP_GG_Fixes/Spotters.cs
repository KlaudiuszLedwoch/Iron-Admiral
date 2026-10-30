// 1.11.417 (#85 step 2, author 30.10 06:20: "add the spotting aircraft - let them fly, let the catapult throw them, check how it
// looked"): the spotting aircraft of NavalAviation.SpotF are drawn. How it was done (USNI Naval History, January 1990,
// "Battleship floatplanes"; Naval Gazing, "Battleship Aviation", part 3):
//  - every take-off was a catapult shot; the aircraft went off before the main battery opened fire (the blast would wreck them);
//  - the catapult was trained out at least 30 degrees from the centre line, into the wind where possible, and fired as the roll
//    brought it level; a powder charge (US) drove the trolley - up to 2.75 g on the British cross-deck catapult; the pilot held
//    the stick back so the nose came up as soon as the aircraft left the trolley;
//  - after the mission the ship turned across the wind so its wake left a smooth slick, the aircraft alighted in it, taxied onto
//    the towed mat or sled, and the crane hoisted it aboard.
// Here: in daylight each battleship, battlecruiser and cruiser with spotting (NavalAviation spot level > 0) fires one aircraft off
// its catapult, staggered, before its main battery opens - the trolley run off the ship's side about 35 degrees off the bow,
// nose up, a climbing turn to about 900 m, then a wide circuit on the own side of the target, out of the reach of its
// anti-aircraft fire. The spotting bonus counts only while the aircraft is up. At dusk, or when its ship sinks, it comes back,
// alights in the ship's wake and is hoisted in (or flies off). Aircraft: Short 184 before 1930 (seaplane), later the Swordfish
// model - stand-ins until the Walrus model has its moving parts set up. Switch: NavalAviationSpotterPlanes (off = the bonus
// as before, without the aircraft).
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Spotters
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("NavalAviationSpotterPlanes", true, null, "Battle: the spotting aircraft are flown - fired off the catapult before the main battery opens, circling on the own side of the target, back to the ship's wake at dusk; the spotting bonus counts only while the aircraft is up (off = the bonus without aircraft)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Spotters] " + m); }
        enum Ph { Wait, Catapult, Climb, Spot, Home, Alight, Gone }
        class Sp
        {
            public Ship S; public GameObject Go; public AirModels.Ctl Ctl; public Ph P; public Vector3 Pos, Dir; public float T, LaunchAt, Side, Seed, Roll, Ang; public int Shots;
        }
        static readonly List<Sp> sps = new List<Sp>();
        static readonly Dictionary<IntPtr, Sp> byShip = new Dictionary<IntPtr, Sp>();
        static IntPtr battle; static float bt; static int logged, errors;
        static float WS() { float w = TorpedoSettings.World(10f) / 10f; return (w <= 0.1f || w > 20f) ? 3.2f : w; }
        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }
        static bool Night() { try { return BattleAi.IsNight(); } catch { return false; } }

        // the spotting bonus needs the aircraft up (when the aircraft are drawn)
        internal static bool Airborne(Ship s)
        {
            if (On == null || !On.Value || s == null) return true;
            return byShip.TryGetValue(s.Pointer, out var p) && (p.P == Ph.Climb || p.P == Ph.Spot);
        }

        static void Clear()
        {
            foreach (var p in sps) { try { if (p.Go != null) UnityEngine.Object.Destroy(p.Go); } catch { } }
            sps.Clear(); byShip.Clear(); bt = 0f; shotPlanes = 0;
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) { if (sps.Count > 0) Clear(); return; }
            try
            {
                if (!GameManager.IsBattle) { if (sps.Count > 0 || battle != IntPtr.Zero) { Clear(); battle = IntPtr.Zero; } return; }
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (b == null || b.Ships == null || b.Ships.Count == 0) return;
                if (b.Pointer != battle) { Clear(); battle = b.Pointer; logged = 0; }
                float dt = Time.deltaTime; if (dt <= 0f) return;
                bt += dt;
                // one aircraft per ship that has spotting, launched 15-75 s into the battle, by day
                if (!Night())
                    for (int i = 0; i < b.Ships.Count && sps.Count < 12; i++)
                    {
                        var s = b.Ships[i]; if (!Alive(s) || byShip.ContainsKey(s.Pointer)) continue;
                        if (NavalAviation.SpotLevelOf(s) <= 0) { byShip[s.Pointer] = new Sp { S = s, P = Ph.Gone }; continue; }
                        var p = new Sp { S = s, P = Ph.Wait, LaunchAt = bt + 15f + (sps.Count % 6) * 10f, Side = (sps.Count % 2 == 0) ? 1f : -1f, Seed = UnityEngine.Random.value * 100f };
                        sps.Add(p); byShip[s.Pointer] = p;
                    }
                for (int i = sps.Count - 1; i >= 0; i--)
                {
                    var p = sps[i];
                    try { Step(p, dt); }
                    catch (Exception e) { if (errors++ < 10) Log("move: " + e.Message); p.P = Ph.Gone; }
                    if (p.P == Ph.Gone) { try { if (p.Go != null) UnityEngine.Object.Destroy(p.Go); } catch { } p.Go = null; sps.RemoveAt(i); }
                }
            }
            catch (Exception e) { if (errors++ < 10) Log("tick: " + e.Message); }
        }

        static string ModelFor(Ship s) { int y = 1930; try { y = (int)s.GetYear(s); } catch { } return y < 1930 ? "short_184" : "swordfish_i"; }

        static Ship Target(Ship s)
        {
            try
            {
                var en = s.enemies; if (en == null) return null;
                foreach (var kv in en) { var a = kv.Value; if (a != null && a.target != null && Alive(a.target)) return a.target; }
            }
            catch { }
            return null;
        }

        static void Step(Sp p, float dt)
        {
            var s = p.S; float ws = WS();
            bool alive = Alive(s);
            if (p.P == Ph.Wait)
            {
                if (!alive || Night()) { p.P = Ph.Gone; return; }
                if (bt < p.LaunchAt) return;
                p.Go = AirModels.Spawn(ModelFor(s), AirStrike.Scale != null ? AirStrike.Scale.Value : 1f, out p.Ctl);
                if (p.Go == null) { p.P = Ph.Gone; if (logged++ < 4) Log("no aircraft model - spotting aircraft not drawn"); return; }
                var f = s.transform.forward; f.y = 0f; f.Normalize();
                p.Dir = Quaternion.Euler(0f, 35f * p.Side, 0f) * f;          // catapult trained out ~35 deg off the bow
                p.Pos = s.transform.position + Vector3.up * 12f + f * 8f * ws;  // on the catapult, amidships-forward
                p.P = Ph.Catapult; p.T = 0f;
                if (logged++ < 8) Log($"{s.name}: spotting aircraft ({ModelFor(s)}) fired off the catapult");
                try { Dispatch.Air($"{Dispatch.Ship(s.name)}: spotting aircraft catapulted."); } catch { }
            }
            if (p.Go == null) { p.P = Ph.Gone; return; }
            p.T += dt;
            Vector3 old = p.Dir; float v; Vector3 goal;
            switch (p.P)
            {
                case Ph.Catapult:
                    // the trolley run: ~2.5 g for about a second and a half, then the nose comes up
                    v = Mathf.Min(30f, 2.5f * 9.81f * p.T) * ws;
                    p.Pos += p.Dir * v * dt;
                    var tilt = Quaternion.AngleAxis(-Mathf.Min(12f, p.T * 10f), Vector3.Cross(p.Dir, Vector3.up)) * p.Dir;
                    p.Go.transform.position = p.Pos; p.Go.transform.rotation = Quaternion.LookRotation(tilt, Vector3.up);
                    Props(p, dt);
                    if (p.T > 1.3f) { p.P = Ph.Climb; p.T = 0f; }
                    return;
                case Ph.Climb:
                    if (!alive) { p.P = Ph.Home; break; }
                    var t0 = Target(s); var c0 = Circuit(s, t0);
                    goal = c0; goal.y = 900f;
                    if (p.Pos.y > 800f) p.P = Ph.Spot;
                    Fly(p, goal, 40f * ws, 0.6f, dt, 6f);
                    break;
                case Ph.Spot:
                    if (!alive || Night()) { p.P = Ph.Home; if (logged++ < 12) Log($"{(s != null ? s.name : "?")}: spotting aircraft {(alive ? "returns at dusk" : "has lost its ship")}"); break; }
                    var t1 = Target(s); var c1 = Circuit(s, t1);
                    p.Ang += dt * 0.12f * p.Side;                                  // a wide circuit, ~50 s per turn
                    goal = c1 + new Vector3(Mathf.Cos(p.Ang), 0f, Mathf.Sin(p.Ang)) * 1500f; goal.y = 900f;
                    Fly(p, goal, 40f * ws, 0.8f, dt, 0f);
                    break;
                case Ph.Home:
                    if (!alive) { goal = p.Pos + new Vector3(p.Dir.x, 0f, p.Dir.z) * 2000f; goal.y = 600f; Fly(p, goal, 40f * ws, 0.5f, dt, 2f); if (p.T > 60f) p.P = Ph.Gone; break; }
                    // to the slick in the ship's wake, 400 m astern
                    var aft = s.transform.position - s.transform.forward * 400f;
                    goal = aft; goal.y = Flat(aft - p.Pos) > 1500f ? 300f : 2f;
                    Fly(p, goal, (Flat(aft - p.Pos) > 1500f ? 40f : 25f) * ws, 0.7f, dt, 4f);
                    if (p.Pos.y < 4f && Flat(aft - p.Pos) < 300f) { p.P = Ph.Alight; p.T = 0f; }
                    break;
                case Ph.Alight:
                    // on the water: taxi up to the ship's side, then hoisted in
                    var side = s != null ? s.transform.position - s.transform.right * 20f : p.Pos;
                    var d = side - p.Pos; d.y = 0f; p.Pos += (d.sqrMagnitude > 1f ? d.normalized : p.Dir) * Mathf.Min(6f * ws, d.magnitude) * dt; p.Pos.y = 1f;
                    if (d.sqrMagnitude > 1f) p.Dir = Vector3.Slerp(p.Dir, d.normalized, dt);
                    p.Go.transform.position = p.Pos; p.Go.transform.rotation = Quaternion.LookRotation(new Vector3(p.Dir.x, 0f, p.Dir.z), Vector3.up);
                    if (p.T > 25f) { p.P = Ph.Gone; if (logged++ < 12) Log($"{(s != null ? s.name : "?")}: spotting aircraft hoisted in"); }
                    return;
            }
            Pose(p, old, dt);
            Props(p, dt);
            Shoot(p);
        }

        // 1.11.421 (author 30.10 06:47): test shots of the spotting aircraft (switch AirStrikeScreenshots) - the first two aircraft
        // only: off the catapult (from the deck beside it), climbing away (from behind), and on its circuit looking past it at the
        // target and the fall of shot
        static int shotPlanes;
        static void Shoot(Sp p)
        {
            if (p.Go == null || p.S == null) return;
            if (p.Shots == 0 && p.P == Ph.Catapult && p.T > 0.6f)
            {
                if (shotPlanes >= 2) { p.Shots = 9; return; }
                shotPlanes++; p.Shots = 1;
                var right = Vector3.Cross(Vector3.up, p.Dir).normalized;
                var cam = p.Pos - p.Dir * 40f + right * 25f * -p.Side + Vector3.up * 14f;
                AirStrike.ShotAt(cam, p.Pos + p.Dir * 20f, 55f, "spotter_catapult");
            }
            else if (p.Shots == 1 && p.P == Ph.Climb && p.T > 4f)
            {
                p.Shots = 2; var f = new Vector3(p.Dir.x, 0f, p.Dir.z).normalized;
                AirStrike.ShotAt(p.Pos - f * 45f + Vector3.up * 10f, p.Pos + f * 60f, 55f, "spotter_climb");
            }
            else if (p.Shots == 2 && p.P == Ph.Spot && p.T > 25f)
            {
                p.Shots = 3; var t = Target(p.S); var f = new Vector3(p.Dir.x, 0f, p.Dir.z).normalized;
                var look = t != null ? t.transform.position : p.Pos + f * 2000f + Vector3.down * 900f;
                var to = (look - p.Pos); to.y = 0f; to.Normalize();
                AirStrike.ShotAt(p.Pos - to * 40f + Vector3.up * 12f, look, 45f, "spotter_spotting");
            }
        }

        // the circuit: on the own side of the target, 6.5 km from it (beyond its light AA), on the line to the own ship; no target:
        // over the own ship
        static Vector3 Circuit(Ship s, Ship t)
        {
            var sp = s.transform.position;
            if (t == null) return sp;
            var tp = t.transform.position; var d = sp - tp; d.y = 0f;
            float dist = d.magnitude; if (dist < 1f) return sp;
            float keep = Mathf.Min(dist * 0.6f, Mathf.Max(6500f, AirStrike.AaRange != null ? AirStrike.AaRange.Value + 2500f : 6500f));
            return tp + d / dist * keep;
        }

        static void Fly(Sp p, Vector3 goal, float v, float turn, float dt, float climb)
        {
            var d = goal - p.Pos; var nd = d.sqrMagnitude > 1f ? d.normalized : p.Dir;
            if (climb > 0f) { float want = Mathf.Clamp((goal.y - p.Pos.y) / 200f, -0.25f, 0.25f); nd = new Vector3(nd.x, 0f, nd.z).normalized; nd.y = want; nd.Normalize(); }
            p.Dir = Vector3.Slerp(p.Dir, nd, Mathf.Clamp01(dt * turn)).normalized;
            p.Pos += p.Dir * v * dt;
            if (p.Pos.y < 1f) p.Pos.y = 1f;
        }

        static void Pose(Sp p, Vector3 oldDir, float dt)
        {
            Vector3 fa = new Vector3(oldDir.x, 0f, oldDir.z), fb = new Vector3(p.Dir.x, 0f, p.Dir.z);
            float rate = (fa.sqrMagnitude > 0.001f && fb.sqrMagnitude > 0.001f && dt > 0f) ? Vector3.SignedAngle(fa, fb, Vector3.up) / dt : 0f;
            p.Roll = Mathf.Lerp(p.Roll, Mathf.Clamp(-rate * 1.6f, -45f, 45f), Mathf.Clamp01(dt * 2.5f));
            float t = Time.time;
            float bump = (Mathf.PerlinNoise(p.Seed, t * 0.5f) - 0.5f) * 6f;
            p.Go.transform.position = p.Pos + Vector3.up * bump;
            p.Go.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up) * Quaternion.Euler(0f, 0f, p.Roll + bump);
        }
        static void Props(Sp p, float dt)
        {
            try { if (p.Ctl != null) foreach (var pr in p.Ctl.Props) if (pr != null) pr.Rotate(0f, 0f, 1800f * dt, Space.Self); } catch { }
        }
    }
}
