// 1.11.100: the mod's own panels (F7 intelligence, F8 treasury, F10 settings, trade, battle report, dispatches, decisions,
// diagnostics) follow the game's UI scale. They were drawn in raw screen pixels and looked tiny on large screens.
// The scale is the scale factor of the game's own UI canvas (it already contains the player's UI scale and the resolution),
// times the ModUiScale setting; the panels are laid out in "virtual" pixels W x H and GUI.matrix scales them up.
using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class UiScale
    {
        internal static MelonPreferences_Entry<bool> Auto;
        internal static MelonPreferences_Entry<float> Mult;
        internal static float F = 1f;
        static float next; static bool logged; static float lastLogged = -1f;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Auto = cat.CreateEntry("ModUiScaleAuto", true, null, "The mod's panels follow the game's UI scale (scale factor of the game's UI canvas)");
            Mult = cat.CreateEntry("ModUiScale", 1f, null, "Extra size multiplier for the mod's panels (1 = same as the game's UI; e.g. 1.25 = 25% larger)");
        }

        internal static float W { get { return Screen.width / F; } }
        internal static float H { get { return Screen.height / F; } }
        // mouse position in the panels' coordinates (top-left origin)
        internal static Vector2 Mouse()
        {
            var mp = Input.mousePosition;
            return new Vector2(mp.x / F, (Screen.height - mp.y) / F);
        }

        static float GameScale()
        {
            try
            {
                var ui = G.ui;
                if (ui != null)
                {
                    var c = ui.GetComponentInParent<Canvas>();
                    if (c == null) c = ui.GetComponentInChildren<Canvas>();
                    if (c != null) { var root = c.rootCanvas != null ? c.rootCanvas : c; float s = root.scaleFactor; if (s > 0.1f) return s; }
                }
            }
            catch { }
            return Screen.height / 1080f; // fallback: the game's UI is laid out for 1080p
        }

        static void Update()
        {
            float now = Time.unscaledTime;
            if (now < next) return; next = now + 2f;
            float s = (Auto == null || Auto.Value) ? GameScale() : 1f;
            float m = Mult != null ? Mult.Value : 1f; if (m < 0.5f) m = 0.5f; if (m > 3f) m = 3f;
            F = Mathf.Clamp(s * m, 0.5f, 4f);
            if (!logged || Mathf.Abs(F - lastLogged) > 0.05f) { logged = true; lastLogged = F; MelonLogger.Msg($"[DIP GG Fixes][UI] panel scale {F:0.00} (game UI {s:0.00} x setting {m:0.00}, screen {Screen.width}x{Screen.height})"); }
        }

        internal static void Begin() { Update(); GUI.matrix = Matrix4x4.Scale(new Vector3(F, F, 1f)); }
        internal static void End() { GUI.matrix = Matrix4x4.identity; }
    }
}
