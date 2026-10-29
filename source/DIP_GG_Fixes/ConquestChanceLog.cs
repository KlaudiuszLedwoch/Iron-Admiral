// 1.11.359 (#77, player report 29.10: a "military conflict" (uprising backed by Greece) in Austria-Hungary's province - the shown
// chance went 79, 67, 93, 100 % while the defender sent 2.5, 1.1, 2.4 and 3.3 times the needed tonnage). The game code
// (CampaignConquestEvent.GetConquestChance) for uprisings multiplies the previous chance by the kill ratio and by a factor that
// falls with the defender's tonnage, stores the result, and does that on every call - also each time the tooltip is built,
// not only once per turn. This log only (no change to the game's rule) writes every call that changes the value with its inputs,
// so the formula and the call count can be checked in a real campaign before anything is changed.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ConquestChanceLog
    {
        static readonly Dictionary<IntPtr, int> calls = new Dictionary<IntPtr, int>();
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Conflict] " + m); }

        [HarmonyPatch(typeof(CampaignConquestEvent), nameof(CampaignConquestEvent.GetConquestChance))]
        static class Patch_Chance
        {
            static void Prefix(CampaignConquestEvent __instance, out float __state)
            {
                __state = float.NaN;
                try { if (__instance != null) __state = __instance.cachedConquestChance; } catch { }
            }
            static void Postfix(CampaignConquestEvent __instance, bool force, float __result, float __state)
            {
                try
                {
                    if (__instance == null) return;
                    calls.TryGetValue(__instance.Pointer, out int n); n++; calls[__instance.Pointer] = n;
                    bool changed = float.IsNaN(__state) || Math.Abs(__result - __state) > 0.0005f;
                    if (!force && !changed && n > 3) return;
                    var e = __instance;
                    string att = "?", def = "?";
                    try { att = e.Attacker != null ? e.Attacker.name : "-"; } catch { }
                    try { def = e.Defender != null ? e.Defender.name : "-"; } catch { }
                    Log($"{e.Name} type {e.EventType}: chance {__state:0.000} -> {__result:0.000} (force {force}, call {n}) | attacker {att}, defender {def} | required {e.requiredTonnage} + additional {e.AdditionalRequiredTonnage}, current {e.CurrentTonnage} | duration {e.DurationCurrent}/{e.DurationTotal}, max {e.maxDuration} | kills attacker {e.AttackerKills} (total {e.AttackerKillsTotal}), defender {e.DefenderKills} (total {e.DefenderKillsTotal})");
                }
                catch (Exception ex) { Log("log failed: " + ex.Message); }
            }
        }
    }
}
