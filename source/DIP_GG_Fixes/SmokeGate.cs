// 1.11.341 (#22, player suggestion "smoke only when needed"): the game's own AI lays a smoke screen whenever it can, also far
// outside the range of any enemy gun, so the smoke is gone when it is needed and hides the AI's own targets. A smoke screen
// that the game AI asks for is now laid only when a visible enemy has the ship within its gun range (at most 15 km). Smoke laid
// by the mod's battle AI (turn-away after a torpedo attack, covering a retreat) and by the player is not affected.
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class SmokeGate
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static bool ModCall;   // set by BattleAi.TrySmoke around its own LaunchSmokeScreen calls
        static int allowed, blocked;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiSmokeOnlyWhenNeeded", true, null, "Battle AI: the AI lays a smoke screen only when a visible enemy has the ship within its gun range, not as soon as it can (the mod's own smoke for torpedo turn-aways and covering retreats is not affected)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Smoke] " + m); }

        // true = the threat that justifies smoke; dist = nearest visible enemy in whose gun range the ship is (or nearest at all)
        static bool Threatened(Ship s, out float dist, out string who)
        {
            dist = float.MaxValue; who = "-";
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (b == null || s.player == null) return true;
            var en = b.ShipsOf(b.OppositePlayer(s.player), true); if (en == null) return true;
            bool hit = false;
            for (int i = 0; i < en.Count; i++)
            {
                var e = en[i];
                if (e == null || e.isSinking || e.isDead || e.IsSurrendered) continue;
                bool vis = false; try { vis = e.IsVisibleFor(s); } catch { }
                if (!vis) continue;
                float d = BattleAi.Dist(s, e);
                float gr = BattleAi.GunRange(e);
                bool inRange = d <= 15000f && d <= gr;
                if (inRange && (!hit || d < dist)) { hit = true; dist = d; who = e.name; }
                else if (!hit && d < dist) { dist = d; who = e.name; }
            }
            return hit;
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.LaunchSmokeScreen))]
        static class Patch_Launch
        {
            static bool Prefix(Ship __instance)
            {
                try
                {
                    if (On == null || !On.Value || ModCall || __instance == null) return true;
                    var p = __instance.player; if (p == null || p.isMain) return true;
                    bool ok = Threatened(__instance, out float d, out string who);
                    if (ok) allowed++; else blocked++;
                    int n = ok ? allowed : blocked;
                    if (n <= 5 || n % 20 == 0)
                        Log($"{__instance.name}: AI smoke {(ok ? "laid" : "withheld")} - {(d < float.MaxValue ? $"nearest visible enemy {who} at {d / 1000f:0.0} km" : "no visible enemy")}{(ok ? " (in its gun range)" : "")} [laid {allowed}, withheld {blocked}]");
                    return ok;
                }
                catch { return true; }
            }
        }
    }
}
