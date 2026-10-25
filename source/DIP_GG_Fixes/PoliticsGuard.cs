// 1.11.249: politics window guards. In the author's campaign (24.10, 20:29, after Austria-Hungary came back) opening the politics
// window threw 96 NullReferenceExceptions in PoliticsRelationshipElement.Init(nation, player) and 30 in
// CampaignPolitics_ElementUI.Init/RefreshActions -> ActionsManager.CanInteractWithActionType, and the rows of several nations
// stayed empty (Russia, USA, Japan, Spain, China missing until a diplomatic action refreshed the list). The same pattern as the
// turn guards: a Player on the list with incomplete data. The exceptions are caught so the window finishes drawing its rows, and
// the first cases are logged with what is missing (data, relation, provinces) so the cause can be found.
using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PoliticsGuard
    {
        internal static int logged;
        internal static string Describe(Player p)
        {
            if (p == null) return "null player";
            string name = "?"; bool data = false, dis = false, major = false; int prov = -1; string ctrl = "?";
            try { data = p.data != null; if (data) name = p.data.name; } catch { }
            try { dis = p.isDisabled; } catch { }
            try { major = p.isMajor; } catch { }
            try { prov = p.provinces != null ? p.provinces.Count : -1; } catch { }
            try { ctrl = p.isAi ? "AI" : "human"; } catch { }
            return $"{name} (data {(data ? "ok" : "NULL")}, {(dis ? "disabled" : "active")}, {(major ? "major" : "minor")}, {prov} province(s), {ctrl})";
        }
        internal static string Relation(Player a, Player b)
        {
            try
            {
                foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return "relation found"; }
                return "NO relation record";
            }
            catch (Exception e) { return "relation lookup failed: " + e.Message; }
        }
    }

    [HarmonyPatch(typeof(PoliticsRelationshipElement), "Init")]
    internal static class Patch_PoliticsRelationshipElement_Guard
    {
        static Exception Finalizer(Exception __exception, Player __0, Player __1)
        {
            if (__exception == null) return null;
            if (PoliticsGuard.logged++ < 12)
                MelonLogger.Warning($"[DIP GG Fixes][Politics] relationship row failed ({__exception.GetType().Name}): nation {PoliticsGuard.Describe(__0)}, viewer {PoliticsGuard.Describe(__1)}, {PoliticsGuard.Relation(__0, __1)} - row left as drawn, the window goes on");
            return null;
        }
    }

    [HarmonyPatch]
    internal static class Patch_CampaignPolitics_ElementUI_Guard
    {
        static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "Init", "InitActions", "RefreshActions" })
            { var m = AccessTools.Method(typeof(CampaignPolitics_ElementUI), n); if (m != null) yield return m; }
        }
        static Exception Finalizer(Exception __exception, Player __0, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            if (PoliticsGuard.logged++ < 12)
                MelonLogger.Warning($"[DIP GG Fixes][Politics] {__originalMethod?.Name} failed ({__exception.GetType().Name}: {__exception.Message}) for {PoliticsGuard.Describe(__0)} - actions of this row may be missing, the window goes on");
            return null;
        }
    }

    [HarmonyPatch(typeof(ActionsManager), "CanInteractWithActionType")]
    internal static class Patch_CanInteractWithActionType_Guard
    {
        static int logged;
        static Exception Finalizer(Exception __exception, ref bool __result, Player __0, Player __1)
        {
            if (__exception == null) return null;
            __result = false;
            if (logged++ < 12)
                MelonLogger.Warning($"[DIP GG Fixes][Politics] CanInteractWithActionType failed ({__exception.GetType().Name}): {PoliticsGuard.Describe(__0)} -> {PoliticsGuard.Describe(__1)}, {PoliticsGuard.Relation(__0, __1)} - action treated as unavailable");
            return null;
        }
    }
}
