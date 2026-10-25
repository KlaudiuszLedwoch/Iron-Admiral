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
            else if (!b && inBattle) { inBattle = false; ClearStores(); return; }
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
                    if (!m)
                    {
                        // 1.11.221: where is the unmounted part? (24.10: shells of the bare-hull cruiser started 990 m above the target)
                        string pos = ""; try { var w = p.transform.position; var sp = s.transform.position; pos = $" at y {w.y:0} (ship y {sp.y:0}, {(w - sp).magnitude:0} m from the ship)"; } catch { }
                        bool re = false; try { p.TryFindMount(true); re = p.mount != null; } catch { }
                        string fix = re ? " - re-mounted" : Repair(s, p);   // 1.11.243
                        noMount.Add(nm + (mdl ? "" : " (no model)") + pos + fix);
                    }
                    else if (!mdl) noModel.Add(nm);
                }
                if (noMount.Count == 0 && noModel.Count == 0) return null;
                string hullName = "?"; try { hullName = s.hull != null && s.hull.data != null ? s.hull.data.name : "?"; } catch { }
                return $"{s.name} ({s.design?.name}) hull {hullName}: {n} parts; no mount: {(noMount.Count > 0 ? string.Join(", ", noMount) : "none")}; mounted but no model: {(noModel.Count > 0 ? string.Join(", ", noModel) : "none")}";
            }
            catch (Exception e) { if (errors++ < 3) Log("report: " + e.Message); return null; }
        }

        // 1.11.243 (author's report 24.10, second bare hull - CA Bedford on ca_3_warrior: tower, funnels, 10" and 7" guns 992 m BELOW the
        //           ship, firing upwards from under the sea): a part with no mount that lies far from its ship is put back where the
        //           design has it - the design store keeps every part's position and rotation relative to the hull. Visual and firing
        //           position only; the hull's mount table is not touched. Switch: RepairUnmountedParts (F10, Battle).
        internal static MelonPreferences_Entry<bool> RepairOn;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            RepairOn = cat.CreateEntry("RepairUnmountedParts", true, null, "Battle: a part that found no mount on its hull and lies far from the ship (bare hulls, guns firing from under the sea) is put back at its design position");
        }
        static readonly Dictionary<IntPtr, Dictionary<string, Part.Store>> stores = new Dictionary<IntPtr, Dictionary<string, Part.Store>>();
        static string Repair(Ship s, Part p)
        {
            try
            {
                if (RepairOn != null && !RepairOn.Value) return "";
                var w = p.transform.position; var sp = s.transform.position;
                if ((w - sp).magnitude < 150f) return "";
                Dictionary<string, Part.Store> map;
                if (!stores.TryGetValue(s.Pointer, out map))
                {
                    map = new Dictionary<string, Part.Store>();
                    // the design's store first (its parts stand where the designer put them); the battle ship's own store as a fallback,
                    // where only records near the hull are trusted
                    Ship d = null; try { d = s.design; } catch { }
                    foreach (var src in new[] { d, s })
                    {
                        if (src == null) continue;
                        try { var st = src.ToStore(false); if (st != null && st.parts != null) for (int i = 0; i < st.parts.Count; i++) { var ps = st.parts[i]; if (ps == null || ps.position.magnitude > 150f) continue; string id = ps.Id.ToString(); if (!map.ContainsKey(id)) map[id] = ps; } } catch { }
                        if (map.Count > 0) break;
                    }
                    stores[s.Pointer] = map;
                }
                Part.Store rec; if (!map.TryGetValue(p.Id.ToString(), out rec))
                {
                    // ids differ between the design and the ship built from it: the next unused record of the same part name
                    string nm = p.data != null ? p.data.name : null; rec = null;
                    foreach (var kv in map) { var ps = kv.Value; if (ps != null && ps.name == nm && !used.Contains(ps.Pointer)) { rec = ps; break; } }
                    if (rec == null) return " - no design record";
                }
                used.Add(rec.Pointer);
                var t = p.transform;
                if (t.parent != s.transform) t.SetParent(s.transform, false);
                t.localPosition = rec.position;
                try { t.localRotation = rec.rotation; } catch { }
                return $" - put back at its design position ({rec.position.x:0.#}, {rec.position.y:0.#}, {rec.position.z:0.#})";
            }
            catch (Exception e) { return " - repair failed: " + e.Message; }
        }
        static readonly HashSet<IntPtr> used = new HashSet<IntPtr>();
        internal static void ClearStores() { stores.Clear(); used.Clear(); }

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