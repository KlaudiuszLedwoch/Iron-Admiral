// 1.11.161 (beta): shell ballistics measurement. 1.11.163: physical ballistics.
// The game moves every shell along a ballistic arc with linear air drag (Il2CppBallistics.Trajectory3D) built by
// Shell.CalcTrajectory for a time of flight it derives itself. In DIP (and to a lesser degree in vanilla) those arcs are almost
// flat: a 15" shell at 11 km takes ~13 s and falls at ~1.5 deg, so long-range fire never plunges onto decks.
// Changing air_drag_coef / speed_shell_mod / shell_kinetic_energy_mod does not change the arcs (measured in 1.11.161/162),
// so RealisticBallistics now rebuilds the arc after CalcTrajectory:
//   1. a point-mass model with quadratic drag (air density falling with height, drag rising through the sound barrier),
//      shell mass from the calibre and a drag coefficient by era (blunt 2 crh shells before ~1912, 4 crh and longer later),
//      fitted to the 15"/42 Mk I and 12"/45 Mk X range tables (NavWeaps), gives the real time of flight and angle of fall
//      for the gun's muzzle velocity and the range;
//   2. the game's own linear-drag trajectory is rebuilt with a drag constant chosen so that it lands on the same target point
//      at the real time of flight with the real angle of fall.
// BallisticsLog (on by default) writes time of flight, elevation and angle of fall per calibre and range band to the log.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppBallistics;

