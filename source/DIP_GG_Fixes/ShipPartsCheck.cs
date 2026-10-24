// 1.11.217 (author, 24.10): ships in battle whose parts have no mount or no model. In the 24.10 battle the British CA Cochrane
// (AI design "Black Prince", hull ca_3_warrior) appeared as a bare hull - no towers, funnels or turrets - while its guns fired;
// the game logged "missing required mount: torpedo_x0, ca_3_warrior" four times (Part.TryFindMount from Part.ModelLoadedOrReused):
// the design carries deck torpedo tubes at positions the hull's mount table (Mods\mounts.csv, DIP 1.3.5/TAF) does not have.
// This check lists, 15 s after a battle starts, every ship with parts that have no mount or no model, so that the cause of the
// bare hulls can be read from the log; the generator's kept designs are refused when any part has no mount (ShipGenSpeed).
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ShipPartsCheck
    {
        static bool inBattle, done; static float at; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Parts] " + m); }

        internal static void Tick()
        {
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (b && !inBattle) { inBattle = true; done = false; at = Time.unscaledTime + 15f; }
            else if (!b && inBattle) { inBattle = false; return; }
            if (!inBattle || done || Time.unscaledTime < at) return;
            done = true;
            try
            {
                var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                var ships = battle.Ships; if (ships == null) return;
                int shipsBad = 0;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    string r = Report(s);
                    if (r != null) { shipsBad++; Log(r); }
                }
                Log($"battle check: {ships.Count} ships, {shipsBad} with parts that have no mount or no model");
            }
            catch (Exception e) { if (errors++ < 3) Log("check: " + e.Message); }
        }

        // null when every part is mounted and modelled
        internal static string Report(Ship s)
        {
            try
            {
                var parts = s.parts; if (parts == null) return null;
                var noMount = new List<string>(); var noModel = new List<string>(); int n = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; if (p == null || p.data == null) continue; n++;
                    bool hull = false; try { hull = p.data.isHull; } catch { }
                    if (hull) continue;
                    string nm = p.data.name;
                    bool m = false, mdl = false;
                    try { m = p.mount != null; } catch { }
                    try { mdl = p.model != null; } catch { }
                    if (!m) noMount.Add(nm + (mdl ? "" : " (no model)")); else if (!mdl) noModel.Add(nm);
                }
                if (noMount.Count == 0 && noModel.Count == 0) return null;
                string hullName = "?"; try { hullName = s.hull != null && s.hull.data != null ? s.hull.data.name : "?"; } catch { }
                return $"{s.name} ({s.design?.name}) hull {hullName}: {n} parts; no mount: {(noMount.Count > 0 ? string.Join(", ", noMount) : "none")}; mounted but no model: {(noModel.Count > 0 ? string.Join(", ", noModel) : "none")}";
            }
            catch (Exception e) { if (errors++ < 3) Log("report: " + e.Message); return null; }
        }

        // 1.11.218: Part.mount is also null for many parts of ships that look right (24.10 log: 8 of 60 parts on a German CA, 25 of 58
        //           on a CL), so a design is refused only when every part outside the hull is unmounted - the bare-hull pattern
        //           (CA Black Prince: 17 of 17).
        internal static bool AllMounted(Ship s)
        {
            try
            {
                var parts = s.parts; if (parts == null) return true;
                int n = 0, un = 0;
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; if (p == null || p.data == null) continue;
                    bool hull = false; try { hull = p.data.isHull; } catch { }
                    if (hull) continue; n++;
                    try { if (p.mount == null) un++; } catch { }
                }
                return n == 0 || un < n;
            }
            catch { return true; }
        }
    }
}