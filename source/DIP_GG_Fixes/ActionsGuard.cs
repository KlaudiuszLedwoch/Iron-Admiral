// 1.11.156: turn guard. ActionsManager.ChoosePlayerForAction (the AI picks a nation for a diplomatic action during NextTurn)
// threw a NullReferenceException for one of the nations (23.10, 1921: the turn stopped at "update research" - the exception
// ends the NextTurn coroutine). Most likely a small state switched on during the campaign (successor states) whose fleet data
// the game never filled. The exception is caught here: no nation is chosen for that action this turn and the turn goes on.
using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    [HarmonyPatch]
    internal static class Patch_ChoosePlayerForAction_Guard
    {
        static int logged;
        static MethodBase TargetMethod() { return AccessTools.Method(typeof(ActionsManager), "ChoosePlayerForAction"); }
        static bool Prepare() { return TargetMethod() != null; }
        static Exception Finalizer(Exception __exception, ref Player __result, ActionsManager.ActionType __0, Player __1)
        {
            if (__exception == null) return null;
            __result = null;
            if (logged++ < 20)
            {
                string who = "?"; try { who = __1 != null && __1.data != null ? __1.data.name : "?"; } catch { }
                MelonLogger.Warning($"[DIP GG Fixes][Turn] ChoosePlayerForAction({__0}, {who}) failed ({__exception.GetType().Name}: {__exception.Message}) - no nation chosen for this action, the turn goes on");
            }
            return null;
        }
    }
}
