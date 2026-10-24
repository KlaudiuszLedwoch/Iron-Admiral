// 1.11.161 (beta): shell ballistics. The game computes every shell's path as a real ballistic arc with air drag
// (Il2CppBallistics.Trajectory3D) for a time of flight it derives from the gun, the range and two parameters: air_drag_coef
// (the bigger, the less the shell slows down) and speed_shell_mod (divisor of the time of flight). DIP 1.3.5 set them to 8.65
// and 1: shells fly almost flat and fast ("lasers"), so they never plunge onto decks at long range.
// 1. Measurement (BallisticsLog, on by default): time of flight, elevation and angle of fall per calibre and range band,
//    written to the log every 2 minutes of a battle and at its end - to compare with real range tables (NavWeaps).
// 2. RealisticBallistics (F10 Battle AI, off by default): the two parameters are set to BallisticsDragCoef / BallisticsSpeedMod
//    while the switch is on (the game's values come back when it is switched off).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Ballistics
    {
        internal static MelonPreferences_Entry<bool> LogOn, Realistic;
        internal static MelonPreferences_Entry<float> Drag, SpeedMod, Kinetic;
        class Bin { public int N; public double T, Fall, Elev, V0, Vs; }
        static readonly SortedDictionary<string, Bin> bins = new SortedDictionary<string, Bin>();
        static float nextReport; static bool inBattle; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Ballistics] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            LogOn = cat.CreateEntry("BallisticsLog", true, null, "Battle: write the shells' time of flight, elevation and angle of fall per calibre and range to the log (for checking the ballistics against real range tables)");
            Realistic = cat.CreateEntry("RealisticBallistics", false, null, "Battle: shells fly on realistic arcs - longer time of flight and steeper fall at long range (plunging fire onto decks), instead of the almost flat paths of DIP");
            Drag = cat.CreateEntry("BallisticsDragCoef", 8.45f, null, "Battle (realistic ballistics): air_drag_coef - the smaller, the more a shell slows down and the steeper it falls (game 8.45, DIP 8.65)");
            Kinetic = cat.CreateEntry("BallisticsKineticMod", 29.2f, null, "Battle (realistic ballistics): shell_kinetic_energy_mod - landing speed of shells, curvature of the arc (game 29.2, DIP 5.319)");
            SpeedMod = cat.CreateEntry("BallisticsSpeedMod", 1.0f, null, "Battle (realistic ballistics): speed_shell_mod - divisor of the time of flight (game and DIP: 1)");
        }

        // ---- parameters ----
        static readonly Dictionary<string, float> orig = new Dictionary<string, float>();
        static IntPtr gdPtr; static int applied = -1; static float aDrag, aSpeed, aKin;
        static ParamData P(string name)
        {
            try { var gd = G.GameData; if (gd == null || gd.paramsRaw == null) return null; ParamData p; return gd.paramsRaw.TryGetValue(name, out p) ? p : null; } catch { return null; }
        }
        static void Set(string name, float v)
        {
            var p = P(name); if (p == null) return;
            if (!orig.ContainsKey(name)) orig[name] = p.value;
            p.value = v;
        }
        static void Restore(string name) { var p = P(name); float v; if (p != null && orig.TryGetValue(name, out v)) p.value = v; }

        internal static void Tick()
        {
            try
            {
                var gd = G.GameData; if (gd == null) return;
                if (gd.Pointer != gdPtr) { gdPtr = gd.Pointer; orig.Clear(); applied = -1; }
                bool on = Realistic != null && Realistic.Value;
                if (on && (applied != 1 || aDrag != Drag.Value || aSpeed != SpeedMod.Value || aKin != Kinetic.Value))
                {
                    Set("air_drag_coef", Drag.Value); Set("speed_shell_mod", SpeedMod.Value); Set("shell_kinetic_energy_mod", Kinetic.Value);
                    applied = 1; aDrag = Drag.Value; aSpeed = SpeedMod.Value; aKin = Kinetic.Value;
                    float od, os; orig.TryGetValue("air_drag_coef", out od); orig.TryGetValue("speed_shell_mod", out os);
                    float ok0; orig.TryGetValue("shell_kinetic_energy_mod", out ok0); Log($"realistic ballistics on: air_drag_coef {od} -> {Drag.Value}, speed_shell_mod {os} -> {SpeedMod.Value}, shell_kinetic_energy_mod {ok0} -> {Kinetic.Value}");
                }
                else if (!on && applied != 0)
                {
                    if (applied == 1) { Restore("air_drag_coef"); Restore("speed_shell_mod"); Restore("shell_kinetic_energy_mod"); Log("realistic ballistics off: game values restored"); }
                    applied = 0;
                }
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle && !inBattle) { inBattle = true; bins.Clear(); nextReport = Time.time + 120f; }
                else if (!battle && inBattle) { inBattle = false; Report("battle end"); }
                else if (battle && Time.time > nextReport) { nextReport = Time.time + 120f; Report("battle"); }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        // ---- measurement ----
        internal static void Created(Shell s, Part from)
        {
            if (LogOn == null || !LogOn.Value || s == null) return;
            try
            {
                var arc = s.arc; if (arc == null) return;
                float t = arc.timeToTarget; if (t <= 0f) return;
                Vector3 a = s.shellStart, b = s.originalTo; float range = new Vector2(b.x - a.x, b.z - a.z).magnitude; if (range < 500f) return;
                Vector3 v0 = arc.initialVelocity; float h0 = new Vector2(v0.x, v0.z).magnitude;
                Vector3 v1 = v0; try { var tr = arc.trajectory; if (tr != null) v1 = tr.VelocityAtTime(t); } catch { }
                float h1 = new Vector2(v1.x, v1.z).magnitude;
                float elev = Mathf.Atan2(v0.y, h0) * Mathf.Rad2Deg, fall = Mathf.Atan2(-v1.y, h1) * Mathf.Rad2Deg;
                float cal = 0f; try { var d = from != null ? from.data : null; if (d != null) cal = d.caliber; } catch { }
                if (cal < 100f) return; // main and heavy secondary guns only
                string key = $"{cal / 25.4f,5:0.0} in | {Mathf.Floor(range / 2000f) * 2,2:0}-{Mathf.Floor(range / 2000f) * 2 + 2,2:0} km";
                if (!bins.TryGetValue(key, out var bin)) { bin = new Bin(); bins[key] = bin; }
                bin.N++; bin.T += t; bin.Fall += fall; bin.Elev += elev; bin.V0 += v0.magnitude; bin.Vs += v1.magnitude;
            }
            catch (Exception e) { if (errors++ < 5) Log("measure: " + e.Message); }
        }

        static void Report(string tag)
        {
            if (bins.Count == 0) return;
            var sb = new System.Text.StringBuilder($"{tag} - shells by calibre and range ({(Realistic != null && Realistic.Value ? $"realistic: drag {Drag.Value}, speed mod {SpeedMod.Value}, kinetic {Kinetic.Value}" : "game values")}): calibre | range | shells | time of flight | elevation | angle of fall | muzzle / striking speed (game units)");
            foreach (var kv in bins)
            {
                var b = kv.Value; double n = b.N;
                sb.Append($"\n   {kv.Key} | {b.N,4} | {b.T / n,5:0.0} s | {b.Elev / n,5:0.0} deg | {b.Fall / n,5:0.0} deg | {b.V0 / n,5:0} / {b.Vs / n,5:0}");
            }
            Log(sb.ToString());
        }
    }

    [HarmonyPatch]
    internal static class Patch_ShellCreate_Ballistics
    {
        static MethodBase TargetMethod() { try { return AccessTools.GetDeclaredMethods(typeof(Shell)).FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length >= 2 && m.GetParameters()[1].ParameterType == typeof(Part)); } catch { return null; } }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Shell __result, Part __1) { Ballistics.Created(__result, __1); }
    }
}
