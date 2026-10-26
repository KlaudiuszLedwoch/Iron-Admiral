// 1.11.289 (#51): TweaksAndFixes' Ui.Update postfix reads G.settings.filterdResolution[G.settings.resolution] after checking only
// "Count > resolution". When the game's own Settings.ApplyOnInit fails at start (ArgumentOutOfRangeException, seen twice in the
// test of 26.10 10:05), the index stays negative and the postfix throws in every frame (24 888 times in 6 minutes, a 33 MB log) - and
// everything after that line in the postfix (TAF's UI modifications, campaign and designer updates) is skipped for the whole session.
// The guard runs before Ui.Update: when the index is outside the list it points it at the entry matching the current screen
// resolution (adding that entry when the list has none), so nothing on screen changes and TAF's check passes. Logged once.
using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ResolutionGuard
    {
        static bool logged; static int fails;
        internal static void Check()
        {
            try
            {
                var s = G.settings; if (s == null) return;
                var list = s.filterdResolution; if (list == null) return;
                int idx = s.resolution, n = list.Count;
                if (idx >= 0 && idx < n) return;
                var cur = Screen.currentResolution; int found = -1;
                for (int i = 0; i < n; i++) { var r = list[i]; if (r.width == cur.width && r.height == cur.height) { found = i; break; } }
                if (found < 0) { list.Add(cur); found = list.Count - 1; }
                s.resolution = found;
                if (!logged) { logged = true; MelonLogger.Msg($"[DIP GG Fixes][Settings] resolution index {idx} was outside the list of {n} resolutions (game settings failed at start) - set to {found} ({cur.width}x{cur.height}, the current screen), so TweaksAndFixes' per-frame check no longer throws"); }
            }
            catch (Exception e) { if (fails++ < 3) MelonLogger.Warning("[DIP GG Fixes][Settings] resolution guard: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Ui), "Update")]
    internal static class Patch_UiUpdate_ResolutionGuard
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() { ResolutionGuard.Check(); }
    }
}
