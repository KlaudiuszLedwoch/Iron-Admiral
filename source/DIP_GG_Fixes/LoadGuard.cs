// 1.11.509/510 (#122): an older campaign save hung while loading - a saved minor alliance named a nation the game no longer knows
// (test campaign: russia - chile) and MinorAlliance.FromStore stopped the whole load with KeyNotFoundException. Returning null from
// FromStore is not enough (the loader uses the result at once - 1.11.509 test: NullReferenceException in LoadCampaign). Before every
// step of the loading coroutine the saved data it holds is searched for lists of saved minor alliances, and an alliance whose
// major or minor nation is unknown is removed from the list before the loader reads it, with a line in the log.
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
        internal static void ApplyHooks(HarmonyLib.Harmony h)
        {
            try
            {
                int n = 0;
                foreach (var t in typeof(GameManager).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!t.Name.Contains("LoadCampaign")) continue;
                    var m = t.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) continue;
                    h.Patch(m, prefix: new HarmonyMethod(typeof(LoadGuard).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic))); n++;
                }
                MelonLogger.Msg("[DIP GG Fixes][LoadGuard] campaign loading guarded (" + n + " method(s))");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] " + e.Message); }
        }

        static bool Known(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            try { return G.GameData.players.ContainsKey(key); } catch { return true; }
        }

        static readonly HashSet<IntPtr> seen = new HashSet<IntPtr>();
        static int Clean(Il2CppSystem.Collections.Generic.List<MinorAlliance.Store> list)
        {
            if (list == null || seen.Contains(list.Pointer)) return 0;
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list[i]; string a = null, b = null;
                try { if (s != null) { a = s.Major; b = s.Minor; } } catch { }
                if (s != null && Known(a) && Known(b)) continue;
                list.RemoveAt(i); removed++;
                MelonLogger.Msg($"[DIP GG Fixes][LoadGuard] saved minor alliance {a ?? "?"} - {b ?? "?"} dropped: {(Known(a) ? b : a) ?? "?"} is not a nation of this game");
            }
            seen.Add(list.Pointer);
            return removed;
        }

        // the loader's own fields and one level below them (the saved campaign object) are searched for List<MinorAlliance.Store>
        static void Pre(object __instance) { try { Scan(__instance, 3); } catch { } }
        // only the game's own store classes are followed (Il2Cpp namespace, names ending in "Store"/"Data"/"Save" or the loader itself)
        static void Scan(object o, int depth)
        {
            if (o == null || depth < 0) return;
            foreach (var p in o.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                var pt = p.PropertyType;
                if (pt == typeof(Il2CppSystem.Collections.Generic.List<MinorAlliance.Store>))
                {
                    object w = null; try { w = p.GetValue(o); } catch { }
                    Clean(w as Il2CppSystem.Collections.Generic.List<MinorAlliance.Store>); continue;
                }
                if (depth == 0 || pt.IsValueType || pt == typeof(string)) continue;
                string n = pt.Name;
                if (pt.Namespace == null || !pt.Namespace.StartsWith("Il2Cpp") || pt.Namespace.StartsWith("Il2CppSystem")) continue;
                if (!(n.EndsWith("Store") || n.EndsWith("Data") || n.EndsWith("Save") || n.Contains("Campaign"))) continue;
                object v = null; try { v = p.GetValue(o); } catch { }
                Scan(v, depth - 1);
            }
        }

        internal static void Tick() { }
    }
}
