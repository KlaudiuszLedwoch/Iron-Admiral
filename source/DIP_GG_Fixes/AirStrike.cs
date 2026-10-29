// 1.11.372 (#85 step 2 prototype). Author 29.10 12:05: carriers stay off the map (beyond the horizon), but the player should get a
// battle with enemy aircraft; a surface fleet catching a carrier at short range comes later (step 3).
// Prototype (switch AirStrikeTest, OFF by default - for test battles): in daylight, waves of torpedo aircraft of the AI side come in
// from beyond the horizon, attack the largest ship of the player's side from both beams (anvil attack) and fly away.
// - aircraft are simple shapes built at run time (no game models exist); speed AirStrikeSpeed m/s, approach at 300 m, torpedo run in
//   at 30 m;
// - every torpedo is a real game torpedo (Torpedo.Create) launched from the drop point; the game resolves its run, the hit and the
//   damage. The torpedo data come from a torpedo tube of a ship of the attacking side (no tube - no wave, logged);
// - anti-aircraft fire is abstract (author 29.10 12:41-12:42): light guns of 2-3 in fire at any height, guns over 3 in only at
//   aircraft running in low (splash barrage, AirStrikeSplashRange); every second inside AirStrikeAaRange an aircraft can be shot down - the chance
//   grows with the number of light guns of 2-3 in on the ships in range and with the year (1910s weak, 1940s strong);
// - no waves at night. Log lines "[AirStrike] ...".
// History: Taranto 1940 (21 Swordfish, 5 torpedo hits, 2 lost), Force Z 1941 (land-based, 34 torpedo aircraft, 3-4 lost); aircraft
// struck from beyond the horizon in daylight and good weather.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class AirStrike
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<int> Waves, PerWave;
        internal static MelonPreferences_Entry<float> FirstAfter, Interval, SpeedMs, AaRange, AaPerGun, SplashRange, SplashPerGun, TorpRunM, TorpKn, Scale;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AirStrikeTest", false, null, "Battle (test): torpedo aircraft of the AI side come in waves from beyond the horizon in daylight and attack the largest ship of the player's side from both beams; light guns of 2-3 in on the defending ships can shoot them down, heavier guns fire splash barrages at aircraft running in low (prototype of carrier and shore-based air strikes, #85)");
            Waves = cat.CreateEntry("AirStrikeWaves", 3, null, "Battle (test): number of air strike waves per battle");
            PerWave = cat.CreateEntry("AirStrikeAircraftPerWave", 6, null, "Battle (test): torpedo aircraft in one wave");
            FirstAfter = cat.CreateEntry("AirStrikeFirstAfter", 60f, null, "Battle (test): seconds of battle time before the first wave arrives");
            Interval = cat.CreateEntry("AirStrikeInterval", 240f, null, "Battle (test): seconds of battle time between waves");
            SpeedMs = cat.CreateEntry("AirStrikeSpeed", 55f, null, "Battle (test): aircraft speed in m/s (55 = about 107 kn, a 1930s torpedo bomber)");
            AaRange = cat.CreateEntry("AirStrikeAaRange", 4000f, null, "Battle (test): range (m) of the defending ships' anti-aircraft fire");
            AaPerGun = cat.CreateEntry("AirStrikeAaPerGun", 0.00004f, null, "Battle (test): chance per second per light gun of 2-3 in in range to shoot down an aircraft (scaled by the year)");
            SplashRange = cat.CreateEntry("AirStrikeSplashRange", 2700f, null, "Battle (test): range (m) within which guns over 3 in fire into the sea ahead of torpedo aircraft running in low, to bring them down with the splashes");
            SplashPerGun = cat.CreateEntry("AirStrikeSplashPerGun", 0.00002f, null, "Battle (test): chance per second per gun over 3 in to bring down a low-flying torpedo aircraft with its splashes");
            TorpRunM = cat.CreateEntry("AirStrikeTorpedoRun", 3000f, null, "Battle (test): run of an aerial torpedo (m)");
            TorpKn = cat.CreateEntry("AirStrikeTorpedoSpeed", 35f, null, "Battle (test): speed of an aerial torpedo (kn)");
            Scale = cat.CreateEntry("AirStrikeAircraftScale", 2.5f, null, "Battle (test): drawing scale of the aircraft (1 = true size, about 12 m span)");
        }

        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirStrike] " + m); }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }

        enum Phase { Approach, Run, Away, Down }
        class Plane
        {
            public GameObject Go; public Phase P; public Vector3 Pos, Dir; public Ship Target; public float Side; public bool Dropped;
            public float Fall; public int Wave;
        }
        static readonly List<Plane> planes = new List<Plane>();
        static IntPtr battle; static float battleT; static int wavesSent; static float nextWave;
        static readonly Dictionary<int, int[]> waveStats = new Dictionary<int, int[]>(); // wave -> sent, dropped, lost, of them to the splash barrage
        static readonly Dictionary<IntPtr, (int light, int heavy)> aaGuns = new Dictionary<IntPtr, (int light, int heavy)>();
        static int errors, drops; static readonly HashSet<int> reported = new HashSet<int>();

        static void Clear()
        {
            foreach (var p in planes) { try { if (p.Go != null) UnityEngine.Object.Destroy(p.Go); } catch { } }
            planes.Clear(); waveStats.Clear(); reported.Clear(); aaGuns.Clear(); wavesSent = 0; battleT = 0f; nextWave = 0f;
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) { if (planes.Count > 0) Clear(); return; }
            try
            {
                if (!GameManager.IsBattle) { if (planes.Count > 0 || battle != IntPtr.Zero) { Summary(); Clear(); battle = IntPtr.Zero; } return; }
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (b == null || b.Ships == null || b.Ships.Count == 0) return;
                if (b.Pointer != battle) { Clear(); battle = b.Pointer; nextWave = FirstAfter.Value; lastTube = null; }
                if (lastTube == null) lastTube = FindTube(b.Ships);
                float dt = Time.deltaTime; if (dt <= 0f) return;
                battleT += dt;
                if (wavesSent < Waves.Value && battleT >= nextWave)
                {
                    nextWave = battleT + Interval.Value;
                    bool night = false; try { night = BattleAi.IsNight(); } catch { }
                    if (night) { if (wavesSent == 0) Log("night - no air strike while it is dark"); }
                    else Launch(b.Ships);
                    wavesSent++;
                }
                Move(dt, b.Ships);
                // a wave whose aircraft have all gone (away or down): its result once
                foreach (var kv in waveStats)
                {
                    if (reported.Contains(kv.Key)) continue;
                    bool any = false; foreach (var q in planes) if (q.Wave == kv.Key) { any = true; break; }
                    if (any) continue;
                    reported.Add(kv.Key);
                    Log($"wave {kv.Key} result: {kv.Value[0]} aircraft, {kv.Value[1]} torpedoes dropped, {kv.Value[2]} shot down ({kv.Value[3]} by the splash barrage of heavy guns)");
                }
            }
            catch (Exception e) { if (errors++ < 10) Log("tick: " + e.Message); }
        }

        static bool Player(Ship s) { try { return s.player != null && s.player.isMain; } catch { return false; } }

        static void Launch(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            // target: the largest ship of the player's side; torpedo data: a tube of a ship of the attacking side
            Ship target = null; float best = -1f; Part tube = null; Vector3 aiC = Vector3.zero; int aiN = 0;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (!Alive(s)) continue;
                if (Player(s)) { float t = 0f; try { t = s.Tonnage(); } catch { } if (t > best) { best = t; target = s; } }
                else
                {
                    aiC += s.transform.position; aiN++;
                }
            }
            // 1.11.375 (test 29.10 13:11: the AI surface ships were all sunk by the second wave and it found no tube): the torpedo
            // data come from a tube found at the start of the battle; the aircraft fly from beyond the horizon, not from those ships
            tube = lastTube ?? FindTube(ships); if (tube != null) lastTube = tube;
            int wave = wavesSent + 1;
            if (target == null) { Log($"wave {wave}: no target of the player's side"); return; }
            if (tube == null) { Log($"wave {wave}: no ship of the attacking side carries torpedo tubes - torpedo data unavailable, wave not sent"); return; }
            var tp = target.transform.position; tp.y = 0f;
            // come in from the side of the attacking fleet, from beyond the horizon (14 km)
            Vector3 from = aiN > 0 ? (aiC / aiN) - tp : -target.transform.forward; from.y = 0f;
            if (from.sqrMagnitude < 1f) from = Vector3.forward; from.Normalize();
            int n = Math.Max(1, PerWave.Value);
            waveStats[wave] = new int[4]; waveStats[wave][0] = n;
            for (int k = 0; k < n; k++)
            {
                float side = (k % 2 == 0) ? 1f : -1f;                       // half on each beam
                var right = Vector3.Cross(Vector3.up, from);
                var start = tp + from * 14000f + right * (side * 800f + (k / 2) * 120f * side) + Vector3.up * 300f;
                var pl = new Plane { Go = Build(), P = Phase.Approach, Pos = start, Target = target, Side = side, Wave = wave };
                planes.Add(pl);
            }
            Log($"wave {wave}: {n} torpedo aircraft from beyond the horizon against {target.name} ({best:0} t), attacking from both beams; torpedo data from {tube.ship?.name}");
        }
        static Part lastTube;
        static Part FindTube(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            for (int i = 0; ships != null && i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || Player(s)) continue;
                try { var ts = s.torpedoesAll; if (ts != null && ts.Count > 0 && ts[0] != null) return ts[0]; } catch { }
            }
            return null;
        }

        static GameObject Build()
        {
            var root = new GameObject("IA_AirStrike_Plane");
            float s = Scale.Value;
            var col = new Color(0.22f, 0.24f, 0.2f);
            void Box(Vector3 size, Vector3 at)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try { var c = g.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); } catch { }
                try { g.GetComponent<Renderer>().material.color = col; } catch { }
                g.transform.SetParent(root.transform, false); g.transform.localScale = size * s; g.transform.localPosition = at * s;
            }
            Box(new Vector3(1.2f, 1.2f, 10f), Vector3.zero);            // fuselage
            Box(new Vector3(13f, 0.25f, 2f), new Vector3(0f, 0.3f, 1f)); // wing
            Box(new Vector3(4.5f, 0.2f, 1.2f), new Vector3(0f, 0.4f, -4.4f)); // tailplane
            Box(new Vector3(0.2f, 2f, 1.2f), new Vector3(0f, 1.3f, -4.4f));   // fin
            return root;
        }

        // aim point for the run (author 29.10 12:40: torpedo aircraft attacked from the beam - the longest target): abeam of the point the
        // target will reach (80 deg off its bow) on the plane's side, 1000 m out - both halves of the wave from opposite beams (anvil)
        static Vector3 RunPoint(Plane p)
        {
            var t = p.Target.transform; var f = t.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
            float v = 0f; try { v = BattleAi.Speed(p.Target); } catch { }
            var ahead = t.position + f * Mathf.Clamp(v * 60f, 0f, 1500f);
            var dir = Quaternion.Euler(0f, p.Side * 30f, 0f) * f;
            var rp = ahead + dir * 1200f; rp.y = 30f; return rp;
        }

        static void Move(float dt, Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            float v = SpeedMs.Value;
            for (int i = planes.Count - 1; i >= 0; i--)
            {
                var p = planes[i];
                try
                {
                    if (p.Go == null) { planes.RemoveAt(i); continue; }
                    if (p.P == Phase.Down)
                    {
                        p.Fall += dt; p.Pos += p.Dir * v * 0.6f * dt + Vector3.down * (20f + 30f * p.Fall) * dt;
                        p.Go.transform.position = p.Pos; p.Go.transform.Rotate(0f, 0f, 200f * dt, Space.Self);
                        if (p.Pos.y <= 0f) { UnityEngine.Object.Destroy(p.Go); planes.RemoveAt(i); }
                        continue;
                    }
                    if (!Alive(p.Target) && !p.Dropped) { p.P = Phase.Away; }
                    Vector3 goal;
                    if (p.P == Phase.Approach) { goal = RunPoint(p); goal.y = 300f; if (Flat(goal - p.Pos) < 3000f) p.P = Phase.Run; }
                    else if (p.P == Phase.Run)
                    {
                        goal = RunPoint(p);
                        if (Flat(goal - p.Pos) < 150f) { Drop(p); p.P = Phase.Away; }
                    }
                    else goal = p.Pos + new Vector3(p.Dir.x, 0f, p.Dir.z) * 1000f + Vector3.up * 40f;
                    if (p.P == Phase.Away)
                    {
                        if (p.Dir.sqrMagnitude < 0.01f) p.Dir = Vector3.forward;
                        if (p.Pos.y < 250f) goal.y = p.Pos.y + 200f;
                        var tp = p.Target != null ? p.Target.transform.position : p.Pos;
                        if (Flat(p.Pos - tp) > 12000f) { UnityEngine.Object.Destroy(p.Go); planes.RemoveAt(i); continue; }
                    }
                    var d = goal - p.Pos; var nd = d.sqrMagnitude > 1f ? d.normalized : p.Dir;
                    p.Dir = p.Dir.sqrMagnitude < 0.01f ? nd : Vector3.Slerp(p.Dir, nd, Mathf.Clamp01(dt * 0.8f)).normalized;
                    p.Pos += p.Dir * v * dt;
                    p.Go.transform.position = p.Pos;
                    p.Go.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up);
                    if (p.P != Phase.Away || p.Pos.y < 200f) Aa(p, dt, ships);
                }
                catch (Exception e) { if (errors++ < 10) Log("move: " + e.Message); try { UnityEngine.Object.Destroy(p.Go); } catch { } planes.RemoveAt(i); }
            }
        }
        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        static void Drop(Plane p)
        {
            if (p.Dropped) return; p.Dropped = true;
            try
            {
                if (lastTube == null) return;
                var t = p.Target.transform; var tp = t.position;
                float tv = TorpKn.Value * 0.5144f, sv = 0f; try { sv = BattleAi.Speed(p.Target); } catch { }
                // lead the target: aim where it will be when a torpedo running straight from here reaches its track
                var start = p.Pos; start.y = 0f; var f = t.forward; f.y = 0f; f.Normalize();
                float dist = Flat(tp - start), tt = dist / Mathf.Max(1f, tv);
                var aim = tp + f * sv * tt; aim.y = 0f;
                var dir = (aim - start); dir.y = 0f; dir.Normalize();
                float speedArg = TorpedoSettings.World(tv);
                float duration = TorpRunM.Value / Mathf.Max(0.1f, speedArg);
                var tor = Torpedo.Create(lastTube, start, dir, duration, speedArg);
                waveStats[p.Wave][1]++;
                if (drops++ < 30) Log($"wave {p.Wave}: torpedo dropped {dist:0} m from {p.Target.name}, {Vector3.Angle(f, -dir):0} deg off her bow{(tor == null ? " - the game returned no torpedo" : "")}");
            }
            catch (Exception e) { if (errors++ < 10) Log("drop: " + e.Message); }
        }

        // light guns 2-3 in (anti-aircraft fire at any height, author 29.10 12:41-12:42) and heavier guns (splash barrage against
        // aircraft running in low, author 29.10 12:42: "when they flew low the heavy artillery tried to bring them down with water
        // splashes"; a 1922 Proceedings article quotes a claimed "heavy water barrage at from 2,000 to 3,000 yards" against torpedo
        // planes, unproven then; Yamato fired her 18 in guns at the attacking aircraft in April 1945)
        static (int light, int heavy) Guns(Ship s)
        {
            if (aaGuns.TryGetValue(s.Pointer, out var n)) return n;
            int l = 0, h = 0;
            try
            {
                var parts = s.parts;
                for (int i = 0; parts != null && i < parts.Count; i++)
                {
                    var pd = parts[i] != null ? parts[i].data : null; if (pd == null || !pd.isGun) continue;
                    float cal = 99f; try { cal = G.GameData.GunData(pd).caliberInch; } catch { }
                    if (cal >= 1.9f && cal <= 3.1f) l++; else if (cal > 3.1f) h++;
                }
            }
            catch { }
            n = (l, h); aaGuns[s.Pointer] = n; return n;
        }

        static float YearF(Ship s)
        {
            int y = 1925; try { y = (int)s.GetYear(s); } catch { }
            if (y < 1920) return 0.35f; if (y < 1930) return 0.6f; if (y < 1937) return 1f; if (y < 1942) return 1.5f; return 2.5f;
        }

        static void Aa(Plane p, float dt, Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            float r = AaRange.Value, miss = 1f, missLight = 1f; int guns = 0, heavy = 0;
            bool low = p.Pos.y < 80f;   // the torpedo run (30 m): within reach of the splash barrage
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (!Alive(s) || !Player(s)) continue;
                float d = Flat(s.transform.position - p.Pos);
                if (d > r) continue;
                var g = Guns(s); guns += g.light;
                float ml = Mathf.Pow(1f - Mathf.Clamp01(AaPerGun.Value * YearF(s)), g.light * dt);
                miss *= ml; missLight *= ml;
                if (low && d <= SplashRange.Value && g.heavy > 0) { heavy += g.heavy; miss *= Mathf.Pow(1f - Mathf.Clamp01(SplashPerGun.Value), g.heavy * dt); }
            }
            if (guns == 0 && heavy == 0) return;
            if (UnityEngine.Random.value > miss)
            {
                // which fire brought it down: in proportion to the share of each in the total chance
                float pl = 1f - missLight, pt = 1f - miss; bool splash = heavy > 0 && UnityEngine.Random.value * pt > pl;
                p.P = Phase.Down; waveStats[p.Wave][2]++; if (splash) waveStats[p.Wave][3]++;
                if (waveStats[p.Wave][2] <= 4) Log($"wave {p.Wave}: aircraft shot down {(splash ? $"by the splash barrage of the heavy guns ({heavy} gun(s) firing into the sea ahead of it)" : $"by light anti-aircraft guns ({guns} gun(s) of 2-3 in in range)")}{(p.Dropped ? ", after its drop" : ", before its drop")}");
            }
        }

        static void Summary()
        {
            foreach (var kv in waveStats) if (!reported.Contains(kv.Key)) Log($"wave {kv.Key} result: {kv.Value[0]} aircraft, {kv.Value[1]} torpedoes dropped, {kv.Value[2]} shot down ({kv.Value[3]} by the splash barrage of heavy guns)");
        }
    }
}
