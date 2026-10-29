// 1.11.357 (author 28.10, 20:20-20:21, screenshot at 3840x2160, TweaksAndFixes UI scale 2.51: "the fonts in the designer are too large -
// only in the designer"): the designer's part window (the right-hand panel with the gun or part: Mark, weight, ammo weight, cost,
// crew, ranges, damage, reload) grows with the game's UI scale while its box does not, so at a large UI scale the text fills the
// screen edge. Its text is drawn smaller by DesignerPartTextScale (0.7 = 70 %; 1 = the game's size). Only the designer's part
// window is changed; the original size of every text is kept, so the scale is never applied twice.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace DipGgFixes
{
    internal static class DesignerText
    {
        internal static MelonPreferences_Entry<float> Scale;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Scale = cat.CreateEntry("DesignerPartTextScale", 0.7f, null, "Display: size of the text in the designer's part window (the right-hand panel with the selected gun or part) relative to the game's size - 0.7 = 70 %, 1 = unchanged; it grows with the game's UI scale and fills the screen edge at large scales");
        }
        static readonly Dictionary<IntPtr, float> orig = new Dictionary<IntPtr, float>();
        static float next, applied = -1f; static int logged, errors;
        internal static void Tick()
        {
            if (Scale == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            try
            {
                bool cons = false; try { cons = GameManager.IsConstructor; } catch { }
                if (!cons) return;
                var ui = G.ui; if (ui == null) return;
                GameObject root = null; try { root = ui.partInfo; } catch { }
                if (root == null || !root.activeInHierarchy) return;
                float k = Mathf.Clamp(Scale.Value, 0.4f, 1.5f);
                var texts = root.GetComponentsInChildren<TMP_Text>(true);
                int n = 0;
                for (int i = 0; i < texts.Length; i++)
                {
                    var t = texts[i]; if (t == null) continue;
                    float o; if (!orig.TryGetValue(t.Pointer, out o)) { o = t.enableAutoSizing ? t.fontSizeMax : t.fontSize; orig[t.Pointer] = o; }
                    float want = o * k;
                    if (t.enableAutoSizing) { if (Math.Abs(t.fontSizeMax - want) > 0.05f) { t.fontSizeMax = want; n++; } }
                    else if (Math.Abs(t.fontSize - want) > 0.05f) { t.fontSize = want; n++; }
                }
                if (orig.Count > 2000) orig.Clear();
                if (n > 0 && (logged++ < 3 || Math.Abs(applied - k) > 0.001f)) MelonLogger.Msg($"[DIP GG Fixes][UI] designer part window: {n} text(s) drawn at {k:P0} of the game's size");
                applied = k;
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][UI] designer part window text: " + e.Message); }
        }

        // 1.11.396 (author 29.10 18:48-18:51, screenshot of the refit designer at 3840x2160: the part window's text still fills the
        // right edge - "Weight: 617 t" in letters a third of the panel wide): the window's text comes from Ui.GetPartInfoText with
        // its own <size=..> tags, which override the font size set above, so the 1.11.357 change had no effect (nothing in the log).
        // The text itself is scaled now: every absolute <size=N> tag becomes N x DesignerPartTextScale and the whole text is
        // wrapped in <size=..%>, in the designer only.
        static int textLogged;
        [HarmonyLib.HarmonyPatch(typeof(Ui), nameof(Ui.GetPartInfoText))]
        static class Patch_PartInfoText
        {
            static void Postfix(ref string __result)
            {
                try
                {
                    if (Scale == null || string.IsNullOrEmpty(__result)) return;
                    bool cons = false; try { cons = GameManager.IsConstructor; } catch { }
                    if (!cons) return;
                    float k = Mathf.Clamp(Scale.Value, 0.4f, 1.5f); if (Math.Abs(k - 1f) < 0.01f) return;
                    int tags = 0; var ci = System.Globalization.CultureInfo.InvariantCulture;
                    var sb = new System.Text.StringBuilder(__result.Length + 32); string src = __result; int pos = 0;
                    while (true)
                    {
                        int a = src.IndexOf("<size=", pos, StringComparison.OrdinalIgnoreCase);
                        if (a < 0) { sb.Append(src, pos, src.Length - pos); break; }
                        int b = src.IndexOf('>', a); if (b < 0) { sb.Append(src, pos, src.Length - pos); break; }
                        string val = src.Substring(a + 6, b - a - 6).Trim(); if (val.EndsWith("px", StringComparison.OrdinalIgnoreCase)) val = val.Substring(0, val.Length - 2);
                        sb.Append(src, pos, a - pos);
                        if (float.TryParse(val, System.Globalization.NumberStyles.Float, ci, out float v)) { sb.Append("<size=").Append((v * k).ToString("0.#", ci)).Append('>'); tags++; }
                        else sb.Append(src, a, b - a + 1);   // % or em: relative, follows the wrapper
                        pos = b + 1;
                    }
                    string s = sb.ToString();
                    __result = "<size=" + (k * 100f).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%>" + s + "</size>";
                    if (textLogged++ < 2) MelonLogger.Msg($"[DIP GG Fixes][UI] designer part text drawn at {k:P0} ({tags} size tag(s) scaled)");
                }
                catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][UI] designer part text: " + e.Message); }
            }
        }
    }
}
