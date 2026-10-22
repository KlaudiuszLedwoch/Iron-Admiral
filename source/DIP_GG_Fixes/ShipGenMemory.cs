// 1.11.99: ship generator failure memory (campaign).
// At the end of a turn the AI designs new ships with the game's generator (TweaksAndFixes prints "Begin shipgen" ... "Result").
// In a 22.10 log 5 of 8 designs failed and took 7.5 of 8 minutes: every failure runs all 4 attempts of 10-45 s each and the
// same nation tries the same hull again in later turns. Here a nation whose design on a hull failed gets only one attempt on
// that hull for the next months (ShipGenMemoryMonths); a success clears the entry. The generator itself is not changed -
// only the number of attempts of the game's coroutine (_triesTotal) is lowered before the first attempt.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ShipGenMemory
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<int> Months, Tries;
        static readonly Dictionary<string, int> failed = new Dictionary<string, int>();   // key -> month index of the last failure
        static readonly HashSet<IntPtr> limited = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, string> keys = new Dictionary<IntPtr, string>();
        static readonly Dictionary<IntPtr, float> started = new Dictionary<IntPtr, float>();
        internal static int saved, fails, succ;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ShipGenMemory", true, null, "Campaign: a nation whose ship design on a hull failed in the generator gets only one attempt on that hull for the next months (shorter end of turn)");
            Months = cat.CreateEntry("ShipGenMemoryMonths", 12, null, "Ship generator memory: how many months a failed nation/hull pair is remembered");
            Tries = cat.CreateEntry("ShipGenMemoryTries", 1, null, "Ship generator memory: attempts for a remembered nation/hull pair (the game uses 4)");
        }

        static int MonthIndex()
        {
            try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; }
        }
        static string Key(Ship s)
        {
            try { return s.player.data.name + "|" + s.hull.data.name; } catch { return null; }
        }

        internal static void After(Ship._GenerateRandomShip_d__573 it, int stateBefore, bool running)
        {
            if (On == null || !On.Value || it == null) return;
            bool campaign = false; try { campaign = GameManager.IsCampaign && !GameManager.IsConstructor && !GameManager.IsBattle; } catch { }
            if (!campaign) return;
            var p = it.Pointer;
            var ship = it.__4__this; if (ship == null) return;
            if (!keys.TryGetValue(p, out var key)) { key = Key(ship); if (key == null) return; keys[p] = key; started[p] = UnityEngine.Time.realtimeSinceStartup; }
            int total = it._triesTotal_5__4;
            // lower the number of attempts once, as soon as the coroutine has set it and before the first attempt has failed
            if (!limited.Contains(p) && total > 1 && it._tryN_5__5 == 0)
            {
                limited.Add(p);
                int now = MonthIndex();
                if (now >= 0 && failed.TryGetValue(key, out int at) && now - at < Months.Value)
                {
                    int t = Math.Max(1, Math.Min(total, Tries.Value));
                    if (t < total)
                    {
                        it._triesTotal_5__4 = t; saved += total - t;
                        MelonLogger.Msg($"[DIP GG Fixes][ShipGen] {key.Replace("|", ": hull ")} failed {now - at} month(s) ago - {t} attempt(s) instead of {total}");
                    }
                }
            }
            if (running) return;
            // the coroutine has finished: same success rule as the TweaksAndFixes report (attempts used up = failure)
            bool success = it._tryN_5__5 != it._triesTotal_5__4;
            float secs = started.TryGetValue(p, out var t0) ? UnityEngine.Time.realtimeSinceStartup - t0 : 0f;
            int m = MonthIndex();
            if (success) { succ++; failed.Remove(key); }
            else { fails++; if (m >= 0) failed[key] = m; }
            MelonLogger.Msg($"[DIP GG Fixes][ShipGen] {key.Replace("|", ": hull ")} - {(success ? "success" : "failure")} after {it._tryN_5__5} failed attempt(s) of {it._triesTotal_5__4}, {secs:0} s; remembered failures {failed.Count}, attempts saved this session {saved}");
            keys.Remove(p); limited.Remove(p); started.Remove(p);
        }
    }

    [HarmonyPatch(typeof(Ship._GenerateRandomShip_d__573), "MoveNext")]
    internal static class Patch_ShipGenMemory
    {
        private static void Prefix(Ship._GenerateRandomShip_d__573 __instance, out int __state)
        {
            __state = -99; try { __state = __instance.__1__state; } catch { }
        }
        private static void Postfix(Ship._GenerateRandomShip_d__573 __instance, int __state, bool __result)
        {
            try { ShipGenMemory.After(__instance, __state, __result); }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] memory: " + e.Message); }
        }
    }
}
