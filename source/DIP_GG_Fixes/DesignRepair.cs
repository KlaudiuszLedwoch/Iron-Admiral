// 1.11.103: ships of the player whose design is not in the player's design list (prizes taken before 1.11.6, ships received
// in other ways) cannot be refitted - the refit starts from the ship's design in the list. 22.10: a captured French BB of the
// Magnanime class had no design in the German list. Once per loaded campaign (a few seconds after the map appears) every such
// ship gets a copy of its design in the player's list (one copy per foreign design), the same way prizes do since 1.11.6.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignRepair
    {
        internal static MelonPreferences_Entry<bool> On;
        static IntPtr session; static float due = -1f; static bool done;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("RepairMissingDesigns", true, null, "Campaign: the player's ships whose design is missing from the design list (e.g. old prizes) get a copy of their design, so they can be refitted");
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            bool map = false; try { map = GameManager.IsWorldMap; } catch { }
            if (!map) return;
            var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) return;
            var ptr = cc.CampaignData.Pointer;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (ptr != session) { session = ptr; done = false; due = now + 5f; }
            // 1.11.141: checked again every new month (the game may erase a design during the campaign)
            int month = 0; try { month = Fog.Month(); } catch { }
            if (month != lastMonth) { if (lastMonth != int.MinValue && done) { done = false; due = now + 5f; } lastMonth = month; }
            if (done || now < due) return;
            done = true;
            try { Run(); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignRepair] " + e.Message); }
        }

        static int lastMonth = int.MinValue;
        static string Safe(Func<bool> f) { try { return f().ToString(); } catch { return "?"; } }
        static void Run()
        {
            Player main = null;
            var ps = CampaignController.Instance.CampaignData.Players;
            for (int i = 0; i < ps.Count; i++) if (ps[i] != null && ps[i].isMain) { main = ps[i]; break; }
            if (main == null) return;
            var inList = new HashSet<IntPtr>();
            var en = main.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var x = en.Current; if (x != null) inList.Add(x.Pointer); }
            // 1.11.105: the visible design list (designs) can differ from designsAll - ships whose design is in designsAll but not
            // in the visible list, or whose design belongs to another nation, are listed in the log (22.10: Magnanime-class prize)
            var visible = new HashSet<IntPtr>();
            try { var ve = main.designs.GetEnumerator(); var vu = ve.Cast<Il2CppSystem.Collections.IEnumerator>(); while (vu.MoveNext()) { var x = ve.Current; if (x != null) visible.Add(x.Pointer); } } catch { }
            var ships = new List<Ship>();
            var fe = main.fleetAll.GetEnumerator(); var fu = fe.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (fu.MoveNext()) { var s = fe.Current; if (s != null) ships.Add(s); }
            var pc = PlayerController.Instance; if (pc == null) return;
            var cloned = new Dictionary<IntPtr, Ship>();
            int fixedN = 0, checkedN = 0;
            foreach (var s in ships)
            {
                checkedN++;
                Ship d = null; try { d = s.design; } catch { }
                // 1.11.125: the design list shown in the designer is designsAll without erased designs (Player.get_designs) - a prize whose
                // design was erased (Magnanime, Implacable 23.10) keeps it in designsAll but cannot be refitted: it gets a fresh copy
                bool erased = false; try { erased = d != null && d.isErased; } catch { }
                if (d != null && inList.Contains(d.Pointer) && !erased)
                {
                    string owner = null; try { owner = d.player != null ? d.player.data.name : null; } catch { }
                    bool vis = visible.Contains(d.Pointer);
                    if (!vis || (owner != null && owner != main.data.name))
                    {
                        bool refit = false, isDes = false; try { refit = d.isRefitDesign; isDes = d.isDesign; } catch { }
                        string hull = "?"; try { hull = d.hull != null && d.hull.data != null ? d.hull.data.name : "?"; } catch { }
                        MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] check: {s.name} - design {d.name}, owner {owner ?? "?"}, in visible list {vis}, isDesign {isDes}, refit design {refit}, hull {hull}");
                    }
                    continue;
                }
                if (d == null) continue;
                try
                {
                    if (!cloned.TryGetValue(d.Pointer, out var c))
                    {
                        c = pc.CloneDesign(d, main);
                        cloned[d.Pointer] = c;
                        if (c != null) inList.Add(c.Pointer);
                        // 1.11.141: the copy of an erased design came back erased too (23.10: BB Magnanime copied at every load and
                        // still missing in the designer) - the copy is made a normal, visible design of the player
                        if (c != null)
                        {
                            // isErased = status Erased (7): give the copy the status of a normal visible design of the player
                            try
                            {
                                if (c.isErased)
                                {
                                    var st = VesselEntity.Status.None; bool got = false;
                                    try { var e6 = main.designs.GetEnumerator(); var u6 = e6.Cast<Il2CppSystem.Collections.IEnumerator>(); while (u6.MoveNext()) { var x = e6.Current; if (x != null && !x.isErased) { st = x.status; got = true; break; } } } catch { }
                                    c.status = st;
                                    MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] copy {c.name}: erased status cleared (now {st}{(got ? ", as the other designs" : "")})");
                                }
                            }
                            catch (Exception e2) { MelonLogger.Warning("[DIP GG Fixes][DesignRepair] status: " + e2.Message); }
                            try { if (c.player == null || c.player.Pointer != main.Pointer) c.player = main; } catch { }
                            bool inAll = false; try { var e3 = main.designsAll.GetEnumerator(); var u3 = e3.Cast<Il2CppSystem.Collections.IEnumerator>(); while (u3.MoveNext()) if (e3.Current != null && e3.Current.Pointer == c.Pointer) { inAll = true; break; } } catch { }
                            bool vis2 = false; try { var e5 = main.designs.GetEnumerator(); var u5 = e5.Cast<Il2CppSystem.Collections.IEnumerator>(); while (u5.MoveNext()) if (e5.Current != null && e5.Current.Pointer == c.Pointer) { vis2 = true; break; } } catch { }
                            MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] copy {c.name}: erased {Safe(() => c.isErased)}, in design list {inAll}, visible in the designer {vis2}");
                        }
                        MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {s.name}: design {d.name} (owner {(d.player != null ? d.player.data.name : "?")}) was not in the {main.data.name} design list{(erased ? " (erased)" : "")} - copied as {(c != null ? c.name : "null")}");
                    }
                    if (c != null) { s.design = c; fixedN++; }
                }
                catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][DesignRepair] {s.name}: " + e.Message); }
            }
            MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {main.data.name}: {checkedN} ships checked, {fixedN} given a design in the list ({cloned.Count} design copies)");
            try { Rejoin(main, ships, visible); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignRepair] rejoin: " + e.Message); }
        }

        // 1.11.504 (author 31.10, 11:35: "BB Westfalen came loose from her class design - it happened once before with another ship;
        // fix it so I can refit her together with the others"): a ship that sits alone on a design of its own while that design is
        // the same as the class design of her sisters (same base name, hull, displacement, armour and parts - a copy made when the game
        // lost or erased the design, or by the design repair above) is put back on the class design, so a refit takes the whole class.
        // Every player's ship and its design are logged once per load (design, ships on it) so a loose ship can be found in the log.
        static string BaseName(string n) { if (string.IsNullOrEmpty(n)) return ""; int i = n.IndexOf(" ("); return (i > 0 ? n.Substring(0, i) : n).Trim(); }
        static string Sig(Ship d)
        {
            var sb = new System.Text.StringBuilder();
            try { sb.Append(d.hull != null && d.hull.data != null ? d.hull.data.name : "?").Append('|').Append(Math.Round(d.Tonnage())).Append('|'); } catch { sb.Append("?|"); }
            try { foreach (var kv in d.armor) sb.Append((int)kv.Key).Append('=').Append(Math.Round(kv.Value)).Append(','); } catch { }
            sb.Append('|');
            try { var names = new List<string>(); var parts = d.parts; for (int i = 0; parts != null && i < parts.Count; i++) { var p = parts[i]; if (p != null && p.data != null) names.Add(p.data.name); } names.Sort(); sb.Append(string.Join(",", names)); } catch { }
            return sb.ToString();
        }
        static List<Ship> AllVisible(Player main) { var l = new List<Ship>(); try { var e = main.designs.GetEnumerator(); var u = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (u.MoveNext()) { var x = e.Current; if (x != null) { bool er = false; try { er = x.isErased; } catch { } if (!er) l.Add(x); } } } catch { } return l; }
        static void Rejoin(Player main, List<Ship> ships, HashSet<IntPtr> visible)
        {
            var byDesign = new Dictionary<IntPtr, List<Ship>>(); var designs = new Dictionary<IntPtr, Ship>();
            foreach (var s in ships)
            {
                Ship d = null; try { d = s.design; } catch { }
                if (d == null) continue;
                if (!byDesign.TryGetValue(d.Pointer, out var l)) { l = new List<Ship>(); byDesign[d.Pointer] = l; designs[d.Pointer] = d; }
                l.Add(s);
            }
            foreach (var kv in byDesign)
            {
                var d = designs[kv.Key]; string t = "?"; try { t = d.shipType != null ? d.shipType.name : "?"; } catch { }
                if (t == "bb" || t == "bc" || t == "ca" || t == "cl")
                    MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] class {d.name} ({t}, refit design {Safe(() => d.isRefitDesign)}, visible {visible.Contains(d.Pointer)}): {kv.Value.Count} ship(s) - {string.Join(", ", kv.Value.ConvertAll(x => x.name))}");
            }
            int moved = 0;
            foreach (var kv in byDesign)
            {
                if (kv.Value.Count != 1) continue;   // only a ship alone on her design
                var d = designs[kv.Key]; string bn = BaseName(d.name), sig = Sig(d);
                Ship target = null; int best = 0;
                // 1.11.505 (author 31.10, 11:39: "meanwhile I refitted the other 3 ships"): the class design may have no ships left on it
                // (the sisters moved to a refit design) - every visible design of the player counts, the one with the most ships first;
                // back on the class design the ship can take the same refit as her sisters
                foreach (var d2 in AllVisible(main))
                {
                    if (d2 == null || d2.Pointer == kv.Key) continue;
                    if (BaseName(d2.name) != bn) continue;
                    if (Sig(d2) != sig) { MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {kv.Value[0].name} on {d.name}: class design {d2.name} has the same name but a different ship (hull, displacement, armour or parts) - not this one"); continue; }
                    int n = byDesign.TryGetValue(d2.Pointer, out var on2) ? on2.Count : 0;
                    if (target == null || n > best) { best = n; target = d2; }
                }
                if (target == null) continue;
                var s = kv.Value[0];
                try { s.design = target; moved++; MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {s.name}: alone on design {d.name}, identical to the class design {target.name} ({best} other ship(s) on it) - put back on the class design; her refit can now follow the class"); }
                catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][DesignRepair] {s.name}: rejoin failed: " + e.Message); }
            }
            MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {main.data.name}: {moved} loose ship(s) put back on their class design");
        }
    }
}
