// 1.11.509 (#122): an older campaign save hung while loading - a saved minor alliance named a nation the game no longer knows and
// MinorAlliance.FromStore stopped the whole load with KeyNotFoundException. Such an alliance is now skipped with a line in the log
// (FromStore returns null), and every null left in the campaign's lists of minor alliances is removed once the map is shown.
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace DipGgFixes
{
    internal static class LoadGuard
    {
        static int skipped; static bool cleaned; static IntPtr session;

        internal static void ApplyHooks(HarmonyLib.Harmony h)
        {
            try
            {
                var m = AccessTools.Method(typeof(MinorAlliance), "FromStore");
                if (m == null) { MelonLogger.Msg("[DIP GG Fixes][LoadGuard] MinorAlliance.FromStore not found"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(LoadGuard).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic)),
                    finalizer: new HarmonyMethod(typeof(LoadGuard).GetMethod(nameof(Fin), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] " + e.Message); }
        }

        static bool Known(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            try { return G.GameData.players.ContainsKey(key); } catch { return true; }
        }

        static bool Pre(MinorAlliance.Store store, ref MinorAlliance __result)
        {
            try
            {
                if (store == null) { __result = null; skipped++; cleaned = false; return false; }
                string a = store.Major, b = store.Minor;
                if (Known(a) && Known(b)) return true;
                __result = null; skipped++; cleaned = false;
                MelonLogger.Msg($"[DIP GG Fixes][LoadGuard] saved minor alliance {a ?? "?"} - {b ?? "?"} skipped: {(Known(a) ? b : a) ?? "?"} is not a nation of this game");
                return false;
            }
            catch { return true; }
        }

        static Exception Fin(Exception __exception, ref MinorAlliance __result)
        {
            if (__exception == null) return null;
            skipped++; cleaned = false; __result = null;
            MelonLogger.Msg("[DIP GG Fixes][LoadGuard] a saved minor alliance could not be read (" + __exception.GetType().Name + ") - skipped");
            return null;
        }

        // removes the skipped (null) alliances from every list of minor alliances the campaign keeps
        internal static void Tick()
        {
            if (skipped == 0 || cleaned) return;
            bool map = false; try { map = GameManager.IsWorldMap; } catch { }
            if (!map) return;
            cleaned = true;
            int removed = 0;
            try
            {
                var cc = CampaignController.Instance; if (cc == null) return;
                foreach (var host in new object[] { cc, cc.CampaignData })
                {
                    if (host == null) continue;
                    foreach (var p in host.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (p.Name.IndexOf("inorAlliances", StringComparison.Ordinal) < 0 || p.GetIndexParameters().Length > 0) continue;
                        object v = null; try { v = p.GetValue(host); } catch { }
                        if (v is Il2CppSystem.Collections.Generic.List<MinorAlliance> list)
                            for (int i = list.Count - 1; i >= 0; i--) if (list[i] == null) { list.RemoveAt(i); removed++; }
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] cleanup: " + e.Message); }
            MelonLogger.Msg($"[DIP GG Fixes][LoadGuard] {skipped} saved minor alliance(s) skipped while loading, {removed} empty entr(ies) removed");
            skipped = 0;
        }
    }
}