namespace DipGgFixes
{
    internal static class Ballistics
    {
        internal static MelonPreferences_Entry<bool> LogOn, Realistic; internal static MelonPreferences_Entry<float> WorldSpeed;
        internal static float WS { get { float w = WorldSpeed != null ? WorldSpeed.Value : 3.2f; return w < 1f ? 1f : w; } }
        class Bin { public int N, Hit; public double T, Fall, Elev, V0, Vs; }
        static readonly SortedDictionary<string, Bin> bins = new SortedDictionary<string, Bin>();
        static float nextReport; static bool inBattle; static int errors, rebuilt, skipped, beyond, samples, probes, odd;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Ballistics] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            LogOn = cat.CreateEntry("BallisticsLog", true, null, "Battle: write the shells' time of flight, elevation and angle of fall per calibre and range to the log (for checking the ballistics against real range tables)");
            // 1.11.206 (author, 24.10): the battle world is time-compressed - ships move about 3.2x their nominal speed per game second
            //           (velocity checks: 52.7 m/s for a 32-knot cruiser, 32.3 m/s for a 24-knot torpedo boat) and the game's own shells fly
            //           3.2x faster too. Real times of flight in game seconds left the shells behind the ships ("I outrun my own shells").
            //           The physical time of flight is divided by this factor; angles, elevation and striking speed stay real.
            WorldSpeed = cat.CreateEntry("BattleWorldSpeedFactor", 3.2f, null, "Battle: how many times faster than its nominal speed a ship moves per game second (the game's own time compression, measured 3.2); shell times of flight are compressed by the same factor so that shells and ships share one clock");
            Realistic = cat.CreateEntry("RealisticBallistics", true, null, "Battle: physical ballistics - shells fly with the real time of flight and angle of fall of their calibre, muzzle velocity and era (plunging fire at long range), instead of the almost flat paths of the game");
        }

        internal static void Tick()
        {
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle && !inBattle) { inBattle = true; bins.Clear(); gunStats.Clear(); gunSeen.Clear(); GunLab.ResetCache(); grades.Clear(); rebuilt = skipped = beyond = samples = probes = 0; scaledMisses = 0; ratioSum = 0; mvLogged.Clear(); TechYears.Reset(); Obliquity.ResetCache(); nextReport = Time.time + 120f; }
                else if (!battle && inBattle) { inBattle = false; Report("battle end"); }
                else if (battle && Time.time > nextReport) { nextReport = Time.time + 120f; Report("battle"); }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        // ---- shell construction (1.11.180: by technology, never by the calendar year) ----
        // drag coefficient (at supersonic speed) by the shell nose technology of the firing ship: blunt 2 crh shells without it,
        // the improved nose (projectile_shell_15, 1907) ~4 crh, the 1927 shells (projectile_shell_16) longer still.
        // 1.11.212 (author, 24.10): refitted to NavWeaps range tables - 15"/42 4crh APC (871 kg, 2,450 fps): Cd 0.50 reproduces range and time of flight
        //           at 5.7-30 deg within 1-2 %; 15"/42 6crh (879 kg, 2,400 fps): 0.40; USN 5"/51 (22.7 kg, 3,150 fps, Range Tables 1935): 0.41
        //           (10,970 m: elevation 9.1 vs 9.34 deg, 22.6 vs 22.76 s, fall 19.7 vs 19.5 deg). One Cd fits 5-inch and 15-inch alike - the
        //           drag of small shells was overstated by the old 0.55 / 0.45, which made a 5-inch shell need 16-17 deg and 31 s for 11 km.
        static readonly float[] CdByNose = { 0.74f, 0.50f, 0.41f };
        static float Cd(Ship s) { return CdByNose[Math.Max(0, Math.Min(2, TechYears.ShellNose(s)))]; }
        internal static float CdOf(Ship s) { try { return Cd(s); } catch { return CdByNose[1]; } } // 1.11.216
        internal static float CdByMk(int mk) { return CdByNose[mk <= 2 ? 0 : mk == 3 ? 1 : 2]; } // 1.11.221
        internal static void ShootOut(double mv, double calIn, double mass, double cd, double el, out double range, out double time, out double fall) { Shoot(mv, calIn, mass, cd, el, out range, out time, out fall); }
        static float Mass(float calIn) { float dcm = calIn * 2.54f; return 0.0145f * dcm * dcm * dcm; }
        // shell weight as the game computes it for this gun, ship and shell type (light/heavy shell technologies, AP/HE); formula as fallback
        static float ShellMass(Shell sh, Part p, float calIn)
        {
            try
            {
                if (p != null && p.data != null)
                {
                    var st = new Il2CppSystem.Nullable<Ship.ShellType>(sh.shellType);
                    float w = Part.ShellWeight(p.data, p.ship, st);
                    if (w > 0.1f && w < 3000f) return w;
                }
            }
            catch { }
            return Mass(calIn);
        }
        static double MachFactor(double m)
        {
            if (m < 0.8) return 0.45;
            if (m < 1.1) return 0.45 + (m - 0.8) / 0.3 * 0.55;
            return Math.Pow(1.1 / m, 0.35);
        }

        // ---- real trajectory (point mass, quadratic drag) ----
        static void Shoot(double mv, double calIn, double mass, double cd, double el, out double range, out double time, out double fall)
        {
            double d = calIn * 0.0254, m = mass > 0.1 ? mass : Mass((float)calIn), A = Math.PI * d * d / 4;
            double x = 0, y = 0, vx = mv * Math.Cos(el), vy = mv * Math.Sin(el), t = 0, dt = 0.05;
            while (true)
            {
                double v = Math.Sqrt(vx * vx + vy * vy), rho = 1.225 * Math.Exp(-y / 9000.0), a = 343.0 - 0.004 * Math.Max(0, Math.Min(y, 11000));
                double k = 0.5 * rho * cd * MachFactor(v / a) * A / m;
                double ax = -k * v * vx, ay = -9.81 - k * v * vy;
                double nx = x + vx * dt, ny = y + vy * dt; vx += ax * dt; vy += ay * dt; t += dt;
                if ((ny < 0 && t > 0.1) || t > 200)
                {
                    double f = ny < 0 ? y / (y - ny) : 1; range = x + (nx - x) * f; time = t - dt + dt * f; fall = Math.Atan2(-vy, vx); return;
                }
                x = nx; y = ny;
            }
        }
        struct Real { public bool Ok, Beyond; public float T, Fall, Mv; }
        static readonly Dictionary<long, Real> cache = new Dictionary<long, Real>();
        // 1.11.224 (author's decision 24.10): the barrels never rise above the mounting of the gun's generation (elevMax), and the range
        //          is never cut. A target beyond what the shell reaches at that elevation is reached with a higher muzzle velocity -
        //          the shell leaves faster and flies the mount-elevation arc (real time of flight, real angle of fall of that arc).
        //          Replaces the stretched 45-deg shot of 1.11.198 and the range cap of 1.11.216 (off by default since 1.11.224).
        static Real RealFor(float mv, float calIn, float mass, float cd, float R, float elevMax)
        {
            int mvi = Mathf.RoundToInt(mv / 10f), ci = Mathf.RoundToInt(calIn * 2f), ri = Mathf.Clamp(Mathf.RoundToInt(R / 200f), 0, 16383), cdi = Mathf.RoundToInt(cd * 100f), mi = Mathf.Clamp(Mathf.RoundToInt(mass), 0, 16383), ei = Mathf.Clamp(Mathf.RoundToInt(elevMax), 1, 63);
            long key = ((long)mvi << 52) | ((long)ci << 43) | ((long)cdi << 34) | ((long)mi << 20) | ((long)ei << 14) | (long)ri;
            Real r; if (cache.TryGetValue(key, out r)) return r;
            double mvq = mvi * 10.0, cq = ci / 2.0, Rq = ri * 200.0, cdq = cdi / 100.0, mq = mi, rr, tt, ff, em = ei * Math.PI / 180;
            r.Mv = (float)mvq;
            Shoot(mvq, cq, mq, cdq, em, out rr, out tt, out ff);
            if (rr < Rq)
            {
                // 1.11.225 (author's decision 24.10): the muzzle velocity comes from the technologies alone and the mount elevation from the
                //          gun generation; a target beyond the physical reach at that elevation (the range cap of RangeByMountElevation keeps
                //          this rare) flies the maximum-elevation shot stretched to the range, as 1.11.198 did at 45 deg
                r.Ok = false; r.Beyond = true; r.T = (float)(tt * Rq / rr); r.Fall = (float)ff;
            }
            else
            {
                double lo = 0, hi = em;
                for (int i = 0; i < 28; i++) { double mid = (lo + hi) / 2; Shoot(mvq, cq, mq, cdq, mid, out rr, out tt, out ff); if (rr < Rq) lo = mid; else hi = mid; }
                Shoot(mvq, cq, mq, cdq, (lo + hi) / 2, out rr, out tt, out ff);
                r.Ok = true; r.T = (float)tt; r.Fall = (float)ff;
            }
            if (cache.Count > 20000) cache.Clear();
            cache[key] = r; return r;
        }
        // ---- the game's trajectory model (measured with probes, 1.11.166): with p0, v0, drag constant k and terminal velocity vInf
        //      p(t) = p0 + vInf t + (v0 - vInf) t / (1 + k t),   v(t) = vInf + (v0 - vInf) / (1 + k t)^2
        //      so a shell at rest accelerates downwards with 2 k |vInf|; the game uses about 3.1 m/s2 there (1/3 of real gravity),
        //      which is why its arcs are so flat. Here: the arc through (R, dy) at time T, gravity g, drag constant k.
        static void Arc(double k, double g, double R, double dy, double T, out double vx0, out double vy0, out double vi, out double q, out double fall)
        {
            q = 1 + k * T; vi = -g / (2 * k);
            vx0 = R * q / T;
            vy0 = (dy - vi * T) * q / T + vi;
            double vxT = vx0 / (q * q), vyT = vi + (vy0 - vi) / (q * q);
            fall = Math.Atan2(-vyT, vxT);
        }

        internal static void Rebuild(Shell.Arc arc, Vector3 from, Vector3 to, float calIn, Ship ship = null, float mass = 0f, int grade = 0, float mvGun = 0f)
        {
            if (Realistic == null || !Realistic.Value || arc == null || calIn < 0.9f) return;
            try
            {
                Vector3 rel = to - from; Vector2 hz = new Vector2(rel.x, rel.z); float R = hz.magnitude; if (R < 300f) return;
                // 1.11.238 (author's question 24.10: 406 mm "reach 17.3 km", barrels too high): the game's arc speed is not the muzzle
                //           velocity - the game fits a flat arc to its own time of flight, so its "initial velocity" is about range / time
                //           (16 in at 17.6 km: 674 m/s for a 780-790 m/s gun; 5 in at 10 km: 1044 m/s). The physics now starts from the
                //           gun's own muzzle velocity (Part.ShellVelocity: gun data x historical table x the ship's shell and barrel choices).
                float mv = mvGun > 100f ? mvGun : arc.initialVelocity.magnitude; if (!(mv > 1f)) return;
                mv = Mathf.Clamp(mv, 300f, 1100f);
                float elevMax = grade > 0 ? MountElevation.Elev(grade) : 45f;
                var real = RealFor(mv, calIn, mass, Cd(ship), R, elevMax);
                if (real.T <= 0.05f) { skipped++; return; }
                if (real.Beyond) beyond++;                  // 1.11.224: reached with a higher muzzle velocity at the mount elevation
                if (real.Mv > mv) mv = real.Mv;
                double T = real.T, want = real.Fall, dy = rel.y; // real seconds; compressed to game time below (1.11.207)
                // drag constant: the shell leaves the muzzle with (about) its muzzle velocity and arrives at the real time
                double k = Math.Max(1e-6, (mv * Math.Cos(want * 0.5) * T / R - 1) / T);
                // gravity of the arc: 9.81 m/s2, raised where needed so that the shell also falls at the real angle
                double g = 9.81, vx0, vy0, vi, q, f;
                for (int i = 0; i < 3; i++)
                {
                    Arc(k, g, R, dy, T, out vx0, out vy0, out vi, out q, out f);
                    if (f <= 1e-4) break;
                    g = Math.Max(9.81, Math.Min(60.0, g * Math.Tan(want) / Math.Tan(f)));
                }
                Arc(k, g, R, dy, T, out vx0, out vy0, out vi, out q, out f);
                // 1.11.207: the arc is fitted in real time and then put on the game clock (time / WS): the same path in space with drag constant
                //           x WS and all velocities x WS (p(t) = p0 + vInf t + (v0 - vInf) t / (1 + k t) is invariant under that substitution).
                //           1.11.206 compressed T before the fit, which flattened the arcs (24.10: 11 in at 25.8 km fell at 15 deg instead of 33).
                double ws = WS; k *= ws; vx0 *= ws; vy0 *= ws; vi *= ws; T /= ws;
                Vector2 dir = hz / R;
                Vector3 v0 = new Vector3((float)(dir.x * vx0), (float)vy0, (float)(dir.y * vx0));
                Vector3 vInf = new Vector3(0f, (float)vi, 0f);
                var tr = new Trajectory3D((float)k, vInf, from, v0);
                tr.k = (float)k; tr.vInfinity = vInf; tr.p0 = from; tr.v0 = v0;
                double q2 = q * q;
                Vector3 vT = new Vector3((float)(dir.x * vx0 / q2), (float)(vi + (vy0 - vi) / q2), (float)(dir.y * vx0 / q2));
                Vector3 oldTV = arc.targetVelocity; float oldT = arc.timeToTarget;
                arc.trajectory = tr;
                arc.timeToTarget = (float)T;
                arc.initialVelocity = v0;
                arc.targetVelocity = vT;
                rebuilt++;
                // 1.11.199: diagnostics of odd short-range arcs (24.10 log: 10 in at 2-4 km - elevation 18 deg, fall -15 deg)
                if (odd < 12 && R < 10000f && (f < 0 || Math.Atan2(vy0, vx0) > 0.12))
                {
                    odd++;
                    Log($"odd arc {calIn:0.0} in at {R:0} m: dy {dy:0.0} m, muzzle {mv:0} m/s (game arc v0 {arc.initialVelocity.magnitude:0}), mass {mass:0.0} kg, Cd {Cd(ship):0.00}; physical T {T:0.00} s, fall {want * 180 / Math.PI:0.0} deg; fit k {k:0.00000}, g {g:0.0}, vx0 {vx0:0}, vy0 {vy0:0}, fall at T {f * 180 / Math.PI:0.0} deg; game T {oldT:0.00} s");
                }
                if (samples < 6)
                {
                    samples++;
                    Vector3 pT = tr.PositionAtTime((float)T), vt2 = tr.VelocityAtTime((float)T);
                    Log($"sample {calIn:0.0} in at {R:0} m: game T {oldT:0.0} s, target velocity {oldTV.magnitude:0}; physical T {T:0.0} s, fall {want * 180 / Math.PI:0.0} deg, k {k:0.00000}, gravity {g:0.00}; trajectory at T misses the aim point by {(pT - to).magnitude:0.0} m, its fall {Mathf.Atan2(-vt2.y, new Vector2(vt2.x, vt2.z).magnitude) * Mathf.Rad2Deg:0.0} deg");
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("rebuild: " + e.Message); }
        }

        // 1.11.179: gun generation (Mk) of the firing gun, cached per part
        static readonly Dictionary<long, int> grades = new Dictionary<long, int>();
        static int GradeOf(Part p)
        {
            if (p == null) return 0; long k = p.Pointer.ToInt64(); int g; if (grades.TryGetValue(k, out g)) return g;
            g = 0; try { var sh = p.ship; if (sh != null && p.data != null) g = sh.TechGunGrade(p.data, false); } catch { }
            grades[k] = g; return g;
        }

        // 1.11.181: per-shot dispersion by the shooter's fire-control technology (EraAccuracy.ShotDispersionRatio). For a missing shell
        // the offset of its splash point from the point the gun aimed at (target position + target velocity x the game's time of
        // flight) is scaled; the shell then flies to the new splash point.
        static int scaledMisses; static double ratioSum;
        static readonly HashSet<string> mvLogged = new HashSet<string>(); // 1.11.238
        static Vector3 ScaleMiss(Shell s, Part from, Ship shooter, Vector3 start, Vector3 to, float gameT)
        {
            try
            {
                if (s == null || s.willHitTarget || shooter == null || from == null || from.data == null) return to;
                float r = EraAccuracy.ShotDispersionRatio(shooter); if (Math.Abs(r - 1f) < 0.02f) return to;
                Ship.Aim aim = null;
                var d1 = shooter.enemies; if (d1 != null && d1.ContainsKey(from.data)) aim = d1[from.data];
                if (aim == null || aim.target == null) { var d2 = shooter.enemiesLeftSide; if (d2 != null && d2.ContainsKey(from.data)) aim = d2[from.data]; }
                var tg = aim != null ? aim.target : null; if (tg == null) return to;
                Vector3 aimPt = tg.transform.position + tg.velocityCurrent * Math.Max(0f, gameT);
                Vector3 off = to - aimPt; off.y = 0f;
                if (off.magnitude > 5000f) return to;
                Vector3 nt = aimPt + off * r; nt.y = to.y;
                try { s.originalTo = nt; } catch { }
                scaledMisses++; ratioSum += r;
                return nt;
            }
            catch { return to; }
        }

        // ---- measurement ----
        internal static void Created(Shell s, Part from)
        {
            if (s == null) return;
            try
            {
                var arc = s.arc; if (arc == null) return;
                Vector3 a = s.shellStart, b = s.originalTo;
                float cal = 0f; try { var d = from != null ? from.data : null; if (d != null) cal = d.caliber; } catch { }
                Ship shooter = null; try { shooter = from != null ? from.ship : null; } catch { }
                b = ScaleMiss(s, from, shooter, a, b, arc.timeToTarget);
                // 1.11.238: the gun's muzzle velocity for this ship and shell (not the game's fitted arc speed)
                float mvGun = 0f, bonus = 0f;
                try { if (from != null && from.data != null) { var st = new Il2CppSystem.Nullable<Ship.ShellType>(s.shellType); mvGun = Part.ShellVelocity(from.data, shooter, st); try { bonus = Part.ShellVelocityTotalBonus(from.data, shooter, st); } catch { } } } catch { }
                float arcV0 = arc.initialVelocity.magnitude;
                Rebuild(arc, a, b, cal / 25.4f, shooter, ShellMass(s, from, cal / 25.4f), GradeOf(from), mvGun); // 1.11.164: the arc of the created shell is rebuilt (patching CalcTrajectory crashed the game at start)
                if (cal >= 100f && mvLogged.Count < 24) { string mkey = $"{cal / 25.4f:0.0} in Mk{GradeOf(from)} {(shooter != null ? shooter.name : "?")}"; if (mvLogged.Add(mkey)) Log($"muzzle velocity source {mkey}: gun with the ship's shell and barrel choices {mvGun:0} m/s (bonus {bonus:+0.##;-0.##;0}), game arc speed {arcV0:0} m/s (range / time of flight), used {(mvGun > 100f ? mvGun : arcV0):0} m/s, mass {ShellMass(s, from, cal / 25.4f):0} kg, Cd {Cd(shooter):0.00}"); }
                try { var iv = arc.initialVelocity; float hz0 = new Vector2(iv.x, iv.z).magnitude; if (hz0 > 1f) BarrelElevation.Record(from, Mathf.Atan2(iv.y, hz0) * Mathf.Rad2Deg); } catch { } // 1.11.209
                if (LogOn == null || !LogOn.Value) return;
                float t = arc.timeToTarget * WS; if (t <= 0f) return; // logged in real seconds (1.11.206)
                float range = new Vector2(b.x - a.x, b.z - a.z).magnitude; if (range < 500f) return;
                Vector3 v0 = arc.initialVelocity; float h0 = new Vector2(v0.x, v0.z).magnitude;
                Vector3 v1 = v0; try { var tr = arc.trajectory; if (tr != null) v1 = tr.VelocityAtTime(arc.timeToTarget); /* 1.11.211: the trajectory runs in game time - the real-time t gave a fall of 80+ deg */ } catch { }
                float h1 = new Vector2(v1.x, v1.z).magnitude;
                float elev = Mathf.Atan2(v0.y, h0) * Mathf.Rad2Deg, fall = Mathf.Atan2(-v1.y, h1) * Mathf.Rad2Deg;
                if (cal < 100f) return; // main and heavy secondary guns only
                bool rad = false; try { rad = from != null && GunLab.HasRadar(from.ship); } catch { } int mk = GradeOf(from); string key = $"{cal / 25.4f,5:0.0} in Mk{mk} | {(rad ? "R" : "-")} | {Mathf.Floor(range / 2000f) * 2,2:0}-{Mathf.Floor(range / 2000f) * 2 + 2,2:0} km";
                if (!bins.TryGetValue(key, out var bin)) { bin = new Bin(); bins[key] = bin; }
                bin.N++; try { if (s.willHitTarget) bin.Hit++; } catch { } bin.T += t; bin.Fall += fall; bin.Elev += elev; bin.V0 += v0.magnitude / WS; bin.Vs += v1.magnitude / WS; // real m/s (1.11.206)
            }
            catch (Exception e) { if (errors++ < 5) Log("measure: " + e.Message); }
        }

        static void Report(string tag)
        {
            if (bins.Count == 0) return;
            var sb = new System.Text.StringBuilder($"{tag} - shells by calibre and range ({(Realistic != null && Realistic.Value ? $"physical ballistics (shell shape by technology), arcs rebuilt {rebuilt}, beyond the physical reach at the mount elevation (stretched shot) {beyond}, outside the model {skipped}, misses re-spread by fire-control technology {scaledMisses} (mean ratio {(scaledMisses > 0 ? ratioSum / scaledMisses : 1):0.00})" : "game ballistics")}): calibre and gun generation | radar (R) | range | shells | time of flight | elevation | angle of fall | muzzle / striking speed (m/s) | hits (decided by the game when the shell is fired)");
            foreach (var kv in bins)
            {
                var b = kv.Value; double n = b.N;
                sb.Append($"\n   {kv.Key} | {b.N,4} | {b.T / n,5:0.0} s | {b.Elev / n,5:0.0} deg | {b.Fall / n,5:0.0} deg | {b.V0 / n,5:0} / {b.Vs / n,5:0} | {100.0 * b.Hit / n,5:0.0}%");
            }
            Log(sb.ToString());
            if (gunStats.Count > 0)
            {
                var sg = new System.Text.StringBuilder($"{tag} - gun data used in this battle (for the historical review of rate of fire and range): calibre | grade | rounds per minute per barrel | max range (m)");
                foreach (var kv in gunStats) sg.Append($"\n   {kv.Key} | {(kv.Value[0] > 0 ? kv.Value[0].ToString("0.00") : "-")} | {(kv.Value[1] > 0 ? kv.Value[1].ToString("0") : "-")}");
                Log(sg.ToString());
            }
        }

        // 1.11.170: rate of fire and range per calibre and grade as the game computes them (measurement only)
        static readonly SortedDictionary<string, float[]> gunStats = new SortedDictionary<string, float[]>(); static readonly HashSet<long> gunSeen = new HashSet<long>();
        internal static void GunStat(GunData g, int grade, int idx, float v)
        {
            if (LogOn == null || !LogOn.Value || g == null || !inBattle || gunStats.Count > 200) return;
            try
            {
                long sk = (g.Pointer.ToInt64() << 4) ^ (grade << 1) ^ idx; if (!gunSeen.Add(sk)) return;
                string key = $"{g.caliberInch,4:0.0} in | {grade}";
                if (!gunStats.TryGetValue(key, out var a)) { a = new float[2]; gunStats[key] = a; }
                if (a[idx] <= 0f) a[idx] = v;
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Patch_ShellCreate_Ballistics
    {
        static MethodBase TargetMethod() { try { return AccessTools.GetDeclaredMethods(typeof(Shell)).FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length >= 2 && m.GetParameters()[1].ParameterType == typeof(Part)); } catch { return null; } }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Shell __result, Part __1) { Ballistics.Created(__result, __1); }
    }

    // Shell.CalcTrajectory(GunData gunData, int gunGrade, Vector3 initialPosition, Vector3 targetPosition, Ship shipHint, ...)
    [HarmonyPatch]
    internal static class Patch_CalcTrajectoryGun_Ballistics
    {
        static MethodBase TargetMethod()
        {
            try { return AccessTools.GetDeclaredMethods(typeof(Shell)).FirstOrDefault(m => m.Name == "CalcTrajectory" && m.GetParameters().Length >= 4 && m.GetParameters()[0].ParameterType == typeof(GunData) && m.GetParameters()[2].ParameterType == typeof(Vector3) && m.GetParameters()[3].ParameterType == typeof(Vector3)); } catch { return null; }
        }
        static bool Prepare() { return false; } // 1.11.164: off - the game crashed at start with this patch
        static void Postfix(Shell.Arc __result, GunData __0, Vector3 __2, Vector3 __3)
        {
            float cal = 0f; try { if (__0 != null) cal = __0.caliberInch; } catch { }
            Ballistics.Rebuild(__result, __2, __3, cal);
        }
    }

    // Shell.CalcTrajectory(PartData gun, Ship ship, Vector3 from, Vector3 to, ...)
    [HarmonyPatch]
    internal static class Patch_CalcTrajectoryPart_Ballistics
    {
        static MethodBase TargetMethod()
        {
            try { return AccessTools.GetDeclaredMethods(typeof(Shell)).FirstOrDefault(m => m.Name == "CalcTrajectory" && m.GetParameters().Length >= 4 && m.GetParameters()[0].ParameterType == typeof(PartData) && m.GetParameters()[2].ParameterType == typeof(Vector3) && m.GetParameters()[3].ParameterType == typeof(Vector3)); } catch { return null; }
        }
        static bool Prepare() { return false; } // 1.11.164: off - the game crashed at start with this patch
        static void Postfix(Shell.Arc __result, PartData __0, Vector3 __2, Vector3 __3)
        {
            float cal = 0f; try { if (__0 != null) cal = __0.caliber / 25.4f; } catch { }
            Ballistics.Rebuild(__result, __2, __3, cal);
        }
    }

    [HarmonyPatch(typeof(GunData), "Firerate")]
    internal static class Patch_GunFirerate_Stat { static void Postfix(GunData __instance, int __2, ref float __result) { try { if (__instance != null) __result *= HistoricalGuns.RofMult(__instance, __2); } catch { } Ballistics.GunStat(__instance, __2, 0, __result); } }

    [HarmonyPatch(typeof(GunData), "ShellWeight")] // 1.11.213
    internal static class Patch_GunShellWeight_Hist { static void Postfix(GunData __instance, int __2, ref float __result) { try { if (__instance != null) __result *= HistoricalGuns.ShellWeightMult(__instance, __2); } catch { } } }
    [HarmonyPatch(typeof(GunData), "ShellVelocity")] // 1.11.213
    internal static class Patch_GunShellVelocity_Hist { static void Postfix(GunData __instance, int __2, ref float __result) { try { if (__instance != null) __result *= HistoricalGuns.ShellVelocityMult(__instance, __2); } catch { } } }
    [HarmonyPatch(typeof(GunData), "Range")]
    internal static class Patch_GunRange_Stat { static void Postfix(GunData __instance, Ship __0, PartData __1, int __2, ref float __result) { try { if (__instance != null) __result *= HistoricalGuns.RangeMult(__instance, __2); } catch { } try { __result = MountElevation.CapRange(__instance, __0, __1, __2, __result); } catch { } Ballistics.GunStat(__instance, __2, 1, __result); } }

    // 1.11.216 (author, 24.10): a gun cannot reach further than its mounting can elevate. The game's range bonuses pushed a 279 mm Mk 4
    // gun to 41-44 km, which needs 45 degrees of elevation that no 1920s turret had (the barrels stood at the sky at 0.9 km in the
    // 24.10 battle). The range is limited to the physical range of the shell (muzzle velocity, weight, nose shape of the firing ship's
    // technologies) at the elevation of the mountings of the gun's generation: Mk 1 13.5 deg (12"/35 Mk VIII, 12"/40 Mk IX),
    // Mk 2 15 deg (US 12"/45, 12"/50), Mk 3 20 deg (15"/42 Mk I, 14"/45), Mk 4 30 deg (US 14"/50 Mk 11, 8"/55 Mk 9: 41 deg,
    // 16"/45 Mk I: 40 deg - the heavy mounts of the 1920s spread 30-41 deg), Mk 5 45 deg (16"/50 Mk 7, 15" C/34 30 deg, 381/50 36 deg).
    // Longer reach comes with the technologies that raise muzzle velocity and shell shape, as in history. Switch: RangeByMountElevation.
    internal static class MountElevation
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("RangeByMountElevation", true, null, "Battle: a gun's range is limited to what its shell reaches at the elevation of the mountings of its generation (Mk 1 13.5, Mk 2 15, Mk 3 20, Mk 4 30, Mk 5 45 degrees)");
        }
        internal static readonly float[] ElevByMk = { 13.5f, 15f, 20f, 30f, 45f };
        internal static float Elev(int grade) { return ElevByMk[Math.Max(0, Math.Min(4, grade - 1))]; }
        static readonly Dictionary<long, float> cache = new Dictionary<long, float>();
        static int logged, errors;
        internal static float CapRange(GunData g, Ship ship, PartData pd, int grade, float range)
        {
            if (On == null || !On.Value || g == null || range <= 0f || HistoricalGuns.Suspend) return range;
            try
            {
                float mv = 0f, mass = 0f; try { mv = g.ShellVelocity(ship, pd, grade); mass = g.ShellWeight(ship, pd, grade); } catch { }
                // 1.11.238: with a ship, its own gun values (shell and barrel choices of the design) - the same the shells fly with
                if (ship != null && pd != null) { try { float m2 = Part.ShellVelocity(pd, ship, new Il2CppSystem.Nullable<Ship.ShellType>()); if (m2 > 100f) mv = m2; float w2 = Part.ShellWeight(pd, ship, new Il2CppSystem.Nullable<Ship.ShellType>()); if (w2 > 0.1f) mass = w2; } catch { } }
                if (mv < 100f || mass <= 0f) return range;
                float calIn = g.caliberInch > 0f ? g.caliberInch : (pd != null ? pd.caliber / 25.4f : 0f); if (calIn <= 0f) return range;
                // 1.11.221: without a ship (the game asks for raw gun values) the shell shape of the generation: Mk 1-2 blunt, Mk 3 1907 nose, Mk 4-5 1927 nose
                float cd = ship != null ? Ballistics.CdOf(ship) : Ballistics.CdByMk(grade);
                int k = Math.Max(1, Math.Min(5, grade));
                long key = ((long)Mathf.RoundToInt(mv / 5f) << 40) ^ ((long)Mathf.RoundToInt(mass) << 20) ^ ((long)Mathf.RoundToInt(calIn * 4f) << 10) ^ ((long)Mathf.RoundToInt(cd * 100f) << 3) ^ k;
                float max;
                if (!cache.TryGetValue(key, out max))
                {
                    double rr, tt, ff; Ballistics.ShootOut(mv, calIn, mass, cd, Elev(k) * Math.PI / 180.0, out rr, out tt, out ff);
                    max = (float)rr; if (cache.Count > 5000) cache.Clear(); cache[key] = max;
                    if (logged++ < 40) MelonLogger.Msg($"[DIP GG Fixes][Guns] {calIn:0.0}\" Mk {k}: physical range at {Elev(k):0.#} deg mount elevation {max:0} m (muzzle {mv:0} m/s, {mass:0} kg, Cd {cd:0.00}); game range {range:0} m{(range > max ? " -> capped" : "")}");
                }
                return range > max ? max : range;
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Guns] range cap: " + e.Message); return range; }
        }
    }
}
