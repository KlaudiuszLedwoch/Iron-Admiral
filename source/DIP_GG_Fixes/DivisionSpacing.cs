// 1.11.231 (author's decision 24.10, after BABUT's report): AI ships must not lie side by side. Two ships a hull apart take almost
// every shell meant for either of them - the salvo spread that misses one hits the other - and the AI bunched its divisions
// exactly like that in turns and when divisions closed on each other. The game keeps its own collision avoidance and a
// "space between divisions" value per division; for AI divisions both are set here: collision avoidance on, the division
// spacing multiplied by AiDivisionSpacing (and the formation spread by AiDivisionSpread). Applied once per division per battle,
// logged with the values found, so that the game's own numbers are known before they are tuned. Switch: AiDivisionSpacing.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DivisionSpacing
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> SpaceMult;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiDivisionSpacing", true, null, "Battle: AI divisions keep their distance from each other (collision avoidance on, wider spacing between divisions) - ships side by side take every shell meant for either");
            SpaceMult = cat.CreateEntry("AiDivisionSpacingMult", 1.5f, null, "Multiplier of the game's spacing between AI divisions");
        }
        static readonly HashSet<IntPtr> done = new HashSet<IntPtr>();
        static float next; static int errors, logged; static bool inBattle;

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (!b) { if (inBattle) { inBattle = false; done.Clear(); logged = 0; } return; }
            inBattle = true;
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 5f;
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                var ships = battle.Ships; if (ships == null) return;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    Division d = null; try { d = s.division; } catch { }
                    if (d == null || done.Contains(d.Pointer)) continue;
                    bool ai = false; try { ai = s.player != null && !s.player.isMain; } catch { }
                    if (!ai) { done.Add(d.Pointer); continue; }
                    done.Add(d.Pointer);
                    float space = 0f; string spread = "?"; bool avoid = false; string form = "?";
                    try { space = d.spaceBetweenDiv; } catch { }
                    try { spread = d.spread.ToString(); } catch { }
                    try { avoid = d.avoidCollisions; } catch { }
                    try { form = d.formation.ToString(); } catch { }
                    float sm = SpaceMult != null ? Mathf.Clamp(SpaceMult.Value, 0.5f, 4f) : 1.5f;
                    try { d.avoidCollisions = true; } catch { }
                    try { if (space > 0f) d.spaceBetweenDiv = space * sm; } catch { }
                    if (logged++ < 12) MelonLogger.Msg($"[DIP GG Fixes][Spacing] {s.player.data.name} division of {s.name}: formation {form}, space between divisions {space:0} -> {space * sm:0}, spread {spread}, collision avoidance {avoid} -> on");
                }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Spacing] " + e.Message); }
        }
    }
}
