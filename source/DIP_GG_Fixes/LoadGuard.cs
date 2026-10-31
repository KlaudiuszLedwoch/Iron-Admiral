// 1.11.509-511 (#122): an older campaign save hung while loading - a saved minor alliance named a nation the game no longer knows
// (test campaign: russia - chile) and MinorAlliance.FromStore stopped the whole load with KeyNotFoundException. A null result
// stops the loader too (1.11.509), and the saved list is read in the same loader step, so it cannot be cleaned beforehand (1.11.510).
// 1.11.511: the unknown nation of such an alliance is replaced by the known one (an alliance of a nation with itself), so the load
// runs to the end; once the map is shown every alliance of a nation with itself is removed from the campaign's alliance lists.
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
        static int pending; static bool cleaned = true;

        internal static void ApplyHooks(HarmonyLib.Harmony h)
        {
            try
            {
                var m = AccessTools.Method(typeof(MinorAlliance), "FromStore");
                if (m == null) { MelonLogger.Msg("[DIP GG Fixes][LoadGuard] MinorAlliance.FromStore not found"); return; }
                h.Patch(m, prefix: new HarmonyMethod(typeof(LoadGuard).GetMethod(nameof(Pre), BindingFlags.Static | BindingFlags.NonPublic)));
                MelonLogger.Msg("[DIP GG Fixes][LoadGuard] saved minor alliances checked while loading");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] " + e.Message); }
        }

        static bool Known(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            try { return G.GameData.players.ContainsKey(key); } catch { return true; }
        }

        static void Pre(MinorAlliance.Store store)
        {
            try
            {
                if (store == null) return;
                string a = store.Major, b = store.Minor;
                bool ka = Known(a), kb = Known(b);
                if (ka && kb) return;
                string sub = ka ? a : kb ? b : null;
                if (sub == null) { foreach (var kv in G.GameData.players) { sub = kv.Key; break; } }
                if (sub == null) return;
                store.Major = sub; store.Minor = sub;
                pending++; cleaned = false;
                MelonLogger.Msg($"[DIP GG Fixes][LoadGuard] saved minor alliance {a ?? "?"} - {b ?? "?"}: {(ka ? b : a) ?? "?"} is not a nation of this game - the alliance is dropped after loading");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] check: " + e.Message); }
        }

        static bool SelfAlliance(MinorAlliance ma)
        {
            if (ma == null) return true;
            try { return ma.MajorPlayer == null || ma.MinorPlayer == null || ma.MajorPlayer.Pointer == ma.MinorPlayer.Pointer; } catch { return false; }
        }

        static int CleanList(object v)
        {
            int r = 0;
            if (v is Il2CppSystem.Collections.Generic.List<MinorAlliance> list)
                for (int i = list.Count - 1; i >= 0; i--) if (SelfAlliance(list[i])) { list.RemoveAt(i); r++; }
            return r;
        }

        // the campaign's lists of minor alliances (and dictionaries of such lists), found by name on the controller and its data
        internal static void Tick()
        {
            if (cleaned) return;
            bool map = false; try { map = GameManager.IsWorldMap; } catch { }
            if (!map) return;
            cleaned = true;
            int removed = 0; var where = new List<string>();
            try
            {
                var cc = CampaignController.Instance; if (cc == null) return;
                object data = null; try { data = cc.CampaignData; } catch { }
                foreach (var host in new object[] { cc, data })
                {
                    if (host == null) continue;
                    foreach (var p in host.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (p.Name.IndexOf("inorAlliance", StringComparison.Ordinal) < 0 || p.GetIndexParameters().Length > 0) continue;
                        object v = null; try { v = p.GetValue(host); } catch { }
                        if (v == null) continue;
                        int r = CleanList(v);
                        // a dictionary of lists: every value is cleaned
                        if (r == 0)
                        {
                            var values = v.GetType().GetProperty("Values"); object vals = null; try { vals = values?.GetValue(v); } catch { }
                            if (vals != null)
                            {
                                var ge = vals.GetType().GetMethod("GetEnumerator"); object en = null; try { en = ge?.Invoke(vals, null); } catch { }
                                var mn = en?.GetType().GetMethod("MoveNext"); var cur = en?.GetType().GetProperty("Current");
                                while (mn != null && cur != null && (bool)mn.Invoke(en, null)) r += CleanList(cur.GetValue(en));
                            }
                        }
                        if (r > 0) { removed += r; where.Add(p.Name + " " + r); }
                    }
                }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][LoadGuard] cleanup: " + e.Message); }
            MelonLogger.Msg($"[DIP GG Fixes][LoadGuard] {pending} saved minor alliance(s) with an unknown nation: {removed} entr(ies) removed ({(where.Count > 0 ? string.Join(", ", where) : "none found")})");
            pending = 0;
        }
    }
}
