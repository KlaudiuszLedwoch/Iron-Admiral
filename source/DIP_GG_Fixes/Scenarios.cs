// 1.11.61: scenarios - step 1, diagnostics of the game's campaign save handling (log only, no gameplay change).
// Scenarios will be started from the F10 panel without taking one of the game's five campaign save slots. To redirect the
// loading and the later automatic saves of a scenario to its own file, the mod needs to know how the game names and finds
// campaign saves: GameManager.campaignSavePattern, GetSavePath, GetCampaignSaveName(idx), the slot index in use and the
// arguments of ContinueCampaign / LoadCampaign / GetSaveContents when the player loads a campaign. All are logged once
// as "[Scenario] ..." lines.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Scenarios
    {
        static int logged;
        static bool baseLogged;
        static readonly HashSet<string> seen = new HashSet<string>();
        static void Log(string m) { if (logged++ < 60) MelonLogger.Msg("[DIP GG Fixes][Scenario] " + m); }
        static void Once(string key, string m) { if (seen.Add(key)) Log(m); }

        internal static void Tick()
        {
            if (baseLogged) return;
            if (Time.realtimeSinceStartup < 20f) return;
            try
            {
                var gm = GameManager.Instance; if (gm == null) return;
                baseLogged = true;
                string pattern = "?", path = "?"; int slot = -99;
                try { pattern = gm.campaignSavePattern; } catch { }
                try { path = gm.GetSavePath(); } catch (Exception e) { path = "error " + e.Message; }
                try { slot = gm.currentCampaignSlotIndex; } catch { }
                var names = new List<string>();
                for (int i = 0; i < 5; i++) { try { names.Add(i + "=" + gm.GetCampaignSaveName(i)); } catch (Exception e) { names.Add(i + "=error " + e.Message); } }
                Log($"save handling: pattern '{pattern}', path '{Anon(path)}', current slot {slot}, names {string.Join(", ", names)}");
            }
            catch (Exception e) { baseLogged = true; MelonLogger.Warning("[DIP GG Fixes][Scenario] diagnostics: " + e.Message); }
        }
        static string Anon(string s) { try { var u = Environment.UserName; if (!string.IsNullOrEmpty(u) && u.Length > 2) s = s.Replace(u, "<user>"); } catch { } return s; }

        internal static void Apply(HarmonyLib.Harmony h)
        {
            P(h, AccessTools.Method(typeof(GameManager), "GetCampaignSaveName", new[] { typeof(int) }), null, nameof(Post_Name));
            P(h, AccessTools.Method(typeof(GameManager), "ContinueCampaign"), nameof(Pre_Continue), null);
            P(h, AccessTools.Method(typeof(GameManager), "LoadCampaign"), nameof(Pre_Load), null);
            P(h, AccessTools.Method(typeof(GameManager), "GetSaveContents", new[] { typeof(string), typeof(int) }), nameof(Pre_Contents), null);
            P(h, AccessTools.Method(typeof(GameManager), "SetCampaignSlotIndex", new[] { typeof(int) }), nameof(Pre_Slot), null);
            P(h, AccessTools.Method(typeof(GameManager), "SaveInternal"), nameof(Pre_Save), null);
        }
        static void P(HarmonyLib.Harmony h, System.Reflection.MethodBase t, string pre, string post)
        {
            try
            {
                if (t == null) throw new Exception("method not found");
                h.Patch(t, pre != null ? new HarmonyMethod(AccessTools.Method(typeof(Scenarios), pre)) : null, post != null ? new HarmonyMethod(AccessTools.Method(typeof(Scenarios), post)) : null);
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Scenario] hook " + (t != null ? t.Name : "?") + " failed: " + e.Message); }
        }
        static void Post_Name(int idx, string __result) { Once("name" + idx + __result, $"GetCampaignSaveName({idx}) = '{__result}'"); }
        static void Pre_Continue(bool quickLoading, string fileName) { Log($"ContinueCampaign(quickLoading {quickLoading}, fileName '{fileName}')"); }
        static void Pre_Load(bool quickLoading, string saveName) { Log($"LoadCampaign(quickLoading {quickLoading}, saveName '{saveName}')"); }
        static void Pre_Contents(string filename, int idx) { Once("c" + filename + idx, $"GetSaveContents(filename '{filename}', idx {idx})"); }
        static void Pre_Slot(int idx) { Log($"SetCampaignSlotIndex({idx})"); }
        static void Pre_Save(bool force, bool ignoreStateCheck) { try { var gm = GameManager.Instance; Once("save" + (gm != null ? gm.currentCampaignSlotIndex : -1), $"SaveInternal(force {force}) with slot {(gm != null ? gm.currentCampaignSlotIndex : -1)}"); } catch { } }
    }
}
