// 1.11.463 (author 30.10 17:27 and 19:59: "why does saving a design take so long?" - 24.5 s between TweaksAndFixes' "Saving ship" and
// the auto save, while the mod's own design check takes 70 ms and its export logs nothing): a diagnostic switch. When on, the game
// methods whose names speak of designs, saving, refits or ship lists in PlayerController, CampaignController, Player, Ui and
// GameData are timed (Harmony prefix and postfix; methods with ref, out or Nullable parameters are left alone - patching those
// crashes Il2Cpp), and every call that takes more than 300 ms is logged with its time; at the end of a slow frame the slowest
// methods of that frame are listed. Off by default; switch: SlowCallProbeV2.
// 1.11.466: 1.11.465 timed 152 methods matched by words like Update, Check, Refresh and Ships - methods run every frame or from
// worker threads - and the game crashed 3 s later. Now only methods named after designs, saving and refits are timed (no
// Update/Refresh/Check/Ships/Fleet words), and methods with value-type (struct) parameters or results are left alone.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace DipGgFixes
{
    internal static class SlowProbe
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SlowCallProbeV2", false, null, "Diagnostics: time the game's design, save and refit methods and log every call over 300 ms (to find what makes saving a design and loading or refitting a ship slow)"); // 1.11.466: off again - on by default in 1.11.465 it crashed the game 3 s after start (author 30.10 20:19)
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][SlowProbe] " + m); }
        // 1.11.473 (30.10 21:16: a design save took 22.3 s and none of the 25 timed methods of 1.11.466 went over 300 ms): also the ship's own
        // store/clone methods, the game manager and the save storage; the timed methods are listed in the log once
        static readonly string[] Words = { "Design", "Save", "Refit", "Shared", "ToStore", "FromStore", "Clone", "Serialize" };
        static readonly string[] Never = { "Update", "Refresh", "Check", "OnGUI", "Tick", "Get", "Is", "Can", "Has" };
        [ThreadStatic] static Stack<long> stack;
        static readonly Dictionary<string, long> frameTotals = new Dictionary<string, long>();
        static int patched, logged;

        // 1.11.464: MelonLoader reads the saved switch values only after OnInitializeMelon, so the probe is installed from the first
        // frame on which the switch is on (Tick) - switching it on in F10 starts it at once, no restart needed.
        static HarmonyLib.Harmony H; static bool done;
        internal static void Install(HarmonyLib.Harmony h) { H = h; }
        internal static void Tick() { if (!done && H != null && On != null && On.Value) { done = true; Apply(H); } }
        static void Apply(HarmonyLib.Harmony h)
        {
            var pre = new HarmonyMethod(typeof(SlowProbe).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic));
            var post = new HarmonyMethod(typeof(SlowProbe).GetMethod(nameof(Post), BindingFlags.Static | BindingFlags.NonPublic));
            var names = new List<string>();
            foreach (var t in new[] { typeof(PlayerController), typeof(CampaignController), typeof(Player), typeof(Ui), typeof(GameData), typeof(Ship), typeof(GameManager), typeof(Storage) })
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        if (m.IsAbstract || m.IsGenericMethod || m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("_")) continue;
                        bool word = false; foreach (var w in Words) if (m.Name.Contains(w)) { word = true; break; }
                        if (!word) continue;
                        bool bad = false;
                        foreach (var w in Never) if (m.Name.StartsWith(w) || m.Name.Contains("Update") || m.Name.Contains("Refresh")) { bad = true; break; }
                        foreach (var p in m.GetParameters()) { var pt = p.ParameterType; if (pt.IsByRef || p.IsOut || (pt.IsValueType && !pt.IsPrimitive && !pt.IsEnum)) { bad = true; break; } }
                        var rt = m.ReturnType;
                        if (bad || rt.IsByRef || (rt.IsValueType && !rt.IsPrimitive && !rt.IsEnum && rt != typeof(void))) continue;
                        h.Patch(m, pre, post); patched++; names.Add(t.Name + "." + m.Name);
                    }
                    catch (Exception e) { if (logged++ < 10) Log($"{t.Name}.{m.Name}: not timed - {e.Message}"); }
                }
            }
            Log($"{patched} game method(s) timed - calls over 300 ms are logged: {string.Join(", ", names)}");
        }
        static void Pre() { if (stack == null) stack = new Stack<long>(); stack.Push(Stopwatch.GetTimestamp()); }
        static void Post(MethodBase __originalMethod)
        {
            try
            {
                if (stack == null || stack.Count == 0) return;
                long t0 = stack.Pop(); double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (ms > 300.0) Log($"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}: {ms:0} ms");
            }
            catch { }
        }
    }
}
