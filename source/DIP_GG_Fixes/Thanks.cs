// 1.11.370 (author 29.10: "the thanks should be part of the mod - these people are named regularly"). The players who test, report
// and record for Iron Admiral are listed in Mods\IronAdmiral_THANKS.txt (name | reports | what they did; ordered by the number and
// length of their reports, updated with the forum reviews). The list is written to the log at start and shown as one line at the
// bottom of the main menu. Switch: ThanksOnMainMenu.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Thanks
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ThanksOnMainMenu", true, null, "The main menu names the players who test, report and record for Iron Admiral (Mods\\IronAdmiral_THANKS.txt)");
        }
        static List<string> names; static string line; static GUIStyle st;
        static void Load()
        {
            if (names != null) return;
            names = new List<string>();
            try
            {
                var f = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiral_THANKS.txt");
                if (File.Exists(f))
                    foreach (var raw in File.ReadAllLines(f))
                    {
                        var l = raw.Trim(); if (l.Length == 0 || l.StartsWith("#")) continue;
                        var p = l.Split('|'); var n = p[0].Trim(); if (n.Length > 0) names.Add(n);
                    }
            }
            catch { }
            line = names.Count > 0 ? "Thanks to the players who test and report: " + string.Join(", ", names) : null;
            if (line != null) MelonLogger.Msg("[DIP GG Fixes][Thanks] " + line);
        }
        internal static void OnGUI()
        {
            try
            {
                if (On == null || !On.Value) return;
                bool menu = false; try { menu = GameManager.IsMainMenu; } catch { }
                if (!menu) return;
                Load(); if (line == null) return;
                if (st == null) { st = new GUIStyle(); st.fontSize = 12; st.wordWrap = true; st.normal.textColor = new Color(0.85f, 0.85f, 0.75f, 0.9f); }
                float w = Math.Min(900f, UiScale.W - 40f);
                GUI.Label(new Rect(16f, UiScale.H - 30f, w, 22f), line, st);
            }
            catch { }
        }
    }
}
