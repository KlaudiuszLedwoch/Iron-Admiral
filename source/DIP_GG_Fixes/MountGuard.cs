// 1.11.293 (#32): Mount.Fits(PartData, ...) reads Mount.parentPart, a lazy getter: when the cached field parentPart_ is empty it
// looks for a Part among the mount's parents and asserts (Assertion.CheckNotNull) when there is none. On DIP hulls some mounts
// are left without a parent part (the part was removed or the mount sits outside any part), and every fit check against them
// threw "assertion failed" - 352 to 2 412 times per test battle, each with a full stack trace.
// The guard runs before the game (and before TweaksAndFixes' own Fits prefix): a mount with no Part above it cannot hold a part,
// so the answer is "does not fit" and the game's code is not entered. Mounts with a parent part are untouched.
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    [HarmonyPatch(typeof(Mount), "Fits", new Type[] { typeof(PartData), typeof(Il2CppSystem.Collections.Generic.List<string>), typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Patch_MountFits_Orphan
    {
        static int skipped; static int fails;
        // 1.11.332 (#65): the historical builder (French pre-dreadnought hulls) met a destroyed mount or part here about 85 000 times
        // in 15 minutes - every NullReferenceException wrote a full stack trace, the log grew to 94 MB and the game exited. A fit check
        // that throws now answers "does not fit"; the count lets the builder give up such a class.
        internal static int Nre;
        static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            string m = __exception.Message ?? "";
            if (__exception is NullReferenceException || m.Contains("NullReference"))
            {
                __result = false;
                if (Nre++ == 0 || Nre % 1000 == 0) MelonLogger.Msg($"[DIP GG Fixes][Parts] Mount.Fits on a destroyed mount or part - treated as not fitting ({Nre} time(s) so far)");
                return null;
            }
            return __exception;
        }
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Mount __instance, PartData __0, ref bool __result)
        {
            try
            {
                if (__instance == null || __0 == null) { __result = false; return false; } // 1.11.332 (#65): destroyed mount / no part data

                if (__instance.parentPart_ != null) return true;
                if (__instance.GetComponentInParent<Part>() != null) return true;
                __result = false;
                if (skipped++ < 5) MelonLogger.Msg($"[DIP GG Fixes][Parts] mount {__instance.name} has no parent part - treated as not fitting (skipped {skipped} time(s) so far; the game would assert here)");
                return false;
            }
            catch (Exception e) { if (fails++ < 3) MelonLogger.Warning("[DIP GG Fixes][Parts] mount guard: " + e.Message); return true; }
        }
    }
}
