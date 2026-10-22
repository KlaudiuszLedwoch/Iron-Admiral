// 1.11.96: battle performance.
//  1. Perf: measures the time the mod itself takes per frame in battle (AimAndShoot, AILogic, OnUpdate, OnLateUpdate, OnGUI,
//     GetEffectiveArmor, Param) and the frame rate, and writes a "[Perf]" line every 30 s of battle and at its end.
//  2. ParamTweak: the per-nation preferred distance (ai_distance_mod) and the training-dependent surrender threshold were
//     applied through a Harmony postfix on MonoBehaviourExt.Param(string, float) - one of the most called methods of the game;
//     every call paid for the patch and for converting the parameter name to a managed string. Now the value of the parameter
//     itself (GameData.paramsRaw) is changed only for the duration of Ship.AILogic / Ship.CheckForSurrender and restored
//     right after. A self-test reads the parameter back through the game; if it does not match, the old postfix is used.
//  3. Armour diagnostics (1.11.43, log only) are off by default and their four Harmony hooks are installed only when switched
//     on (they ran on every shell impact and hit).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Perf
    {
        internal static MelonPreferences_Entry<bool> On;
        static MelonPreferences_Entry<bool> mig196;
        internal const int AIM = 0, AI = 1, UPD = 2, LATE = 3, GUI = 4, ARMOR = 5, PARAM = 6, TON = 7, ECON = 8, CANB = 9, N = 10;
        static readonly string[] names = { "AimAndShoot", "AILogic", "OnUpdate", "OnLateUpdate", "OnGUI", "GetEffectiveArmor", "Param", "TonnageMax/Limit", "series cost/time", "CanBuildShipsFromDesign" };
        static readonly long[] ticks = new long[N]; static readonly int[] calls = new int[N];
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly HashSet<IntPtr> ships = new HashSet<IntPtr>();
        static int frames; static float winStart; static float maxDt; static bool wasBattle; static int reports;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PerformanceLog", true, null,
                "Battle: write a [Perf] line every 30 s with the frame rate and the time the mod takes per frame (for finding slowdowns in large battles)");
            mig196 = cat.CreateEntry("PerfDefaults196", false, null, "Internal: 1.11.96 switched the armour diagnostics off once (they slowed large battles)");
            if (!mig196.Value)
            {
                try { if (ArmorDiag.On != null) ArmorDiag.On.Value = false; } catch { }
                mig196.Value = true;
            }
        }
        internal static bool Enabled { get { return On != null && On.Value; } }
        internal static long T() { return clock.ElapsedTicks; }
        internal static void Add(int k, long start) { ticks[k] += clock.ElapsedTicks - start; calls[k]++; }
        internal static void Ship(IntPtr p) { if (wasBattle) ships.Add(p); }

        // called once per frame from OnUpdate
        internal static void Frame()
        {
            if (!Enabled) return;
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            float now = UnityEngine.Time.unscaledTime;
            if (!b)
            {
                if (wasBattle) { Report("battle end"); wasBattle = false; Reset(now); return; }
                // 1.11.97: outside battle (turn processing, ship generator): report every 60 s when the mod's hooks were called
                frames++; float dt0 = UnityEngine.Time.unscaledDeltaTime; if (dt0 > maxDt) maxDt = dt0;
                if (now - winStart >= 60f) { CampaignReport(now - winStart); Reset(now); }
                return;
            }
            if (!wasBattle) { CampaignReport(now - winStart); wasBattle = true; reports = 0; Reset(now); return; }
            frames++;
            float dt = UnityEngine.Time.unscaledDeltaTime; if (dt > maxDt) maxDt = dt;
            if (now - winStart >= 30f) { Report("30 s"); Reset(now); }
        }
        static void Reset(float now) { winStart = now; frames = 0; maxDt = 0f; Array.Clear(ticks, 0, N); Array.Clear(calls, 0, N); ships.Clear(); }
        static int campReports;
        static void CampaignReport(float el)
        {
            if (el <= 0f || campReports > 300) return;
            double tpms = Stopwatch.Frequency / 1000.0, total = 0; long hooks = 0;
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < N; k++)
            {
                if (calls[k] == 0 || k == UPD || k == LATE || k == GUI) continue;
                double ms = ticks[k] / tpms; total += ms; hooks += calls[k];
                sb.Append($"{names[k]} {ms:0} ms ({calls[k]} calls), ");
            }
            double upd = (ticks[UPD] + ticks[LATE] + ticks[GUI]) / tpms;
            if (hooks < 1000 && maxDt < 1f) return; // quiet map browsing - nothing to report
            campReports++;
            MelonLogger.Msg($"[DIP GG Fixes][Perf] campaign {el:0} s: {frames} frames, worst frame {maxDt * 1000f:0} ms; mod hooks {total:0} ms in total ({sb.ToString().TrimEnd(' ', ',')}); mod update/GUI {upd:0} ms");
        }
        static void Report(string tag)
        {
            if (frames < 10 || reports++ > 400) return;
            float el = UnityEngine.Time.unscaledTime - winStart; if (el <= 0f) return;
            float fps = frames / el;
            double tpms = Stopwatch.Frequency / 1000.0, total = 0;
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < N; k++)
            {
                if (calls[k] == 0) continue;
                double ms = ticks[k] / tpms / frames; total += k == ARMOR || k == PARAM ? 0 : ms; // armour/param run inside AimAndShoot/AILogic or the game
                sb.Append($"{names[k]} {ms:0.00} ms ({calls[k] / (float)frames:0.#} calls), ");
            }
            int torps = 0; try { var all = Torpedo.AllTorpedo; torps = all != null ? all.Count : 0; } catch { }
            MelonLogger.Msg($"[DIP GG Fixes][Perf] {tag}: {fps:0.0} fps (average frame {1000f / fps:0.0} ms, worst {maxDt * 1000f:0} ms), ships {ships.Count}, torpedoes {torps}; mod per frame ~{total:0.00} ms: {sb.ToString().TrimEnd(' ', ',')}; game time x{UnityEngine.Time.timeScale:0.#}");
        }
    }

    internal static class ParamTweak
    {
        internal static bool Ok = true; static bool tested;
        static readonly Dictionary<string, ParamData> cache = new Dictionary<string, ParamData>();
        static readonly Dictionary<string, float> baseVal = new Dictionary<string, float>();
        static IntPtr gdPtr;
        static ParamData Get(string name)
        {
            var gd = G.GameData; if (gd == null) return null;
            if (gd.Pointer != gdPtr) { gdPtr = gd.Pointer; cache.Clear(); baseVal.Clear(); }
            if (cache.TryGetValue(name, out var pd)) return pd;
            var raw = gd.paramsRaw; if (raw == null) return null;
            ParamData p = null; if (!raw.TryGetValue(name, out p) || p == null) return null;
            cache[name] = p; baseVal[name] = p.value; return p;
        }
        internal static float Base(string name, float def) { var p = Get(name); return p != null && baseVal.TryGetValue(name, out var v) ? v : def; }
        // set the parameter for the duration of one game call; false = not applied (the Param postfix fallback handles it)
        internal static bool Begin(string name, float value)
        {
            if (!Ok) return false;
            try
            {
                var p = Get(name);
                if (p == null) { Fail($"parameter {name} not found in GameData.paramsRaw"); return false; }
                p.value = value;
                if (!tested)
                {
                    tested = true;
                    float got = MonoBehaviourExt.Param(name, -12345f);
                    if (Math.Abs(got - value) > 1e-4f) { p.value = baseVal[name]; Fail($"self-test: Param({name}) returned {got} instead of {value}"); return false; }
                    MelonLogger.Msg($"[DIP GG Fixes][Perf] parameter tweak self-test passed ({name} {baseVal[name]} -> {value} read back by the game); the Param hook is not needed");
                }
                return true;
            }
            catch (Exception e) { Fail(e.Message); return false; }
        }
        internal static void End(string name)
        {
            try { if (cache.TryGetValue(name, out var p) && baseVal.TryGetValue(name, out var v)) p.value = v; } catch { }
        }
        static void Fail(string why)
        {
            if (!Ok) return; Ok = false;
            MelonLogger.Warning("[DIP GG Fixes][Perf] parameter tweak unavailable (" + why + ") - falling back to the Param hook");
            try
            {
                var h = DipGgFixesMod.Harm; if (h == null) return;
                var m = HarmonyLib.AccessTools.Method(typeof(MonoBehaviourExt), "Param", new[] { typeof(string), typeof(float) });
                h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patch_Param), "Postfix"));
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Perf] Param hook fallback failed: " + e.Message); }
        }
    }
}
