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
        internal static MelonPreferences_Entry<bool> On, Screens;
        internal static MelonPreferences_Entry<int> Waves, PerWave;
        internal static MelonPreferences_Entry<float> FirstAfter, Interval, SpeedMs, AaRange, AaPerGun, SplashRange, SplashPerGun, TorpRunM, TorpKn, Scale;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AirStrikeTest", false, null, "Battle (test): torpedo aircraft of the AI side come in waves from beyond the horizon in daylight and attack the largest ship of the player's side from both beams; light guns of 2-3 in on the defending ships can shoot them down, heavier guns fire splash barrages at aircraft running in low (prototype of carrier and shore-based air strikes, #85)");
            Screens = cat.CreateEntry("AirStrikeScreenshots", false, null, "Battle (test): a series of screenshots of every air strike - the run-in, each torpedo drop and each aircraft shot down (UserData\\IronAdmiral\\screens)");
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
            planes.Clear(); waveStats.Clear(); reported.Clear(); track.Clear(); shots = 0; aaGuns.Clear(); wavesSent = 0; battleT = 0f; nextWave = 0f;
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
        // 1.11.376 (test 29.10 14:05, 1918: 13 torpedoes dropped 1640 m out at 47 deg off the bow, no hit; the 30 deg / 1200 m point
        // had stayed in by mistake): the drop point is abeam (80 deg) of where the target will be when the torpedo arrives, 1000 m out.
        // Everything in world units: the battle runs ships and torpedoes faster than real time (a torpedo of 35 kn = 18 m/s runs at
        // about 3.4 world units/s per m/s - Ship.ModifySpeedTorpedo), so the aircraft fly at the same scale and the target's velocity
        // is measured from its movement, not taken from the game's speed units.
        static float Scale0 = -1f;
        static float WorldScale() { if (Scale0 <= 0f) { Scale0 = TorpedoSettings.World(10f) / 10f; if (Scale0 <= 0.1f || Scale0 > 20f) Scale0 = 3.2f; } return Scale0; }
        static readonly Dictionary<IntPtr, (Vector3 pos, Vector3 vel, float t)> track = new Dictionary<IntPtr, (Vector3, Vector3, float)>();
        static Vector3 Vel(Ship s)
        {
            var pos = s.transform.position; pos.y = 0f; float now = battleT;
            if (track.TryGetValue(s.Pointer, out var k))
            {
                float dt = now - k.t; if (dt < 0.5f) return k.vel;
                var v = (pos - k.pos) / dt; v = Vector3.Lerp(k.vel, v, k.vel == Vector3.zero ? 1f : 0.5f);
                track[s.Pointer] = (pos, v, now); return v;
            }
            track[s.Pointer] = (pos, Vector3.zero, now); return Vector3.zero;
        }
        static Vector3 RunPoint(Plane p)
        {
            var t = p.Target.transform; var f = t.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
            var vel = Vel(p.Target); float tw = TorpedoSettings.World(TorpKn.Value * 0.5144f), pv = SpeedMs.Value * WorldScale();
            float toDrop = Flat(t.position - p.Pos) / Mathf.Max(1f, pv);
            var future = t.position + vel * (toDrop + 1000f / Mathf.Max(1f, tw));
            var dir = Quaternion.Euler(0f, p.Side * 80f, 0f) * f;
            var rp = future + dir * 1000f; rp.y = 30f; return rp;
        }

        static void Move(float dt, Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            float v = SpeedMs.Value * WorldScale();
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
                    if (p.P == Phase.Approach) { goal = RunPoint(p); goal.y = 300f; if (Flat(goal - p.Pos) < 3000f) { p.P = Phase.Run; Shot($"wave{p.Wave}_runin", 1.5f, p); } }
                    else if (p.P == Phase.Run)
                    {
                        goal = RunPoint(p);
                        Shot($"wave{p.Wave}_run", 3f, p);
                        if (Flat(goal - p.Pos) < 300f) { Drop(p); p.P = Phase.Away; Shot($"wave{p.Wave}_drop", 1.2f, p); }
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
                    p.Dir = p.Dir.sqrMagnitude < 0.01f ? nd : Vector3.Slerp(p.Dir, nd, Mathf.Clamp01(dt * 1.5f)).normalized;
                    p.Pos += p.Dir * v * dt;
                    p.Go.transform.position = p.Pos;
                    p.Go.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up);
                    if (p.P != Phase.Away || p.Pos.y < 200f) Aa(p, dt, ships);
                }
                catch (Exception e) { if (errors++ < 10) Log("move: " + e.Message); try { UnityEngine.Object.Destroy(p.Go); } catch { } planes.RemoveAt(i); }
            }
        }
        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        // 1.11.377 (author 29.10 14:59: "take a screenshot of the air strike - or a series"): screenshots at the run-in, then every 3 s
        // of real time while aircraft are running in, at each drop and at each aircraft shot down (at most 60 per battle)
        static float nextShot; static int shots;
        // 1.11.378 (first series 29.10 15:04: the unattended test keeps the Start Battle panel on screen and the battle camera was
        // not on the aircraft): the series is rendered by a camera of its own placed at the aircraft, to a texture, without the UI
        // 1.11.379 (author 29.10 15:51: "screenshots from the ship, from the observer - the player watching; make the ship visible"):
        // the series is rendered through the game's own battle camera (real sea), moved for that one render to an observer at the
        // attacked ship - low, behind the ship on the side away from the aircraft, the ship in the foreground and the aircraft beyond
        // it; every other shot from her bridge. Name tags are left out (UI layer), the interface is not on the picture. Separate
        // timers for the run-in series and for drops / aircraft shot down.
        static float nextDrop;
        static void Shot(string tag, float gap, Plane p = null)
        {
            if (Screens == null || !Screens.Value || shots >= 60) return;
            bool ev = tag.EndsWith("drop") || tag.EndsWith("shotdown");
            if (ev) { if (Time.realtimeSinceStartup < nextDrop) return; nextDrop = Time.realtimeSinceStartup + gap; }
            else { if (Time.realtimeSinceStartup < nextShot) return; nextShot = Time.realtimeSinceStartup + gap; }
            shots++;
            if (p != null && p.Go != null && p.Target != null && ReShadeOn()) { if (!busy) MelonCoroutines.Start(ScreenShot(p, tag)); return; }
            if (p == null || p.Go == null || p.Target == null || !View(p, tag)) AutoTest.Shot("airstrike_" + tag);
        }

        // 1.11.380 (author 29.10 15:52: "make them nice - high resolution and ReShade"): with ReShade installed in the game folder
        // the shot is taken on screen, through ReShade: the battle camera's controller is paused, the camera moved to the observer
        // (or the bridge), time frozen, the interface hidden (canvases and the mod's own panels), ReShade's screenshot key
        // (PrintScreen, ReShade.ini KeyScreenshot=44) pressed, then everything restored. ReShade saves the picture at the screen's
        // resolution in the game folder's screenshots folder (ReShade.ini SavePath).
        internal static bool Hiding; static bool busy;
        static int reshade = -1;
        static bool ReShadeOn()
        {
            if (reshade < 0) { try { var g = MelonUtils.GameDirectory; reshade = System.IO.File.Exists(System.IO.Path.Combine(g, "ReShade.ini")) && System.IO.File.Exists(System.IO.Path.Combine(g, "dxgi.dll")) ? 1 : 0; } catch { reshade = 0; } Log("screenshots: " + (reshade == 1 ? "through ReShade (PrintScreen)" : "rendered by the mod (no ReShade)")); }
            return reshade == 1;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        static void Pose(Plane p, string tag, out Vector3 cam, out Vector3 look, out float fov, out bool bridge)
        {
            var st = p.Target.transform; var tp = st.position; tp.y = 0f; var pp = p.Pos;
            var toPlane = pp - tp; toPlane.y = 0f; if (toPlane.sqrMagnitude < 1f) toPlane = st.right; toPlane.Normalize();
            var f = st.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
            bridge = (shots % 2) == 0 && !tag.EndsWith("shotdown");
            if (bridge) { cam = tp + toPlane * 22f - f * 10f + Vector3.up * 40f; look = new Vector3(pp.x, Mathf.Max(15f, pp.y * 0.8f), pp.z); fov = 45f; }   // the bridge wing on the side of the attack (test 15:56: from inside the superstructure)
            else { cam = tp - toPlane * 420f - f * 160f + Vector3.up * 45f; look = Vector3.Lerp(tp, new Vector3(pp.x, 0f, pp.z), 0.5f) + Vector3.up * 30f; fov = 40f; }
        }
        static System.Collections.IEnumerator ScreenShot(Plane p, string tag)
        {
            busy = true;
            Camera c = Camera.main; CameraController cc = null; bool ccWas = false;
            Vector3 oPos = Vector3.zero; Quaternion oRot = Quaternion.identity; float oFov = 60f, oScale = 1f; int oMask = -1;
            var canv = new List<Canvas>(); bool set = false;
            try
            {
                if (c != null && p.Target != null)
                {
                    Pose(p, tag, out var cam, out var look, out var fov, out var bridge);
                    try { cc = UnityEngine.Object.FindObjectOfType<CameraController>(); if (cc != null) { ccWas = cc.enabled; cc.enabled = false; } } catch { }
                    oPos = c.transform.position; oRot = c.transform.rotation; oFov = c.fieldOfView; oMask = c.cullingMask; oScale = Time.timeScale;
                    Time.timeScale = 0f; Hiding = true;
                    try { foreach (var x in UnityEngine.Object.FindObjectsOfType<Canvas>()) if (x != null && x.enabled) { x.enabled = false; canv.Add(x); } } catch { }
                    int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) c.cullingMask = oMask & ~(1 << ui);
                    c.transform.position = cam; c.transform.rotation = Quaternion.LookRotation(look - cam, Vector3.up); c.fieldOfView = fov;
                    set = true;
                    Log($"screenshot through ReShade: {tag} ({(bridge ? "from the bridge" : "observer at the ship")}) - {p.Target.name}");
                }
            }
            catch (Exception e) { Log("screenshot (ReShade): " + e.Message); }
            if (set)
            {
                for (int i = 0; i < 3; i++) yield return null;
                try { keybd_event(0x2C, 0, 0, UIntPtr.Zero); keybd_event(0x2C, 0, 2, UIntPtr.Zero); } catch (Exception e) { Log("screenshot key: " + e.Message); }
                for (int i = 0; i < 8; i++) yield return null;
            }
            try
            {
                if (set)
                {
                    foreach (var x in canv) try { if (x != null) x.enabled = true; } catch { }
                    c.transform.position = oPos; c.transform.rotation = oRot; c.fieldOfView = oFov; c.cullingMask = oMask;
                    Time.timeScale = oScale;
                }
                if (cc != null) cc.enabled = ccWas;
            }
            catch { }
            Hiding = false; busy = false;
        }
        static Type convT; static System.Reflection.MethodInfo encode;
        static bool View(Plane p, string tag)
        {
            RenderTexture rt = null; Texture2D tex = null; Camera c = null;
            Vector3 oPos = Vector3.zero; Quaternion oRot = Quaternion.identity; float oFov = 60f; int oMask = -1; RenderTexture oTarget = null; bool moved = false;
            try
            {
                var st = p.Target.transform; var tp = st.position; tp.y = 0f; var pp = p.Pos;
                var toPlane = pp - tp; toPlane.y = 0f; if (toPlane.sqrMagnitude < 1f) toPlane = st.right; toPlane.Normalize();
                var f = st.forward; f.y = 0f; if (f.sqrMagnitude < 0.01f) f = Vector3.forward; f.Normalize();
                Vector3 cam, look; float fov;
                bool bridge = (shots % 2) == 0 && !tag.EndsWith("shotdown");
                if (bridge)
                {   // on the bridge: a little abaft the bow, about 30 m up, looking out at the aircraft over the ship's side
                    cam = tp + f * 25f + Vector3.up * 32f - toPlane * 8f;
                    look = new Vector3(pp.x, Mathf.Max(20f, pp.y * 0.6f), pp.z);
                    fov = 55f;
                }
                else
                {   // observer on the water: behind the ship on the side away from the aircraft, the ship in the foreground
                    cam = tp - toPlane * 650f - f * 250f + Vector3.up * 70f;
                    look = Vector3.Lerp(tp, new Vector3(pp.x, 0f, pp.z), 0.35f) + Vector3.up * 40f;
                    fov = 50f;
                }
                c = Camera.main; if (c == null) return false;
                oPos = c.transform.position; oRot = c.transform.rotation; oFov = c.fieldOfView; oMask = c.cullingMask; oTarget = c.targetTexture; moved = true;
                int ui = LayerMask.NameToLayer("UI");
                c.transform.position = cam; c.transform.rotation = Quaternion.LookRotation(look - cam, Vector3.up); c.fieldOfView = fov;
                if (ui >= 0) c.cullingMask = oMask & ~(1 << ui);
                int w = 1920, h = 1080;
                rt = new RenderTexture(w, h, 24); c.targetTexture = rt; c.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                if (encode == null)
                {
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { convT = a.GetType("UnityEngine.ImageConversion"); if (convT != null) break; }
                    if (convT != null) encode = convT.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                }
                if (encode == null) { Log("screenshot: UnityEngine.ImageConversion.EncodeToPNG not found"); return false; }
                var res = encode.Invoke(null, new object[] { tex }) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>;
                if (res == null) return false;
                var bytes = new byte[res.Length]; for (int i = 0; i < bytes.Length; i++) bytes[i] = res[i];
                var dirOut = System.IO.Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "screens"); System.IO.Directory.CreateDirectory(dirOut);
                var file = System.IO.Path.Combine(dirOut, $"{DateTime.Now:yyyyMMdd_HHmmss}_{shots:00}_airstrike_{tag}_{(bridge ? "bridge" : "observer")}.png");
                System.IO.File.WriteAllBytes(file, bytes);
                Log("screenshot " + file);
                return true;
            }
            catch (Exception e) { if (errors++ < 10) Log("screenshot view: " + e.Message); return false; }
            finally
            {
                if (moved && c != null) { try { c.targetTexture = oTarget; c.transform.position = oPos; c.transform.rotation = oRot; c.fieldOfView = oFov; c.cullingMask = oMask; } catch { } }
                try { if (tex != null) UnityEngine.Object.Destroy(tex); } catch { }
                try { if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); } } catch { }
            }
        }

        static void Drop(Plane p)
        {
            if (p.Dropped) return; p.Dropped = true;
            try
            {
                if (lastTube == null) return;
                var t = p.Target.transform; var tp = t.position;
                float tv = TorpKn.Value * 0.5144f;
                float speedArg = TorpedoSettings.World(tv);
                // lead the target (world units): the intercept of a torpedo at speedArg with the target's measured velocity
                var start = p.Pos; start.y = 0f; var f = t.forward; f.y = 0f; f.Normalize();
                tp.y = 0f; float dist = Flat(tp - start);
                var V = Vel(p.Target); var P = tp - start;
                float a = V.sqrMagnitude - speedArg * speedArg, b = 2f * Vector3.Dot(P, V), c = P.sqrMagnitude, T = dist / Mathf.Max(1f, speedArg);
                float disc = b * b - 4f * a * c;
                if (Mathf.Abs(a) > 1e-3f && disc >= 0f) { float sq = Mathf.Sqrt(disc), t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a); float tt = t1 > 0f && (t1 < t2 || t2 <= 0f) ? t1 : t2; if (tt > 0f) T = tt; }
                var aim = tp + V * T; aim.y = 0f;
                var dir = (aim - start); dir.y = 0f; dir.Normalize();
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
                Shot($"wave{p.Wave}_shotdown", 1.2f, p);
                if (waveStats[p.Wave][2] <= 4) Log($"wave {p.Wave}: aircraft shot down {(splash ? $"by the splash barrage of the heavy guns ({heavy} gun(s) firing into the sea ahead of it)" : $"by light anti-aircraft guns ({guns} gun(s) of 2-3 in in range)")}{(p.Dropped ? ", after its drop" : ", before its drop")}");
            }
        }

        static void Summary()
        {
            foreach (var kv in waveStats) if (!reported.Contains(kv.Key)) Log($"wave {kv.Key} result: {kv.Value[0]} aircraft, {kv.Value[1]} torpedoes dropped, {kv.Value[2]} shot down ({kv.Value[3]} by the splash barrage of heavy guns)");
        }
    }
}
