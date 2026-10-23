// 1.11.143 (beta): port window under the fog of war - step 1, diagnostics. The game's port window (PortPopupUI.Show) lists the
// ships in a foreign port exactly. Before the list is hidden or blurred for poorly known ports, the window's structure is
// written to the log once per game start (object names and a sample of their texts) for the first two port windows opened.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class PortPopupFog
    {
        static bool logged; static int errors, shown;
        internal static void Shown(PortPopupUI ui, PortElement port)
        {
            if (logged || ui == null || port == null) return;
            try
            {
                if (++shown > 2) { logged = true; return; }
                string id = ""; try { id = port.Id; } catch { }
                count = 0;
                var sb = new StringBuilder();
                Walk(ui.transform, 0, sb);
                MelonLogger.Msg($"[DIP GG Fixes][Fog] port window ({id}) - structure for the fog of war:\n{sb}");
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Msg("[DIP GG Fixes][Fog] port window diagnostics: " + e.Message); }
        }
        static int count;
        static void Walk(Transform t, int depth, StringBuilder sb)
        {
            if (t == null || depth > 7 || count > 160) return;
            count++;
            string txt = ""; try { var tm = t.GetComponent<TMP_Text>(); if (tm != null && !string.IsNullOrEmpty(tm.text)) txt = " \"" + (tm.text.Length > 60 ? tm.text.Substring(0, 60) : tm.text).Replace("\n", " / ") + "\""; } catch { }
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (inactive)").Append(txt).Append('\n');
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }

    [HarmonyPatch]
    internal static class Patch_PortPopupFog
    {
        static MethodBase TargetMethod() { return SafePatches.Find(typeof(PortPopupUI), "Show", typeof(PortElement)); }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(PortPopupUI __instance, PortElement __0) { PortPopupFog.Shown(__instance, __0); }
    }
}
