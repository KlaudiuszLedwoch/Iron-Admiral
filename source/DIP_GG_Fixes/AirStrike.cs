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
        internal static MelonPreferences_Entry<string> Model;
        internal static MelonPreferences_Entry<bool> Carrier; internal static MelonPreferences_Entry<int> CarrierFrom, CarrierTo;
        internal static MelonPreferences_Entry<int> Waves, PerWave;
        internal static MelonPreferences_Entry<float> FirstAfter, Interval, SpeedMs, AaRange, AaPerGun, SplashRange, SplashPerGun, TorpRunM, TorpKn, Scale, Spacing, Turb;
        internal static MelonPreferences_Entry<bool> BarrageFx;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AirStrikeTest", false, null, "Battle (test): torpedo aircraft of the AI side come in waves from beyond the horizon in daylight and attack the largest ship of the player's side from both beams; light guns of 2-3 in on the defending ships can shoot them down, heavier guns fire splash barrages at aircraft running in low (prototype of carrier and shore-based air strikes, #85)");
            Screens = cat.CreateEntry("AirStrikeScreenshots", false, null, "Battle (test): a series of screenshots of every air strike - the run-in, each torpedo drop and each aircraft shot down (UserData\\IronAdmiral\\screens)");
            Carrier = cat.CreateEntry("CustomBattleCarrier", true, null, "Battle (beta): in a custom battle of the aircraft model's years (CustomBattleCarrierFrom-To) the player has a carrier beyond the horizon - its torpedo aircraft attack the enemy's largest ship in waves by day");
            CarrierFrom = cat.CreateEntry("CustomBattleCarrierFrom", 1915, null, "Battle (beta): first year of custom battles with our carrier (Short 184 seaplanes flew from the seaplane carrier HMS Ben-my-Chree from 1915)");
            CarrierTo = cat.CreateEntry("CustomBattleCarrierTo", 1925, null, "Battle (beta): last year of custom battles with our carrier");
            Model = cat.CreateEntry("AirStrikeModel", "short_184", null, "Battle (test): aircraft model from Mods\\IronAdmiral_Models (file name without .iam); empty or missing = aircraft drawn from simple solids");
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
            Scale = cat.CreateEntry("AirStrikeAircraftScale", 1f, null, "Battle (test): drawing scale of the aircraft (1 = true size, about 14 m span)");
            Spacing = cat.CreateEntry("AirStrikeSpacing", 250f, null, "Battle (test): distance (m) between the aircraft of one section in the formation and between their drop points along the target");
            Turb = cat.CreateEntry("AirStrikeTurbulence", 1f, null, "Battle (test): strength of the aircraft's natural motion - bumps, drift, wobble and banking into turns (0 = rigid flight)");
            BarrageFx = cat.CreateEntry("AirStrikeBarrage", true, null, "Battle (test): show the defending fleet's barrage - tracers and shell bursts of the light guns, splash columns of the heavy guns ahead of aircraft running in low");
        }

        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirStrike] " + m); }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }

        enum Phase { Approach, Run, Away, Down }
        class Plane
        {
            public GameObject Go, Torp; public Transform Prop; public Phase P; public Vector3 Pos, Dir; public Ship Target; public float Side; public bool Dropped;
            public float Fall; public int Wave; public float RockFrom = -1f; public HashSet<IntPtr> Greeted = new HashSet<IntPtr>();
            public float SlotOff, Seed, Roll;   // 1.11.391: own place in the attack (along the target), turbulence seed, bank angle
            public AirModels.Ctl Ctl; public float RollVis, Ail, Elev, Rud;   // 1.11.393: moving control surfaces of the model
        }
        static readonly List<Plane> planes = new List<Plane>();
        static IntPtr battle; static float battleT; static int wavesSent; static float nextWave;
        static readonly Dictionary<int, int[]> waveStats = new Dictionary<int, int[]>(); // wave -> sent, dropped, lost, of them to the splash barrage
        static readonly Dictionary<IntPtr, (int light, int heavy)> aaGuns = new Dictionary<IntPtr, (int light, int heavy)>();
        static int errors, drops; static readonly HashSet<int> reported = new HashSet<int>();

        static void Clear()
        {
            foreach (var p in planes) { try { if (p.Go != null) UnityEngine.Object.Destroy(p.Go); } catch { } }
            FxClear(); planes.Clear(); waveStats.Clear(); reported.Clear(); track.Clear(); shots = 0; aerial.Clear(); aerialPtr.Clear(); follow = Follow.None; PauseCam(false); radio.Clear(); said.Clear(); hitSaid.Clear(); aaGuns.Clear(); wavesSent = 0; battleT = 0f; nextWave = 0f;
        }

        internal static void Tick()
        {
            bool test = On != null && On.Value, car = Carrier != null && Carrier.Value;
            if (!test && !car) { if (planes.Count > 0) Clear(); return; }
            try
            {
                if (!GameManager.IsBattle) { if (planes.Count > 0 || battle != IntPtr.Zero) { Summary(); Clear(); battle = IntPtr.Zero; } return; }
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
                if (b == null || b.Ships == null || b.Ships.Count == 0) return;
                if (b.Pointer != battle)
                {
                    Clear(); battle = b.Pointer; nextWave = FirstAfter.Value; lastTube = null;
                    // 1.11.384 (author 29.10 16:43-16:44: "add our carrier to the custom battle in the beta - at a date that fits the aircraft
                    // model"; he picks his own designs of the period in the custom battle): in a custom battle of CustomBattleCarrierFrom..To
                    // (the Sopwith Camel years: HMS Furious flew 2F.1 Camels against Tondern in July 1918) the player has a carrier beyond the
                    // horizon - its waves attack the enemy; otherwise the test switch sends the AI side's aircraft at the player
                    int yr = 0; try { for (int i = 0; i < b.Ships.Count; i++) { var s = b.Ships[i]; if (s != null && Player(s)) { int y = (int)s.GetYear(s); if (y > yr) yr = y; } } } catch { }
                    bool custom = false; try { custom = GameManager.IsCustomBattle; } catch { }
                    atkIsPlayer = car && custom && yr >= CarrierFrom.Value && yr <= CarrierTo.Value;
                    active = atkIsPlayer || test;
                    if (atkIsPlayer) Log($"custom battle {yr}: our carrier ({CarrierName()}, {TypeName()}) is beyond the horizon - its waves attack the enemy's largest ship");
                    else if (car && custom) Log($"custom battle {yr}: no carrier - our carrier comes only in custom battles of {CarrierFrom.Value}-{CarrierTo.Value} (the aircraft model's years)");
                }
                if (!active) return;
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
                Move(dt, b.Ships); FxTick();
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
        static bool atkIsPlayer, active;
        static bool Defender(Ship s) { return atkIsPlayer ? !Player(s) : Player(s); }

        static void Launch(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            // target: the largest ship of the player's side; torpedo data: a tube of a ship of the attacking side
            Ship target = null; float best = -1f; Part tube = null; Vector3 aiC = Vector3.zero; int aiN = 0;
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (!Alive(s)) continue;
                if (Defender(s)) { float t = 0f; try { t = s.Tonnage(); } catch { } if (t > best) { best = t; target = s; } }
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
            // 1.11.389 (author 29.10 17:20: "let them appear from behind - flying over their own ships, rocking their wings"): the waves
            // start 8 km behind the attacking fleet's farthest ship, seen from the target, so they always come over their own ships
            float startDist = 14000f;
            for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (!Alive(s) || Defender(s)) continue; var d = s.transform.position - tp; d.y = 0f; float along = Vector3.Dot(d, from); if (along + 8000f > startDist) startDist = along + 8000f; }
            int n = Math.Max(1, PerWave.Value);
            waveStats[wave] = new int[4]; waveStats[wave][0] = n;
            for (int k = 0; k < n; k++)
            {
                float side = (k % 2 == 0) ? 1f : -1f;                       // half on each beam
                var right = Vector3.Cross(Vector3.up, from);
                // 1.11.391 (author 29.10 17:57: "they are too close together"): each aircraft keeps its own place - sections spaced
                // AirStrikeSpacing apart, staggered back and up, and each drops at its own point along the target (a fan of torpedoes)
                int slot = k / 2, perSide = (n + (side > 0 ? 1 : 0)) / 2; float sp = Spacing.Value;
                var start = tp + from * (startDist + slot * sp * 0.5f) + right * side * (800f + slot * sp) + Vector3.up * (300f + slot * 25f);
                int yr = 1925; try { yr = (int)target.GetYear(target); } catch { }
                var go = Build(yr);
                var pl = new Plane { Go = go, Ctl = lastCtl, Prop = lastProp, Torp = lastTorp, P = Phase.Approach, Pos = start, Target = target, Side = side, Wave = wave,
                    SlotOff = (slot - (perSide - 1) * 0.5f) * sp, Seed = UnityEngine.Random.value * 1000f };
                planes.Add(pl);
            }
            try { Dispatch.Air(atkIsPlayer ? $"{CarrierName()}: {Flight(wave)} - {n} x {TypeName()} with torpedoes flown off, target {Dispatch.Ship(target.name)}." : $"Lookouts: {n} enemy aircraft low on the horizon, heading for {Dispatch.Ship(target.name)}."); } catch { }
            Log($"wave {wave}: start {startDist / 1000f:0.0} km from the target, behind the attacking fleet");
            Log($"wave {wave}: {n} {(atkIsPlayer ? "of our" : "enemy")} torpedo aircraft from beyond the horizon against {target.name} ({best:0} t), attacking from both beams; torpedo data from {tube.ship?.name}");
        }
        static Part lastTube; static int tubeLogged;
        static Part FindTube(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            for (int i = 0; ships != null && i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null || Defender(s)) continue;
                try { var ts = s.torpedoesAll; if (ts != null && ts.Count > 0 && ts[0] != null) return ts[0]; } catch { }
            }
            // 1.11.388 (author's custom battle 29.10 17:13: none of his ships had torpedo tubes - "wave not sent"): the aircraft come from
            // the carrier, not from those ships - the torpedo data then come from any tube in the battle (the warhead is scaled anyway)
            for (int i = 0; ships != null && i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null) continue;
                try { var ts = s.torpedoesAll; if (ts != null && ts.Count > 0 && ts[0] != null) { if (tubeLogged++ < 3) Log($"no torpedo tube on the attacking side - torpedo data from {s.name}"); return ts[0]; } } catch { }
            }
            return null;
        }

        // 1.11.381 (author 29.10 15:53-15:58: "slow biplanes at first", photos of the Sopwith Cuckoo dropping a torpedo and of the
        // Fairey Swordfish, a three-view drawing of the Cuckoo; "can't you build the model yourself?"): the aircraft are built at run
        // time from simple solids after the drawings - tapered fabric fuselage, two staggered wings with interplane and cabane struts
        // and bracing wires, split-axle undercarriage, tailplane, fin and rudder, a turning propeller, RAF roundels and the torpedo
        // slung under the fuselage, which disappears at the drop.
        //  - up to 1929: Sopwith T.1 Cuckoo type - inline engine, olive-brown (PC10) upper surfaces over doped-linen undersides,
        //    span about 14.2 m, length about 8.7 m (dimensions from memory, not from a source);
        //  - from 1930: Fairey Swordfish type - radial engine with a ring cowling, aluminium dope, three open cockpits, span about
        //    13.9 m, length about 10.9 m (dimensions from memory, not from a source).
        static Transform lastProp; static GameObject lastTorp; static AirModels.Ctl lastCtl;
        static GameObject Build(int year)
        {
            lastCtl = null;
            // 1.11.383: a model from Mods\IronAdmiral_Models when there is one (AirStrikeModel), with the torpedo slung under it
            var model = Model != null ? Model.Value : "";
            if (!string.IsNullOrEmpty(model))
            {
                var mg = AirModels.Spawn(model, Scale.Value, out var ctl);
                if (mg != null)
                {
                    lastCtl = ctl;
                    var mt = new GameObject("torpedo"); mt.transform.SetParent(mg.transform, false);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    try { UnityEngine.Object.Destroy(body.GetComponent<Collider>()); } catch { }
                    try { body.GetComponent<Renderer>().material.color = new Color(0.20f, 0.21f, 0.22f); } catch { }
                    body.transform.SetParent(mt.transform, false); body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    body.transform.localScale = new Vector3(0.45f, 2.3f, 0.45f) * Scale.Value; body.transform.localPosition = new Vector3(0f, -1.0f, 0f) * Scale.Value;
                    lastProp = ctl != null ? ctl.Prop : null; lastTorp = mt;
                    return mg;
                }
            }
            var root = new GameObject("IA_AirStrike_Plane");
            float s = Scale.Value;
            bool late = year >= 1930;
            Color skin = late ? new Color(0.72f, 0.74f, 0.74f) : new Color(0.33f, 0.30f, 0.19f);
            Color under = late ? new Color(0.66f, 0.68f, 0.68f) : new Color(0.80f, 0.75f, 0.60f);
            Color metal = late ? new Color(0.58f, 0.60f, 0.61f) : new Color(0.25f, 0.25f, 0.24f);
            Color dark = new Color(0.08f, 0.08f, 0.08f), strut = late ? new Color(0.55f, 0.56f, 0.56f) : new Color(0.42f, 0.33f, 0.20f);
            Color red = new Color(0.65f, 0.08f, 0.07f), white = new Color(0.92f, 0.92f, 0.90f), blue = new Color(0.10f, 0.16f, 0.42f);
            Color torpCol = new Color(0.20f, 0.21f, 0.22f);
            GameObject Prim(PrimitiveType t, Color col, Transform parent)
            {
                var g = GameObject.CreatePrimitive(t);
                try { var c = g.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); } catch { }
                try { var m = g.GetComponent<Renderer>().material; m.color = col; try { m.SetFloat("_Glossiness", 0.15f); } catch { } } catch { }
                g.transform.SetParent(parent ?? root.transform, false); return g;
            }
            GameObject Box(Vector3 size, Vector3 at, Color col, Vector3 euler = default(Vector3), Transform parent = null)
            {
                var g = Prim(PrimitiveType.Cube, col, parent); g.transform.localScale = size * s; g.transform.localPosition = at * s; g.transform.localRotation = Quaternion.Euler(euler); return g;
            }
            // a cylinder along the aircraft's length (z), or across (x) / up (y)
            GameObject Cyl(float dia, float len, Vector3 at, Color col, char axis = 'z', Transform parent = null)
            {
                var g = Prim(PrimitiveType.Cylinder, col, parent); g.transform.localScale = new Vector3(dia, len * 0.5f, dia) * s; g.transform.localPosition = at * s;
                g.transform.localRotation = axis == 'z' ? Quaternion.Euler(90f, 0f, 0f) : axis == 'x' ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity; return g;
            }
            // a strut or wire between two points
            void Rod(Vector3 a, Vector3 b, float th, Color col)
            {
                var g = Prim(PrimitiveType.Cube, col, null); var d = b - a;
                g.transform.localPosition = (a + b) * 0.5f * s; g.transform.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                g.transform.localScale = new Vector3(th, th, d.magnitude) * s;
            }
            // RAF roundel on a surface: three flat discs; 'up' = facing up (wing), otherwise facing sideways (fuselage, side sign)
            void Roundel(Vector3 at, float dia, bool up, float side = 1f)
            {
                Color[] cols = { blue, white, red }; float[] f = { 1f, 0.66f, 0.33f };
                for (int i = 0; i < 3; i++)
                {
                    var g = Prim(PrimitiveType.Cylinder, cols[i], null);
                    float t = 0.01f + i * 0.004f;
                    g.transform.localScale = new Vector3(dia * f[i], t, dia * f[i]) * s;
                    g.transform.localPosition = (at + (up ? Vector3.up * (t * 1.0f) : Vector3.right * side * (t * 1.0f))) * s;
                    g.transform.localRotation = up ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
                }
            }

            float L = late ? 10.9f : 8.7f, span = late ? 13.9f : 14.2f, chord = late ? 1.75f : 1.85f, gap = late ? 1.95f : 1.85f;
            float zNose = L * 0.5f, zTail = -L * 0.5f;
            // fuselage: tapered from the engine bay to the sternpost, in segments
            int seg = 6;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg, b = (i + 1) / (float)seg;
                float w = Mathf.Lerp(1.05f, 0.22f, (a + b) * 0.5f * (a + b) * 0.5f + 0.15f * (a + b));
                float h = Mathf.Lerp(1.30f, 0.45f, (a + b) * 0.5f);
                float z0 = Mathf.Lerp(zNose - 1.2f, zTail, a), z1 = Mathf.Lerp(zNose - 1.2f, zTail, b);
                float y = Mathf.Lerp(0f, 0.25f, (a + b) * 0.5f);
                Box(new Vector3(w, h, Mathf.Abs(z1 - z0) + 0.05f), new Vector3(0f, y, (z0 + z1) * 0.5f), skin);
                Box(new Vector3(w * 0.98f, 0.04f, Mathf.Abs(z1 - z0) + 0.05f), new Vector3(0f, y - h * 0.5f, (z0 + z1) * 0.5f), under);
            }
            // engine and propeller
            Transform prop;
            if (late)
            {
                Cyl(1.45f, 0.75f, new Vector3(0f, 0.05f, zNose - 0.75f), metal);                 // Pegasus radial
                Cyl(1.55f, 0.30f, new Vector3(0f, 0.05f, zNose - 0.30f), skin);                  // Townend ring
                Cyl(0.55f, 0.35f, new Vector3(0f, 0.05f, zNose - 0.05f), dark);                  // crankcase / hub
                prop = new GameObject("prop").transform; prop.SetParent(root.transform, false); prop.localPosition = new Vector3(0f, 0.05f, zNose + 0.15f) * s;
                for (int k = 0; k < 3; k++) { var bl = Box(new Vector3(0.20f, 1.65f, 0.06f), new Vector3(0f, 0.82f, 0f), dark, default(Vector3), prop); var hb = new GameObject("b"); hb.transform.SetParent(prop, false); hb.transform.localRotation = Quaternion.Euler(0f, 0f, k * 120f); bl.transform.SetParent(hb.transform, false); }
                // three open cockpits with the crew's heads
                for (int k = 0; k < 3; k++) { Box(new Vector3(0.62f, 0.08f, 0.9f), new Vector3(0f, 0.68f, 0.9f - k * 1.15f), dark); Cyl(0.24f, 0.24f, new Vector3(0f, 0.86f, 0.9f - k * 1.15f), new Color(0.25f, 0.2f, 0.15f), 'y'); }
            }
            else
            {
                Box(new Vector3(0.95f, 1.05f, 1.35f), new Vector3(0f, 0.05f, zNose - 0.65f), metal);   // Sunbeam Arab under its cowling
                Box(new Vector3(0.75f, 0.85f, 0.25f), new Vector3(0f, -0.05f, zNose + 0.05f), dark);  // radiator
                Box(new Vector3(0.08f, 0.55f, 0.08f), new Vector3(0.42f, 0.65f, zNose - 0.9f), dark); Box(new Vector3(0.08f, 0.55f, 0.08f), new Vector3(-0.42f, 0.65f, zNose - 0.9f), dark); // exhausts
                prop = new GameObject("prop").transform; prop.SetParent(root.transform, false); prop.localPosition = new Vector3(0f, 0.05f, zNose + 0.22f) * s;
                Box(new Vector3(0.22f, 2.9f, 0.08f), Vector3.zero, new Color(0.35f, 0.22f, 0.10f), default(Vector3), prop);   // two-blade wooden propeller
                Box(new Vector3(0.62f, 0.08f, 0.85f), new Vector3(0f, 0.68f, -0.6f), dark);                                       // cockpit
                Cyl(0.24f, 0.24f, new Vector3(0f, 0.86f, -0.6f), new Color(0.3f, 0.22f, 0.12f), 'y');                              // pilot
            }
            // wings: the lower at the bottom of the fuselage, the upper one gap above, staggered forward
            float zW = late ? 1.6f : 1.05f, yLow = -0.55f, yUp = yLow + gap, stagger = late ? 0.25f : 0.15f;
            Box(new Vector3(span, 0.12f, chord), new Vector3(0f, yUp, zW + stagger), skin);
            Box(new Vector3(span, 0.02f, chord * 0.98f), new Vector3(0f, yUp - 0.07f, zW + stagger), under);
            Box(new Vector3(span * 0.97f, 0.11f, chord), new Vector3(0f, yLow, zW), skin);
            Box(new Vector3(span * 0.97f, 0.02f, chord * 0.98f), new Vector3(0f, yLow - 0.065f, zW), under);
            Roundel(new Vector3(span * 0.36f, yUp + 0.06f, zW + stagger), 1.25f, true); Roundel(new Vector3(-span * 0.36f, yUp + 0.06f, zW + stagger), 1.25f, true);
            // interplane struts (two bays a side on the Cuckoo, one and a half on the Swordfish) and the cabane
            float[] bays = late ? new[] { 0.30f, 0.46f } : new[] { 0.22f, 0.44f };
            foreach (var sd in new[] { 1f, -1f })
                foreach (var fb in bays)
                {
                    float x = sd * span * fb;
                    Rod(new Vector3(x, yLow, zW + chord * 0.32f), new Vector3(x, yUp, zW + stagger + chord * 0.32f), 0.09f, strut);
                    Rod(new Vector3(x, yLow, zW - chord * 0.32f), new Vector3(x, yUp, zW + stagger - chord * 0.32f), 0.09f, strut);
                    // bracing wires (crossed)
                    Rod(new Vector3(x, yLow, zW), new Vector3(x - sd * span * 0.2f, yUp, zW + stagger), 0.025f, dark);
                    Rod(new Vector3(x, yUp, zW + stagger), new Vector3(x - sd * span * 0.2f, yLow, zW), 0.025f, dark);
                }
            foreach (var sd in new[] { 0.45f, -0.45f }) { Rod(new Vector3(sd, 0.55f, zW + 0.6f), new Vector3(sd * 1.4f, yUp, zW + stagger + 0.6f), 0.08f, strut); Rod(new Vector3(sd, 0.55f, zW - 0.5f), new Vector3(sd * 1.4f, yUp, zW + stagger - 0.5f), 0.08f, strut); }
            // tail: tailplane, fin and rudder (with the rudder stripes on the Cuckoo)
            Box(new Vector3(late ? 4.6f : 4.3f, 0.08f, 1.35f), new Vector3(0f, 0.35f, zTail + 0.7f), skin);
            Box(new Vector3(0.08f, 1.25f, 1.0f), new Vector3(0f, 1.0f, zTail + 0.55f), skin);
            if (!late) { Box(new Vector3(0.09f, 1.15f, 0.22f), new Vector3(0f, 0.95f, zTail - 0.02f), blue); Box(new Vector3(0.09f, 1.15f, 0.22f), new Vector3(0f, 0.95f, zTail - 0.24f), white); Box(new Vector3(0.09f, 1.15f, 0.22f), new Vector3(0f, 0.95f, zTail - 0.46f), red); }
            else Box(new Vector3(0.09f, 1.2f, 0.65f), new Vector3(0f, 0.95f, zTail - 0.2f), skin);
            Rod(new Vector3(0f, 0.15f, zTail + 0.3f), new Vector3(0f, -0.25f, zTail + 0.1f), 0.07f, dark);            // tail skid
            // fuselage roundels
            Roundel(new Vector3(0.36f, 0.35f, zTail + L * 0.33f), 0.8f, false, 1f); Roundel(new Vector3(-0.36f, 0.35f, zTail + L * 0.33f), 0.8f, false, -1f);
            // split-axle undercarriage (clear of the torpedo) and wheels
            foreach (var sd in new[] { 1f, -1f })
            {
                var wh = new Vector3(sd * 1.0f, -1.55f, zW + 0.55f);
                Rod(new Vector3(sd * 0.4f, -0.6f, zW + 1.1f), wh, 0.08f, strut); Rod(new Vector3(sd * 0.4f, -0.6f, zW - 0.2f), wh, 0.08f, strut); Rod(new Vector3(sd * 0.15f, -0.65f, zW + 0.55f), wh, 0.06f, strut);
                Cyl(0.85f, 0.16f, wh, dark, 'x');
            }
            // the torpedo, slung under the fuselage between the wheels
            var torp = new GameObject("torpedo"); torp.transform.SetParent(root.transform, false);
            Cyl(0.45f, late ? 4.9f : 4.6f, new Vector3(0f, -1.0f, zW - 0.6f), torpCol, 'z', torp.transform);
            Cyl(0.30f, 0.45f, new Vector3(0f, -1.0f, zW - 0.6f + (late ? 2.6f : 2.45f)), late ? new Color(0.70f, 0.10f, 0.08f) : torpCol, 'z', torp.transform);   // warhead (red exercise head on the Swordfish photo)
            Box(new Vector3(0.7f, 0.05f, 0.25f), new Vector3(0f, -1.0f, zW - 0.6f - (late ? 2.45f : 2.3f)), torpCol, default(Vector3), torp.transform);
            Box(new Vector3(0.05f, 0.7f, 0.25f), new Vector3(0f, -1.0f, zW - 0.6f - (late ? 2.45f : 2.3f)), torpCol, default(Vector3), torp.transform);
            lastProp = prop; lastTorp = torp;
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
            var rp = future + dir * 1000f + f * p.SlotOff; rp.y = 30f; return rp;
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
                    if (p.P == Phase.Approach) { goal = RunPoint(p); goal.y = 300f; if (Flat(goal - p.Pos) < 3000f) { p.P = Phase.Run; Shot($"wave{p.Wave}_runin", 1.5f, p); Radio(p.Wave, "runin", atkIsPlayer ? Pick($"Tally-ho! {Dispatch.Ship(p.Target.name)} dead ahead - split and go down to the deck!", $"There she is - {Dispatch.Ship(p.Target.name)}. Port section left, starboard section right. Down we go!", $"Target in sight. Going down - take her from both beams, chaps!") : Pick($"Enemy aircraft low on the water - coming in on {Dispatch.Ship(p.Target.name)}!", $"Torpedo planes! Both beams!")); } }
                    else if (p.P == Phase.Run)
                    {
                        goal = RunPoint(p);
                        Shot($"wave{p.Wave}_run", 3f, p);
                        if (Flat(goal - p.Pos) < 300f) { Drop(p); p.P = Phase.Away; Shot($"wave{p.Wave}_drop", 1.2f, p); Radio(p.Wave, "drop", atkIsPlayer ? Pick("Fish away! Pulling up - hard right!", "Torpedo gone! Running true... get out of here!", "Released! Climbing away - watch the flak!") : "They have dropped - torpedoes in the water!"); }
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
                    var oldDir = p.Dir;
                    p.Dir = p.Dir.sqrMagnitude < 0.01f ? nd : Vector3.Slerp(p.Dir, nd, Mathf.Clamp01(dt * 1.5f)).normalized;
                    p.Pos += p.Dir * v * dt;
                    Pose(p, oldDir, dt);
                    // wing rocking over our own ships - the airmen's greeting to the sailors below: a few rolls of about 25 deg
                    if (p.P == Phase.Approach)
                        for (int si = 0; si < ships.Count; si++)
                        {
                            var s = ships[si]; if (!Alive(s) || Defender(s) || p.Greeted.Contains(s.Pointer)) continue;
                            if (Flat(s.transform.position - p.Pos) < 450f) { p.Greeted.Add(s.Pointer); if (p.RockFrom < 0f || Time.time - p.RockFrom > 3.5f) p.RockFrom = Time.time; Radio(p.Wave, "greet|" + Dispatch.Ship(s.name), atkIsPlayer ? Pick($"Waggle your wings for the lads on {Dispatch.Ship(s.name)}!", $"There's {Dispatch.Ship(s.name)} - give them a wave, chaps!", $"{Dispatch.Ship(s.name)} below - they're cheering us on!") : "Enemy aircraft passing over their own fleet.", false); }
                        }
                    try { if (p.Prop != null) p.Prop.Rotate(0f, 0f, 1800f * dt, Space.Self); } catch { }
                    if (p.P != Phase.Away || p.Pos.y < 200f) Aa(p, dt, ships);
                }
                catch (Exception e) { if (errors++ < 10) Log("move: " + e.Message); try { UnityEngine.Object.Destroy(p.Go); } catch { } planes.RemoveAt(i); }
            }
        }
        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        // 1.11.391 (author 29.10 17:57: "the flight looks plastic - no buffeting, no drift, the controls don't work"): the drawn
        // aircraft is no longer nailed to its path. It banks into every turn (roll follows the rate of turn, smoothed), its nose
        // follows the climb or dive, and slow noise adds the bumps of gusty air - rising and sinking a few metres, drifting sideways,
        // small rolls, pitch and yaw. Low over the sea (the torpedo run at 30 m) the bumps are smaller and quicker. The path itself
        // (aim, drop, anti-aircraft) is unchanged; AirStrikeTurbulence scales the motion, 0 = rigid as before.
        static void Pose(Plane p, Vector3 oldDir, float dt)
        {
            float k = Turb != null ? Mathf.Max(0f, Turb.Value) : 1f;
            Vector3 fa = new Vector3(oldDir.x, 0f, oldDir.z), fb = new Vector3(p.Dir.x, 0f, p.Dir.z);
            float rate = (fa.sqrMagnitude > 0.001f && fb.sqrMagnitude > 0.001f && dt > 0f) ? Vector3.SignedAngle(fa, fb, Vector3.up) / dt : 0f;
            float bank = Mathf.Clamp(-rate * 1.6f, -55f, 55f) * Mathf.Min(1f, k);
            p.Roll = Mathf.Lerp(p.Roll, bank, Mathf.Clamp01(dt * 2.5f));
            float t = Time.time, s = p.Seed; bool low = p.Pos.y < 80f;
            float amp = (low ? 0.5f : 1f) * k, fq = low ? 1.6f : 1f;
            float N(float o, float f) { return (Mathf.PerlinNoise(s + o, t * f * fq) - 0.5f) * 2f; }
            var right = Vector3.Cross(Vector3.up, fb.sqrMagnitude > 0.001f ? fb.normalized : Vector3.forward);
            var pos = p.Pos + Vector3.up * (N(0f, 0.55f) * 3.5f + N(7f, 2.1f) * 0.8f) * amp + right * N(13f, 0.35f) * 5f * amp;
            p.Go.transform.position = pos;
            float pitch = N(21f, 0.9f) * 3f * amp, yaw = N(29f, 0.6f) * 2.5f * amp, roll = (N(37f, 0.8f) * 6f + N(43f, 3.0f) * 1.5f) * amp;
            float rock = 0f;
            if (p.RockFrom >= 0f) { float a = Time.time - p.RockFrom; if (a < 3.2f) rock = 25f * Mathf.Sin(a * 6f) * (1f - a / 3.2f * 0.4f); }
            float rollVis = p.Roll + roll + rock;
            p.Go.transform.rotation = Quaternion.LookRotation(p.Dir, Vector3.up) * Quaternion.Euler(pitch, yaw, rollVis);
            // 1.11.393 (author 29.10 18:15: "with ailerons, best"): the model's moving parts follow the motion - the ailerons with the
            // rate of roll (bank, gusts, the wing rocking), the elevator with the climb or dive and the pitch bumps, the rudder with
            // the rate of turn and the yaw bumps; each moves smoothly, at most about 20 degrees
            if (p.Ctl != null && p.Ctl.Surfaces.Count > 0 && dt > 0f)
            {
                float rr = (rollVis - p.RollVis) / dt; p.RollVis = rollVis;
                float sm = Mathf.Clamp01(dt * 6f);
                p.Ail = Mathf.Lerp(p.Ail, Mathf.Clamp(rr * 0.25f, -20f, 20f), sm);
                p.Elev = Mathf.Lerp(p.Elev, Mathf.Clamp(-p.Dir.y * 90f + pitch * 3f, -20f, 20f), sm);
                p.Rud = Mathf.Lerp(p.Rud, Mathf.Clamp(rate * 0.4f + yaw * 3f, -20f, 20f), sm);
                foreach (var (tr, axis, role) in p.Ctl.Surfaces)
                {
                    if (tr == null) continue;
                    float ang = role == 2 ? p.Ail : role == 3 ? -p.Ail : role == 4 ? p.Elev : p.Rud;
                    tr.localRotation = Quaternion.AngleAxis(ang, axis);
                }
            }
        }

        // 1.11.391 (author 29.10 17:57: "I didn't see a barrage of the whole fleet - the whole fleet should fire"): every defending
        // ship in range shows its fire at the aircraft - tracers of the light guns from the ship towards the aircraft, dark shell bursts
        // around them, and white splash columns that the heavy guns raise in the sea ahead of aircraft running in low. Only the
        // picture: the chance of bringing an aircraft down is the one of Aa(). Switch AirStrikeBarrage; at most 220 effects at once.
        class Fx { public GameObject G; public float T0, Life, Size; public int Kind; public Vector3 A, B; }
        static readonly List<Fx> fx = new List<Fx>();
        static GameObject FxPrim(PrimitiveType t, Color c, bool glow)
        {
            var g = GameObject.CreatePrimitive(t);
            try { var cl = g.GetComponent<Collider>(); if (cl != null) UnityEngine.Object.Destroy(cl); } catch { }
            try
            {
                var r = g.GetComponent<Renderer>(); var m = r.material; m.color = c; try { m.SetFloat("_Glossiness", 0.05f); } catch { }
                if (glow) { try { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 3f); } catch { } }
                try { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; } catch { }
            }
            catch { }
            return g;
        }
        static void Barrage(Plane p, Ship s, float d, (int light, int heavy) g, bool low, float dt)
        {
            if (BarrageFx == null || !BarrageFx.Value || fx.Count >= 220 || p.Go == null) return;
            var ap = p.Go.transform.position; float now = Time.time;
            var sp = s.transform.position + Vector3.up * 10f;
            if (g.light > 0)
            {
                float n = Mathf.Min(g.light, 8);
                if (UnityEngine.Random.value < n * 1.2f * dt)
                {   // tracer: a short glowing streak flying from the ship past the aircraft
                    var aim = ap + p.Dir * UnityEngine.Random.Range(-40f, 120f) + UnityEngine.Random.insideUnitSphere * 45f;
                    var go = FxPrim(PrimitiveType.Cube, new Color(1f, 0.55f, 0.15f), true);
                    go.transform.localScale = new Vector3(0.6f, 0.6f, 28f);
                    fx.Add(new Fx { G = go, T0 = now, Life = Mathf.Clamp((aim - sp).magnitude / 900f, 0.4f, 2.5f), Kind = 0, A = sp, B = aim });
                }
                if (UnityEngine.Random.value < n * 0.7f * dt)
                {   // shell burst around the aircraft
                    var at = ap + p.Dir * UnityEngine.Random.Range(-60f, 160f) + UnityEngine.Random.insideUnitSphere * 75f; if (at.y < 12f) at.y = 12f + UnityEngine.Random.value * 15f;
                    var go = FxPrim(PrimitiveType.Sphere, new Color(0.13f, 0.12f, 0.11f), false); go.transform.position = at; go.transform.localScale = Vector3.one * 2f;
                    fx.Add(new Fx { G = go, T0 = now, Life = 3.2f, Kind = 1, A = at, Size = UnityEngine.Random.Range(9f, 15f) });
                }
            }
            if (low && g.heavy > 0 && d <= SplashRange.Value && UnityEngine.Random.value < Mathf.Min(g.heavy, 6) * 0.35f * dt)
            {   // splash column of a heavy shell fired into the sea ahead of the aircraft
                var f = new Vector3(p.Dir.x, 0f, p.Dir.z).normalized; var r = Vector3.Cross(Vector3.up, f);
                var at = p.Pos + f * UnityEngine.Random.Range(80f, 650f) + r * UnityEngine.Random.Range(-90f, 90f); at.y = 0f;
                var go = FxPrim(PrimitiveType.Cylinder, new Color(0.90f, 0.93f, 0.95f), false);
                fx.Add(new Fx { G = go, T0 = now, Life = 4f, Kind = 2, A = at, Size = UnityEngine.Random.Range(28f, 48f) });
                go.transform.position = at; go.transform.localScale = new Vector3(7f, 0.01f, 7f);
            }
        }
        static void FxTick()
        {
            float now = Time.time;
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var e = fx[i];
                try
                {
                    float a = (now - e.T0) / e.Life;
                    if (e.G == null || a >= 1f || a < 0f) { if (e.G != null) UnityEngine.Object.Destroy(e.G); fx.RemoveAt(i); continue; }
                    var tr = e.G.transform;
                    if (e.Kind == 0) { var d = e.B - e.A; tr.position = e.A + d * a; tr.rotation = Quaternion.LookRotation(d.normalized, Vector3.up); }
                    else if (e.Kind == 1)
                    {
                        float sc = a < 0.12f ? Mathf.Lerp(2f, e.Size, a / 0.12f) : a < 0.6f ? e.Size * (1f + (a - 0.12f) * 0.4f) : e.Size * 1.2f * (1f - (a - 0.6f) / 0.4f);
                        tr.localScale = Vector3.one * Mathf.Max(0.01f, sc); tr.position = e.A + Vector3.up * (now - e.T0) * 1.2f;
                    }
                    else
                    {
                        float h = a < 0.2f ? e.Size * (a / 0.2f) : a < 0.45f ? e.Size : e.Size * (1f - (a - 0.45f) / 0.55f);
                        float w = 7f + a * 9f; h = Mathf.Max(0.01f, h);
                        tr.localScale = new Vector3(w, h * 0.5f, w); tr.position = e.A + Vector3.up * (h * 0.5f);
                    }
                }
                catch { try { UnityEngine.Object.Destroy(e.G); } catch { } fx.RemoveAt(i); }
            }
        }
        static void FxClear() { foreach (var e in fx) { try { if (e.G != null) UnityEngine.Object.Destroy(e.G); } catch { } } fx.Clear(); }

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
            if (p != null && p.Go != null && p.Target != null) { if (!busy) MelonCoroutines.Start(ScreenShot(p, tag)); return; }   // 1.11.382 (author 29.10 16:09: "do it without ReShade"): on screen, saved by the game (ScreenCapture)
            if (p == null || p.Go == null || p.Target == null || !View(p, tag)) AutoTest.Shot("airstrike_" + tag);
        }

        // 1.11.380 (author 29.10 15:52: "make them nice - high resolution and ReShade"): with ReShade installed in the game folder
        // the shot is taken on screen, through ReShade: the battle camera's controller is paused, the camera moved to the observer
        // (or the bridge), time frozen, the interface hidden (canvases and the mod's own panels), ReShade's screenshot key
        // (PrintScreen, ReShade.ini KeyScreenshot=44) pressed, then everything restored. ReShade saves the picture at the screen's
        // resolution in the game folder's screenshots folder (ReShade.ini SavePath).
        internal static bool Hiding; static bool busy, lastBridge, cam2Was; static Cam cam2;
        static Camera holdCam; static Vector3 holdPos; static Quaternion holdRot; static float holdFov;
        internal static void LateTick() { try { FollowTick(); } catch { } if (holdCam == null) return; try { holdCam.transform.position = holdPos; holdCam.transform.rotation = holdRot; holdCam.fieldOfView = holdFov; } catch { } }
        // the game's own screen capture at the screen's resolution (UserData\IronAdmiral\screens); it is written at the end of the frame
        static System.Reflection.MethodInfo capture;
        static void SaveScreen(string tag)
        {
            if (capture == null)
            {
                Type st = null; foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { st = a.GetType("UnityEngine.ScreenCapture"); if (st != null) break; }
                if (st != null) capture = st.GetMethod("CaptureScreenshot", new[] { typeof(string), typeof(int) });
            }
            if (capture == null) { Log("screenshot: UnityEngine.ScreenCapture not found"); return; }
            var dirOut = System.IO.Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "screens"); System.IO.Directory.CreateDirectory(dirOut);
            var file = System.IO.Path.Combine(dirOut, $"{DateTime.Now:yyyyMMdd_HHmmss}_{shots:00}_airstrike_{tag}.png");
            capture.Invoke(null, new object[] { file, 1 });
            Log($"screenshot {file} ({Screen.width}x{Screen.height})");
        }
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
                    Pose(p, tag, out var cam, out var look, out var fov, out var bridge); lastBridge = bridge;
                    try { cc = UnityEngine.Object.FindObjectOfType<CameraController>(); if (cc != null) { ccWas = cc.enabled; cc.enabled = false; } } catch { }
                    // 1.11.383 (shots 16:27: the battle camera Cam kept its own view): pause Cam too, and hold the pose every LateUpdate
                    try { cam2 = Cam.Instance; if (cam2 != null) { cam2Was = cam2.enabled; cam2.enabled = false; } } catch { }
                    oPos = c.transform.position; oRot = c.transform.rotation; oFov = c.fieldOfView; oMask = c.cullingMask; oScale = Time.timeScale;
                    Time.timeScale = 0f; Hiding = true;
                    try { foreach (var x in UnityEngine.Object.FindObjectsOfType<Canvas>()) if (x != null && x.enabled) { x.enabled = false; canv.Add(x); } } catch { }
                    int ui = LayerMask.NameToLayer("UI"); if (ui >= 0) c.cullingMask = oMask & ~(1 << ui);
                    c.transform.position = cam; c.transform.rotation = Quaternion.LookRotation(look - cam, Vector3.up); c.fieldOfView = fov;
                    holdCam = c; holdPos = cam; holdRot = c.transform.rotation; holdFov = fov;
                    set = true;
                    Log($"screenshot on screen: {tag} ({(bridge ? "from the bridge" : "observer at the ship")}) - {p.Target.name}");
                }
            }
            catch (Exception e) { Log("screenshot (ReShade): " + e.Message); }
            if (set)
            {
                for (int i = 0; i < 3; i++) yield return null;
                try { SaveScreen(tag + "_" + (lastBridge ? "bridge" : "observer")); } catch (Exception e) { Log("screenshot save: " + e.Message); }
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
                if (cam2 != null) cam2.enabled = cam2Was;
            }
            catch { }
            holdCam = null; Hiding = false; busy = false;
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
                    holdCam = c; holdPos = cam; holdRot = c.transform.rotation; holdFov = fov;
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
            try { if (p.Torp != null) p.Torp.SetActive(false); } catch { }
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
                var tor = Torpedo.Create(lastTube, start, dir, duration, speedArg); Register(tor, dir);
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
                var s = ships[i]; if (!Alive(s) || !Defender(s)) continue;
                float d = Flat(s.transform.position - p.Pos);
                if (d > r) continue;
                var g = Guns(s); guns += g.light;
                try { Barrage(p, s, d, g, low, dt); } catch { }
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
                Radio(p.Wave, null, atkIsPlayer ? (splash ? Pick("Christ - he flew into a splash! He's gone in!", "Heavy guns firing into the sea ahead of us - Number Four caught it!") : Pick("Number Three's hit - he's going down in flames!", "They've got Jenkins! Keep low, keep low!", "Flak's thick - lost one of ours!")) : Pick("Got one! Enemy plane down in the sea!", "Splash one torpedo plane!"));
                if (waveStats[p.Wave][2] <= 4) Log($"wave {p.Wave}: aircraft shot down {(splash ? $"by the splash barrage of the heavy guns ({heavy} gun(s) firing into the sea ahead of it)" : $"by light anti-aircraft guns ({guns} gun(s) of 2-3 in in range)")}{(p.Dropped ? ", after its drop" : ", before its drop")}");
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // 1.11.385 (#85) aerial torpedoes: their own warhead, and a camera that follows them
        static readonly List<(Torpedo t, Vector3 dir)> aerial = new List<(Torpedo, Vector3)>();
        static readonly HashSet<IntPtr> aerialPtr = new HashSet<IntPtr>();
        internal static bool IsAerial(IntPtr p) { return p != IntPtr.Zero && aerialPtr.Contains(p); }
        // aerial warhead against the ship torpedo of the tube the data come from (claude/torpedy_lotnicze_technologie_85.md: aerial
        // warheads 145 kg about 1920, 150-210 kg 1931-1937, 240-270 kg 1943-1945; ship torpedoes of the same years carried more)
        static int factorLogged;
        internal static float WarheadFactor(Ship hit)
        {
            int y = 1925; try { y = (int)hit.GetYear(hit); } catch { }
            float f = y < 1930 ? 0.5f : y < 1940 ? 0.6f : 0.75f;
            if (factorLogged++ < 6) Log($"aerial torpedo hit on {hit.name}: damage x{f:0.00} (aerial warhead of its time)");
            return f;
        }
        static void Register(Torpedo t, Vector3 dir)
        {
            if (t == null) return;
            try { aerial.Add((t, dir)); aerialPtr.Add(t.Pointer); } catch { }
        }
        static bool Alive(Torpedo t) { try { return t != null && t.gameObject != null && t.gameObject.activeInHierarchy; } catch { return false; } }

        // ---------------------------------------------------------------------------------------------------------------------------
        // 1.11.386 (author 29.10 17:02: "above the attacking squadron there should be an identification label - and the dispatch log -
        // and a bubble with the pilots' talk"): a label over every wave in the air (who, how many, what), the radio talk of its pilots
        // in a bubble under the label at the run-in, the drop, hits and losses, and the same lines in the dispatch log
        static readonly System.Random rnd = new System.Random();
        static readonly Dictionary<int, (string text, float t)> radio = new Dictionary<int, (string, float)>();
        static readonly HashSet<string> said = new HashSet<string>();
        static string Pick(params string[] v) { return v[rnd.Next(v.Length)]; }
        static string Flight(int wave) { return atkIsPlayer ? $"Flight {(char)('A' + (wave - 1) % 26)}, {CarrierName()}" : $"Enemy torpedo flight {wave}"; }
        // 1.11.393: the Short 184 flew from seaplane carriers - HMS Ben-my-Chree's Short 184 made the first sinking by an air-dropped torpedo (Dardanelles, August 1915)
        static string CarrierName() { var m = Model != null ? Model.Value : ""; return m == "short_184" ? "HMS Ben-my-Chree" : "HMS Furious"; }
        static string TypeName() { var m = Model != null ? Model.Value : ""; return m == "sopwith_camel" ? "Sopwith Camel" : m == "short_184" ? "Short 184" : string.IsNullOrEmpty(m) ? "torpedo biplane" : m.Replace('_', ' '); }
        static void Radio(int wave, string key, string text, bool log = true)
        {
            if (key != null && !said.Add(wave + "|" + key)) return;
            radio[wave] = (text, Time.unscaledTime);
            if (log) { try { Dispatch.Air($"{Flight(wave)}: \"{text}\""); } catch { } }
        }
        static readonly HashSet<string> hitSaid = new HashSet<string>();
        internal static void OnAerialHit(Ship hit)
        {
            try
            {
                int wave = 0; foreach (var kv in waveStats) if (kv.Key > wave) wave = kv.Key;
                if (wave == 0) return;
                string n = Dispatch.Ship(hit.name);
                if (!hitSaid.Add(wave + "|" + n + "|" + (int)(Time.unscaledTime / 3f))) return;
                Radio(wave, null, atkIsPlayer
                    ? Pick($"Hit! Right under her funnels - {n} is down by the head!", $"Fish running true... HIT! Big column of water on {n}!", $"That's one in {n}'s side - she's slowing!", $"Got her! {n} is listing to port!")
                    : Pick($"Torpedo hit on {n}!", $"{n} hit by an aerial torpedo - flooding forward!"));
            }
            catch { }
        }
        static GUIStyle sTag, sTalk;
        static void DrawLabels()
        {
            var cam = Camera.main; if (cam == null) return;
            if (sTag == null) { sTag = new GUIStyle(); sTag.fontSize = 15; sTag.fontStyle = FontStyle.Bold; sTag.alignment = TextAnchor.MiddleCenter; sTag.normal.textColor = new Color(0.75f, 0.92f, 1f); }
            if (sTalk == null) { sTalk = new GUIStyle(); sTalk.fontSize = 14; sTalk.wordWrap = true; sTalk.alignment = TextAnchor.UpperCenter; sTalk.normal.textColor = new Color(1f, 0.95f, 0.8f); }
            var groups = new Dictionary<int, (Vector3 sum, int n, int up)>();
            foreach (var p in planes) { if (p.Go == null) continue; groups.TryGetValue(p.Wave, out var g); g.sum += p.Pos; g.n++; if (p.P != Phase.Down) g.up++; groups[p.Wave] = g; }
            foreach (var kv in groups)
            {
                if (kv.Value.up == 0) continue;
                var c = kv.Value.sum / kv.Value.n; var sp = cam.WorldToScreenPoint(c + Vector3.up * 25f * Scale.Value); if (sp.z <= 0f) continue;
                float k = UiScale.F > 0.01f ? UiScale.F : 1f, x = sp.x / k, y = (Screen.height - sp.y) / k;
                string tag = $"{Flight(kv.Key)} - {kv.Value.up} x {TypeName()} (torpedo)";
                float w = 360f, h = 24f;
                var col = GUI.color; GUI.color = atkIsPlayer ? new Color(0.6f, 0.85f, 1f, 0.95f) : new Color(1f, 0.55f, 0.5f, 0.95f);
                GUI.Box(new Rect(x - w / 2f, y - h - 6f, w, h), ""); GUI.color = col;
                sTag.normal.textColor = atkIsPlayer ? new Color(0.75f, 0.92f, 1f) : new Color(1f, 0.75f, 0.7f);
                GUI.Label(new Rect(x - w / 2f, y - h - 6f, w, h), tag, sTag);
                if (radio.TryGetValue(kv.Key, out var rt) && Time.unscaledTime - rt.t < 9f)
                {
                    float bw = 380f, bh = 54f; var r = new Rect(x - bw / 2f, y + 2f, bw, bh);
                    GUI.Box(r, ""); GUI.Box(r, "");
                    GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, r.height - 8f), rt.text, sTalk);
                }
            }
        }

        // camera buttons while an air strike is on: follow an aircraft, follow a torpedo just above the water, or free (the game's camera)
        enum Follow { None, Plane, Torpedo }
        static Follow follow; static int followIdx; static bool camPaused; static CameraController fcc; static Cam fcam; static bool fccWas, fcamWas;
        // 1.11.387 (author 29.10 17:11: "in the custom battle panel there should be a class 'torpedo air strike' with the number of waves
        // and of aircraft in a wave"): a small panel on the custom battle setup screen - our carrier's strike on or off, waves, aircraft
        // per wave (written to CustomBattleCarrier, AirStrikeWaves, AirStrikeAircraftPerWave)
        static GUIStyle sSet, sSetH;
        static void SetupPanel()
        {
            bool setup = false; try { setup = GameManager.IsCustomBattleSetup; } catch { }
            if (!setup || Carrier == null || Waves == null || PerWave == null) return;
            float k = UiScale.F > 0.01f ? UiScale.F : 1f, W = Screen.width / k, H = Screen.height / k;
            if (sSetH == null) { sSetH = new GUIStyle(); sSetH.fontStyle = FontStyle.Bold; sSetH.fontSize = 15; sSetH.normal.textColor = new Color(0.85f, 0.93f, 1f); }
            if (sSet == null) { sSet = new GUIStyle(); sSet.fontSize = 14; sSet.normal.textColor = Color.white; }
            float w = 300f, h = 150f, x = W - w - 24f, y = H * 0.42f;
            GUI.Box(new Rect(x, y, w, h), ""); GUI.Box(new Rect(x, y, w, h), "");
            GUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 22f), "Torpedo air strike (our carrier)", sSetH);
            bool on = Carrier.Value;
            if (GUI.Button(new Rect(x + 10f, y + 32f, w - 20f, 24f), on ? "ON - click to switch off" : "OFF - click to switch on")) Carrier.Value = !on;
            int wv = Waves.Value, pw = PerWave.Value;
            GUI.Label(new Rect(x + 10f, y + 64f, 150f, 24f), $"Waves: {wv}", sSet);
            if (GUI.Button(new Rect(x + w - 90f, y + 64f, 36f, 22f), "-") && wv > 1) Waves.Value = wv - 1;
            if (GUI.Button(new Rect(x + w - 48f, y + 64f, 36f, 22f), "+") && wv < 8) Waves.Value = wv + 1;
            GUI.Label(new Rect(x + 10f, y + 92f, 170f, 24f), $"Aircraft per wave: {pw}", sSet);
            if (GUI.Button(new Rect(x + w - 90f, y + 92f, 36f, 22f), "-") && pw > 1) PerWave.Value = pw - 1;
            if (GUI.Button(new Rect(x + w - 48f, y + 92f, 36f, 22f), "+") && pw < 24) PerWave.Value = pw + 1;
            GUI.Label(new Rect(x + 10f, y + 120f, w - 20f, 22f), $"{TypeName()} - battles of {CarrierFrom.Value}-{CarrierTo.Value}", sSet);
        }

        internal static void OnGUI()
        {
            try { SetupPanel(); } catch { }
            if (!active || !GameManager.IsBattle || Hiding) return;
            try { DrawLabels(); } catch { }
            bool any = planes.Count > 0 || aerial.Count > 0 || follow != Follow.None;
            if (!any) return;
            float kk = UiScale.F > 0.01f ? UiScale.F : 1f;
            float w = 150f, h = 26f, x = Screen.width / kk / 2f - w * 1.5f - 8f, y = Screen.height / kk - h - 70f;
            GUI.Box(new Rect(x - 6f, y - 22f, w * 3f + 28f, h + 30f), "Air strike camera");
            if (GUI.Button(new Rect(x, y, w, h), (follow == Follow.Plane ? "> " : "") + "Aircraft")) { follow = Follow.Plane; followIdx++; }
            if (GUI.Button(new Rect(x + w + 8f, y, w, h), (follow == Follow.Torpedo ? "> " : "") + "Torpedo (at the water)")) { follow = Follow.Torpedo; followIdx++; }
            if (GUI.Button(new Rect(x + 2f * (w + 8f), y, w, h), (follow == Follow.None ? "> " : "") + "Free camera")) { follow = Follow.None; }
        }
        static void PauseCam(bool pause)
        {
            if (pause == camPaused) return;
            try
            {
                if (pause)
                {
                    fcc = UnityEngine.Object.FindObjectOfType<CameraController>(); if (fcc != null) { fccWas = fcc.enabled; fcc.enabled = false; }
                    fcam = Cam.Instance; if (fcam != null) { fcamWas = fcam.enabled; fcam.enabled = false; }
                }
                else { if (fcc != null) fcc.enabled = fccWas; if (fcam != null) fcam.enabled = fcamWas; }
            }
            catch { }
            camPaused = pause;
        }
        static void FollowTick()
        {
            if (holdCam != null) return;   // a screenshot holds the camera now
            if (follow == Follow.None || !GameManager.IsBattle) { PauseCam(false); if (!GameManager.IsBattle) follow = Follow.None; return; }
            var c = Camera.main; if (c == null) return;
            Vector3 cam, look;
            if (follow == Follow.Plane)
            {
                Plane p = null; int n = planes.Count;
                for (int k = 0; k < n && p == null; k++) { var q = planes[(followIdx + k) % n]; if (q.Go != null && q.P != Phase.Down) p = q; }
                if (p == null) { follow = Follow.None; PauseCam(false); return; }
                var d = p.Dir; d.y = 0f; if (d.sqrMagnitude < 0.01f) d = Vector3.forward; d.Normalize();
                cam = p.Pos - d * 28f * Scale.Value + Vector3.up * 7f * Scale.Value; look = p.Pos + d * 120f;
            }
            else
            {
                (Torpedo t, Vector3 dir) best = (null, Vector3.zero); int n = aerial.Count;
                for (int k = n - 1; k >= 0; k--) if (!Alive(aerial[k].t)) { aerialPtr.Remove(aerial[k].t != null ? aerial[k].t.Pointer : IntPtr.Zero); aerial.RemoveAt(k); }
                n = aerial.Count; if (n > 0) best = aerial[(followIdx % n + n) % n];
                if (best.t == null) { if (planes.Count > 0) return; follow = Follow.None; PauseCam(false); return; }
                var pos = best.t.transform.position; var d = best.dir; d.y = 0f; d.Normalize();
                cam = new Vector3(pos.x, 0f, pos.z) - d * 14f + Vector3.up * 1.4f;   // just above the surface, behind the torpedo: its wake ahead
                look = new Vector3(pos.x, 0f, pos.z) + d * 70f + Vector3.up * 0.4f;
            }
            PauseCam(true);
            c.transform.position = cam; c.transform.rotation = Quaternion.LookRotation(look - cam, Vector3.up);
        }

        static void Summary()
        {
            foreach (var kv in waveStats) if (!reported.Contains(kv.Key)) Log($"wave {kv.Key} result: {kv.Value[0]} aircraft, {kv.Value[1]} torpedoes dropped, {kv.Value[2]} shot down ({kv.Value[3]} by the splash barrage of heavy guns)");
        }
    }
}
