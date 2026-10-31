// 1.11.506 (#121, author 31.10, 11:57: "I'm playing one turn now so you can study preparing the turn while the human plays"): the
// end of turn of 09.1926 took 52.9 s, of which the game's own "ManageFleet" step took 35.2 s. This log-only probe measures how much of
// that is the AI's design generation: the time spent inside the coroutines GenerateRandomDesigns and AiManageFleet and in
// BuildNewShips, per nation for the design pass, summed per end of turn. A design pass started while the player is on the map can
// only save the GenerateRandomDesigns share. No effect on the game. Log tag [DesignAhead].
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace DipGgFixes
{
    internal static class DesignAheadTiming
    {
        sealed class Acc { public double ms; public int calls; public double worst; }
        static readonly Dictionary<string, Acc> byStep = new Dictionary<string, Acc>();
        static readonly Dictionary<string, Acc> byNation = new Dictionary<string, Acc>();
        static readonly Dictionary<IntPtr, string> nationOf = new Dictionary<IntPtr, string>();
        static float lastCall = -1f; static int turns;

        internal static void ApplyHooks(HarmonyLib.Harmony h)
        {
            try
            {
                var pre = new HarmonyMethod(typeof(DesignAheadTiming).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic));
                var post = new HarmonyMethod(typeof(DesignAheadTiming).GetMethod(nameof(Post), BindingFlags.Static | BindingFlags.NonPublic));
                int n = 0;
                foreach (var t in typeof(CampaignController).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    // 1.11.520: the NextTurn coroutine is not patched any more - with the patch every end of turn stopped with NullReferenceException (tests 31.10, betas 137-146)
                    if (!(t.Name.Contains("GenerateRandomDesigns") || t.Name.Contains("AiManageFleet"))) continue;
                    var m = t.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    h.Patch(m, pre, post); n++;
                }
                var b = AccessTools.Method(typeof(CampaignController), "BuildNewShips");
                if (b != null) { h.Patch(b, pre, post); n++; }
                MelonLogger.Msg("[DIP GG Fixes][DesignAhead] timing probe on " + n + " method(s)");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignAhead] timing probe: " + e.Message); }
        }

        static string StepOf(MethodBase m)
        {
            var dt = m.DeclaringType != null ? m.DeclaringType.Name : "";
            if (dt.Contains("GenerateRandomDesigns")) return "GenerateRandomDesigns";
            if (dt.Contains("AiManageFleet")) return "AiManageFleet";
            if (dt.Contains("NextTurn")) return "NextTurn";
            return m.Name;
        }
        // 1.11.507: the time of the last step of the end of turn (NextTurn / AiManageFleet), read by DesignAhead to stop at once
        internal static float LastTurnStep = -1f;
        static void Pre(out long __state, MethodBase __originalMethod)
        {
            __state = Stopwatch.GetTimestamp();
            try { var dt = __originalMethod.DeclaringType != null ? __originalMethod.DeclaringType.Name : ""; if (dt.Contains("NextTurn") || dt.Contains("AiManageFleet") || __originalMethod.Name == "BuildNewShips") { LastTurnStep = UnityEngine.Time.realtimeSinceStartup; DesignAhead.Abort("end of turn"); } } catch { }
        }
        static void Post(long __state, object __instance, MethodBase __originalMethod)
        {
            if (DesignAhead.Driving) return;   // the design pass on the map logs its own time
            try
            {
                double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
                string step = StepOf(__originalMethod);
                Add(byStep, step, ms);
                if (step == "GenerateRandomDesigns" && __instance != null)
                {
                    string nation = "?";
                    try
                    {
                        var ob = __instance as Il2CppSystem.Object; var key = ob != null ? ob.Pointer : IntPtr.Zero;
                        if (key == IntPtr.Zero || !nationOf.TryGetValue(key, out nation))
                        {
                            var p = __instance.GetType().GetProperty("player"); var pl = p != null ? p.GetValue(__instance) as Player : null;
                            nation = pl != null && pl.data != null ? pl.data.name : "?";
                            if (key != IntPtr.Zero) nationOf[key] = nation;
                        }
                    }
                    catch { }
                    Add(byNation, nation, ms);
                }
                lastCall = UnityEngine.Time.realtimeSinceStartup;
            }
            catch { }
        }
        static void Add(Dictionary<string, Acc> d, string k, double ms)
        {
            if (!d.TryGetValue(k, out var a)) { a = new Acc(); d[k] = a; }
            a.ms += ms; a.calls++; if (ms > a.worst) a.worst = ms;
        }

        // the sums of one end of turn are logged once nothing was measured for 5 s
        internal static void Tick()
        {
            if (lastCall < 0 || UnityEngine.Time.realtimeSinceStartup - lastCall < 5f) return;
            lastCall = -1f; turns++;
            string steps = string.Join("; ", byStep.OrderByDescending(x => x.Value.ms).Select(x => $"{x.Key} {x.Value.ms / 1000.0:0.0} s ({x.Value.calls} steps, worst {x.Value.worst:0} ms)"));
            string nations = string.Join(", ", byNation.OrderByDescending(x => x.Value.ms).Select(x => $"{x.Key} {x.Value.ms / 1000.0:0.0} s/{x.Value.calls}"));
            MelonLogger.Msg($"[DIP GG Fixes][DesignAhead] end of turn {turns}: {steps}");
            if (byNation.Count > 0) MelonLogger.Msg($"[DIP GG Fixes][DesignAhead] design generation per nation: {nations}");
            byStep.Clear(); byNation.Clear(); nationOf.Clear();
        }
    }
}
