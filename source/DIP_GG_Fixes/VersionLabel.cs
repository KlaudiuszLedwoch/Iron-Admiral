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
        static string ia; static bool logged, failed;
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
        // TweaksAndFixes rewrites the text whenever the game data is (re)loaded, not only in Ui.RefreshVersion: check every 2 s
        static float next;
        internal static void Tick()
        {
            if (UnityEngine.Time.realtimeSinceStartup < next) return; next = UnityEngine.Time.realtimeSinceStartup + 2f;
            try { Apply(G.ui); } catch { }
        }
        internal static void Apply(Ui ui)
        {
            try
            {
                if (ui == null || ui.overlayUi == null) return;
                var v = Deep(ui.overlayUi.transform, "Version"); if (v == null) { Fail("no Version object"); return; }
                var tt = Deep(v, "VersionText"); if (tt == null) { Fail("no VersionText object"); return; }
                var text = tt.GetComponent<Text>(); if (text == null) { Fail("no Text component"); return; }
                string cur = text.text ?? "";
                if (cur.StartsWith("Iron Admiral")) return;
                string p = Package(); if (p.Length == 0) return;
                text.text = "Iron Admiral " + p + (cur.Length > 0 ? " | " + cur : "");
                if (!logged) { logged = true; MelonLogger.Msg("[DIP GG Fixes][UI] version text: " + text.text); }
            }
            catch (Exception e) { Fail(e.Message); }
        }
        static void Fail(string m) { if (failed) return; failed = true; MelonLogger.Msg("[DIP GG Fixes][UI] version text not set: " + m); }
        static UnityEngine.Transform Deep(UnityEngine.Transform t, string name)
        {
            if (t == null) return null;
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c.name == name) return c; var d = Deep(c, name); if (d != null) return d; }
            return null;
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
