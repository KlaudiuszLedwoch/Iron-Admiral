// 1.11.180: technology anchors for the historical review. Rule of the mod: effects follow the technology fitted to each ship,
// never the calendar year. Where a historical table is indexed by year, the year used is the historical year of the technology
// (TechnologyData.year from technologies.csv) that the ship actually carries:
//   fire control  - the newest rangefinder or radar component fitted to the ship (its technology's year);
//   shell shape   - the aerodynamic nose technologies the ship was designed with (projectile_shell_15 "improved nose", 1907;
//                   projectile_shell_16 "better designed shells", 1927).
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TechYears
    {
        static readonly Dictionary<long, int> fc = new Dictionary<long, int>();
        static readonly Dictionary<long, int> nose = new Dictionary<long, int>();
        static int logged;
        internal static void Reset() { fc.Clear(); nose.Clear(); logged = 0; }

        // year of the newest fire-control component (rangefinder or radar) fitted to the ship; 0 = none found
        internal static int FireControl(Ship s)
        {
            if (s == null) return 0;
            long k = s.Pointer.ToInt64(); int y; if (fc.TryGetValue(k, out y)) return y;
            y = 0;
            try
            {
                var comps = s.components;
                if (comps != null) foreach (var kv in comps)
                {
                    var cd = kv.Value; if (cd == null) continue;
                    if (cd.type != "rangefinder" && cd.type != "radar") continue;
                    int ty = 0; try { var t = cd.tech; if (t != null) ty = t.year; } catch { }
                    if (ty > y) y = ty;
                }
            }
            catch { }
            if (y == 0)
            {
                try { foreach (var t in s.techs) if (t != null && t.type == "aim_rangefinder" && t.year > y) y = t.year; } catch { }
            }
            fc[k] = y; return y;
        }

        // shell nose generation from the ship's design technologies: 0 = early blunt shells, 1 = improved nose (1907), 2 = 1927 shells
        internal static int ShellNose(Ship s)
        {
            if (s == null) return 0;
            long k = s.Pointer.ToInt64(); int n; if (nose.TryGetValue(k, out n)) return n;
            n = 0;
            // 1.11.257 (#31, author's 25.10 battle 13:42: both player BBs of 1921 at generation 0, every British ship at 1): count what
            // each source holds so the log shows which path misses the nose technology
            int cDesign = 0, cAll = 0, cList = 0, cRes = 0; var seen = new System.Text.StringBuilder();
            try
            {
                foreach (var t in s.techs)
                {
                    if (t == null) continue;
                    string nm = t.name; cDesign++;
                    if (nm == "projectile_shell_15" || nm == "projectile_shell_16") seen.Append(" design:" + nm);
                    if (nm == "projectile_shell_16") n = Math.Max(n, 2);
                    else if (nm == "projectile_shell_15") n = Math.Max(n, 1);
                }
            }
            catch { }
            // 1.11.241 (24.10 battle log: 1921 battleships of Germany and Britain fired with the blunt 1890s shell shape, Cd 0.74, while one
            //           cruiser had the 1907 nose): shells are stores, not part of the hull - a navy fires the shells it has researched
            //           (the Greenboy shells after Jutland went to every ship), so the navy's researched technologies count as well
            if (n < 2)
            {
                try
                {
                    var p = s.player;
                    if (p != null)
                    {
                        try
                        {
                            var e = p.technologiesResearchedAll.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                            while (ue.MoveNext()) { var t = e.Current; if (t == null || t.data == null) continue; string nm = t.data.name; cAll++; if (nm == "projectile_shell_15" || nm == "projectile_shell_16") seen.Append(" researched:" + nm); if (nm == "projectile_shell_16") n = Math.Max(n, 2); else if (nm == "projectile_shell_15") n = Math.Max(n, 1); }
                        }
                        catch (Exception e1) { if (logged++ < 3) MelonLogger.Msg("[DIP GG Fixes][Tech] shell nose, researched list: " + e1.Message); }
                        // 1.11.243 (24.10 log: the player's 1921 ships still fired blunt shells, Cd 0.74, after 1.11.241): the technology list
                        //           of the player with the researched flag as a second path
                        if (n < 2)
                        {
                            try
                            {
                                var tl = p.technologies;
                                if (tl != null) for (int i = 0; i < tl.Count; i++) { var t = tl[i]; if (t == null || t.data == null) continue; bool ok = false; try { ok = t.isResearched; } catch { } cList++; if (ok) cRes++; string nm0 = t.data.name; if (nm0 == "projectile_shell_15" || nm0 == "projectile_shell_16") { float pr = -1f; try { pr = t.progress; } catch { } seen.Append($" list:{nm0}={(ok ? "researched" : "not researched")}{(pr >= 0f ? $" ({pr:0.##})" : "")}"); } if (!ok) continue; string nm = t.data.name; if (nm == "projectile_shell_16") n = Math.Max(n, 2); else if (nm == "projectile_shell_15") n = Math.Max(n, 1); }
                            }
                            catch (Exception e2) { if (logged++ < 3) MelonLogger.Msg("[DIP GG Fixes][Tech] shell nose, technology list: " + e2.Message); }
                        }
                    }
                }
                catch { }
            }
            if (logged < 24) { logged++; try { bool pl = false; try { pl = s.player != null && s.player.isMain; } catch { } MelonLogger.Msg($"[DIP GG Fixes][Tech] shell nose of {s.name}: generation {n} (0 blunt, 1 = 1907 nose, 2 = 1927 shells){(pl ? " [player]" : "")}; sources: design techs {cDesign}, navy researched {cAll}, navy list {cList} ({cRes} researched); nose technologies seen:{(seen.Length > 0 ? seen.ToString() : " none")}"); } catch { } }
            nose[k] = n; return n;
        }
    }
}
