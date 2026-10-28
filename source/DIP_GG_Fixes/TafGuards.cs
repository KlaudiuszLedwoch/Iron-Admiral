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
