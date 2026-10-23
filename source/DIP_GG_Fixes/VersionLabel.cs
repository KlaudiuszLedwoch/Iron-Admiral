// 1.11.111 (beta): the version text in the lower right corner of the main menu and the map shows the Iron Admiral package
// version (Mods\IRON_ADMIRAL_VERSION.txt, e.g. "1.2.66-beta.1") in front of the base mod text that TweaksAndFixes puts there
// from params_override.csv (taf_versiontext, "DIP GG 1.3.5"): "Iron Admiral 1.2.66-beta.1 | DIP GG 1.3.5".
using System;
using System.IO;
using HarmonyLib;
using MelonLoader;
using UnityEngine.UI;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class VersionLabel
    {
        static string ia; static bool logged;
        internal static string Package()
        {
            if (ia != null) return ia;
            ia = "";
            try
            {
                var f = Path.Combine(MelonUtils.GameDirectory, "Mods", "IRON_ADMIRAL_VERSION.txt");
                if (File.Exists(f)) { var l = File.ReadAllLines(f); if (l.Length > 0) ia = l[0].Trim(); }
            }
            catch { }
            return ia;
        }
        internal static void Apply(Ui ui)
        {
            try
            {
                if (ui == null || ui.overlayUi == null) return;
                var v = ui.overlayUi.transform.Find("Version"); if (v == null) return;
                var tt = v.Find("VersionText"); if (tt == null) return;
                var text = tt.GetComponent<Text>(); if (text == null) return;
                string cur = text.text ?? "";
                if (cur.StartsWith("Iron Admiral")) return;
                string p = Package(); if (p.Length == 0) return;
                text.text = "Iron Admiral " + p + (cur.Length > 0 ? " | " + cur : "");
                if (!logged) { logged = true; MelonLogger.Msg("[DIP GG Fixes][UI] version text: " + text.text); }
            }
            catch { }
        }
    }

    // after TweaksAndFixes has set its text (its postfix on the same method)
    [HarmonyPatch(typeof(Ui), "RefreshVersion")]
    internal static class Patch_VersionLabel
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Ui __instance) { VersionLabel.Apply(__instance); }
    }
}
