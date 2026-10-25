// 1.11.261 (author 25.10, 14:35: "when I give an order the buttons close and I have to click again every time"): a click on a mod
// battle strip (division orders, torpedo setting, torpedo targets, gun orders) also reaches the game, which takes it as a click on
// the sea and drops the ship selection - and the strips, drawn for the selected ship, vanished. Now the ship that was selected when
// the player clicked a strip stays the strips' ship until the player selects another ship or clicks outside the strips.
using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class BattleSel
    {
        static Ship last;            // the player's ship the strips were last drawn for
        static bool keep;            // the selection was lost by a click on a strip
        static int clickFrame = -100;
        static readonly List<Rect> rects = new List<Rect>(), prev = new List<Rect>();
        static int rectFrame = -1;

        // each strip reports its box every frame (panel coordinates, UiScale)
        internal static void Panel(Rect r)
        {
            int f = Time.frameCount;
            if (f != rectFrame) { prev.Clear(); prev.AddRange(rects); rects.Clear(); rectFrame = f; }
            rects.Add(r);
        }
        static bool Over(Vector2 p)
        {
            foreach (var r in rects) if (r.Contains(p)) return true;
            foreach (var r in prev) if (r.Contains(p)) return true;
            return false;
        }
        // from OnUpdate: a mouse press on a strip keeps the strips' ship; a press elsewhere lets the game's selection rule again
        internal static void Tick()
        {
            try
            {
                if (!GameManager.IsBattle) { last = null; keep = false; return; }
                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                {
                    if (Over(UiScale.Mouse())) { keep = true; clickFrame = Time.frameCount; }
                    else keep = false;
                }
            }
            catch { }
        }
        static bool Ok(Ship s) { try { return s != null && s.player != null && s.player.isMain && !s.isDead && !s.isSinking && !s.IsSurrendered; } catch { return false; } }
        // the player's ship the strips are drawn for (null = none)
        internal static Ship Ship()
        {
            Ship s = null; try { var ui = G.ui; s = ui != null ? ui.selectedShipMain : null; } catch { }
            if (s != null && Ok(s)) { last = s; return s; }
            if (keep && Ok(last)) return last;
            if (s == null && Time.frameCount - clickFrame < 3 && Ok(last)) { keep = true; return last; }
            return null;
        }
    }
}
