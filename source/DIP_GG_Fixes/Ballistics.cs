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
        internal static MelonPreferences_Entry<bool> LogOn, Realistic;
        class Bin { public int N, Hit; public double T, Fall, Elev, V0, Vs; }
        static readonly SortedDictionary<string, Bin> bins = new SortedDictionary<string, Bin>();
        static float nextReport; static bool inBattle; static int errors, rebuilt, skipped, samples, probes;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Ballistics] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            LogOn = cat.CreateEntry("BallisticsLog", true, null, "Battle: write the shells' time of flight, elevation and angle of fall per calibre and range to the log (for checking the ballistics against real range tables)");
            Realistic = cat.CreateEntry("RealisticBallistics", true, null, "Battle: physical ballistics - shells fly with the real time of flight and angle of fall of their calibre, muzzle velocity and era (plunging fire at long range), instead of the almost flat paths of the game");
        }

        internal static void Tick()
        {
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle && !inBattle) { inBattle = true; bins.Clear(); gunStats.Clear(); gunSeen.Clear(); GunLab.ResetCache(); grades.Clear(); rebuilt = skipped = samples = probes = 0; scaledMisses = 0; ratioSum = 0; TechYears.Reset(); nextReport = Time.time + 120f; }
                else if (!battle && inBattle) { inBattle = false; Report("battle end"); }
                else if (battle && Time.time > nextReport) { nextReport = Time.time + 120f; Report("battle"); }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        // ---- shell construction (1.11.180: by technology, never by the calendar year) ----
        // drag coefficient (at supersonic speed) by the shell nose technology of the firing ship: blunt 2 crh shells without it,
        // the improved nose (projectile_shell_15, 1907) ~4 crh, the 1927 shells (projectile_shell_16) longer still.
        static readonly float[] CdByNose = { 0.74f, 0.55f, 0.45f };
        static float Cd(Ship s) { return CdByNose[Math.Max(0, Math.Min(2, TechYears.ShellNose(s)))]; }
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
        struct Real { public bool Ok; public float T, Fall; }
        static readonly Dictionary<long, Real> cache = new Dictionary<long, Real>();
        static Real RealFor(float mv, float calIn, float mass, float cd, float R)
        {
            int mvi = Mathf.RoundToInt(mv / 10f), ci = Mathf.RoundToInt(calIn * 2f), ri = Mathf.RoundToInt(R / 200f), cdi = Mathf.RoundToInt(cd * 100f), mi = Mathf.Clamp(Mathf.RoundToInt(mass), 0, 16383);
            long key = ((long)mvi << 52) | ((long)ci << 43) | ((long)cdi << 34) | ((long)mi << 20) | (long)ri;
            Real r; if (cache.TryGetValue(key, out r)) return r;
            double mvq = mvi * 10.0, cq = ci / 2.0, Rq = ri * 200.0, cdq = cdi / 100.0, mq = mi, rr, tt, ff;
            Shoot(mvq, cq, mq, cdq, 45 * Math.PI / 180, out rr, out tt, out ff);
            if (rr < Rq) { r.Ok = false; }
            else
            {
                double lo = 0, hi = 45 * Math.PI / 180;
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

        internal static void Rebuild(Shell.Arc arc, Vector3 from, Vector3 to, float calIn, Ship ship = null, float mass = 0f)
        {
            if (Realistic == null || !Realistic.Value || arc == null || calIn < 0.9f) return;
            try
            {
                Vector3 rel = to - from; Vector2 hz = new Vector2(rel.x, rel.z); float R = hz.magnitude; if (R < 300f) return;
                float mv = arc.initialVelocity.magnitude; if (!(mv > 1f)) return;
                mv = Mathf.Clamp(mv, 300f, 1100f);
                var real = RealFor(mv, calIn, mass, Cd(ship), R);
                if (!real.Ok || real.T <= 0.05f) { skipped++; return; }
                double T = real.T, want = real.Fall, dy = rel.y;
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
                Rebuild(arc, a, b, cal / 25.4f, shooter, ShellMass(s, from, cal / 25.4f)); // 1.11.164: the arc of the created shell is rebuilt (patching CalcTrajectory crashed the game at start)
                if (LogOn == null || !LogOn.Value) return;
                float t = arc.timeToTarget; if (t <= 0f) return;
                float range = new Vector2(b.x - a.x, b.z - a.z).magnitude; if (range < 500f) return;
                Vector3 v0 = arc.initialVelocity; float h0 = new Vector2(v0.x, v0.z).magnitude;
                Vector3 v1 = v0; try { var tr = arc.trajectory; if (tr != null) v1 = tr.VelocityAtTime(t); } catch { }
                float h1 = new Vector2(v1.x, v1.z).magnitude;
                float elev = Mathf.Atan2(v0.y, h0) * Mathf.Rad2Deg, fall = Mathf.Atan2(-v1.y, h1) * Mathf.Rad2Deg;
                if (cal < 100f) return; // main and heavy secondary guns only
                bool rad = false; try { rad = from != null && GunLab.HasRadar(from.ship); } catch { } int mk = GradeOf(from); string key = $"{cal / 25.4f,5:0.0} in Mk{mk} | {(rad ? "R" : "-")} | {Mathf.Floor(range / 2000f) * 2,2:0}-{Mathf.Floor(range / 2000f) * 2 + 2,2:0} km";
                if (!bins.TryGetValue(key, out var bin)) { bin = new Bin(); bins[key] = bin; }
                bin.N++; try { if (s.willHitTarget) bin.Hit++; } catch { } bin.T += t; bin.Fall += fall; bin.Elev += elev; bin.V0 += v0.magnitude; bin.Vs += v1.magnitude;
            }
            catch (Exception e) { if (errors++ < 5) Log("measure: " + e.Message); }
        }

        static void Report(string tag)
        {
            if (bins.Count == 0) return;
            var sb = new System.Text.StringBuilder($"{tag} - shells by calibre and range ({(Realistic != null && Realistic.Value ? $"physical ballistics (shell shape by technology), arcs rebuilt {rebuilt}, beyond model range {skipped}, misses re-spread by fire-control technology {scaledMisses} (mean ratio {(scaledMisses > 0 ? ratioSum / scaledMisses : 1):0.00})" : "game ballistics")}): calibre and gun generation | radar (R) | range | shells | time of flight | elevation | angle of fall | muzzle / striking speed (m/s) | hits (decided by the game when the shell is fired)");
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

    [HarmonyPatch(typeof(GunData), "Range")]
    internal static class Patch_GunRange_Stat { static void Postfix(GunData __instance, int __2, ref float __result) { try { if (__instance != null) __result *= HistoricalGuns.RangeMult(__instance, __2); } catch { } Ballistics.GunStat(__instance, __2, 1, __result); } }
}
