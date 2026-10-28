// 1.11.332 (#63): TweaksAndFixes TAFShipData.ResetTorpGrade threw ArgumentOutOfRangeException inside the TweaksAndFixes prefix of
// the game's random ship generator (Ship.GenerateRandomShip, called by the historical builder outside a campaign) - about 10 times
// in 2 hours, after a "Shipgen halted ... Result: Success" block. The exception is swallowed (the grades of that ship are reset
// again on the next generator run) and counted; the first few are logged in one line each.
using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace DipGgFixes
{
    // 1.11.354 (campaign 28.10, turn end 17:57: 120 exceptions in 60 ms right after the game started a land battle): the game asks
    // GunData.ShellWeight and Ship.TechGunGrade for a gun without a ship - the game's own lookup throws KeyNotFoundException and the
    // TweaksAndFixes postfix of TechGunGrade throws "Tried to fetch TAF Data from null ship". With no ship the call returns 0 (what
    // the failed call gave the game anyway); the exceptions are counted and the first one logged in one line.
    [HarmonyPatch(typeof(Il2Cpp.GunData), nameof(Il2Cpp.GunData.ShellWeight))]
    internal static class Patch_ShellWeightNoShip
    {
        internal static int Seen;
        static Exception Finalizer(Exception __exception, Il2Cpp.Ship ship, ref float __result)
        {
            if (__exception == null || ship != null) return __exception;
            __result = 0f;
            if (Seen++ == 0) MelonLogger.Msg("[DIP GG Fixes][Guard] GunData.ShellWeight called without a ship (game, land battle or auto-resolve) - returns 0, further cases counted only");
            return null;
        }
    }
    [HarmonyPatch(typeof(Il2Cpp.Ship), nameof(Il2Cpp.Ship.TechGunGrade))]
    internal static class Patch_TechGunGradeNoShip
    {
        internal static int Seen;
        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            string m = __exception.Message ?? "";
            if (!m.Contains("null ship")) return __exception;
            if (Seen++ == 0) MelonLogger.Msg("[DIP GG Fixes][Guard] Ship.TechGunGrade: TweaksAndFixes asked for the data of a missing ship - skipped, further cases counted only");
            return null;
        }
    }
    [HarmonyPatch]
    internal static class Patch_TafResetTorpGrade
    {
        static int seen;
        static MethodBase TargetMethod()
        {
            try { var t = AccessTools.TypeByName("TweaksAndFixes.TAFShipData"); return t != null ? AccessTools.Method(t, "ResetTorpGrade") : null; }
            catch { return null; }
        }
        static bool Prepare() { return TargetMethod() != null; }
        static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            string m = __exception.Message ?? "";
            if (__exception is ArgumentOutOfRangeException || m.Contains("ArgumentOutOfRange") || m.Contains("Index was out of range"))
            {
                if (seen++ < 3) MelonLogger.Msg("[DIP GG Fixes][TAF] TweaksAndFixes ResetTorpGrade: torpedo grade list shorter than expected - skipped (#63)");
                return null;
            }
            return __exception;
        }
    }
}
