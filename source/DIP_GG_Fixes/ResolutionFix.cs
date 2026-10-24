// 1.11.187: when the game's saved resolution index is invalid (e.g. -1 after a display change), TweaksAndFixes reads
// G.settings.filterdResolution[G.settings.resolution] every frame and throws (about 60 exceptions per second, a log of hundreds of
// thousands of lines and lost frame rate). The index is set to the entry of the current screen resolution.
using System;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ResolutionFix
    {
        static float next; static int done;
        internal static void Tick()
        {
            if (done > 3 || UnityEngine.Time.unscaledTime < next) return; next = UnityEngine.Time.unscaledTime + 5f;
            try
            {
                var st = G.settings; if (st == null) return;
                var list = st.filterdResolution; if (list == null || list.Count == 0) return;
                int idx = st.resolution;
                if (idx >= 0 && idx < list.Count) return;
                var cur = UnityEngine.Screen.currentResolution; int best = list.Count - 1;
                for (int i = 0; i < list.Count; i++) { var r = list[i]; if (r.width == cur.width && r.height == cur.height) { best = i; break; } }
                st.resolution = best; done++;
                MelonLogger.Msg($"[DIP GG Fixes][Settings] invalid resolution index {idx} (of {list.Count}) set to {best} ({list[best].width}x{list[best].height}) - stops the per-frame TweaksAndFixes error");
            }
            catch (Exception e) { done++; MelonLogger.Msg("[DIP GG Fixes][Settings] resolution fix: " + e.Message); }
        }
    }
}
