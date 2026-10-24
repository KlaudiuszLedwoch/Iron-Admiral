// 1.11.180: technology anchors for the historical review. Rule of the mod: effects follow the technology fitted to each ship,
// never the calendar year. Where a historical table is indexed by year, the year used is the historical year of the technology
// (TechnologyData.year from technologies.csv) that the ship actually carries:
//   fire control  - the newest rangefinder or radar component fitted to the ship (its technology's year);
//   shell shape   - the aerodynamic nose technologies the ship was designed with (projectile_shell_15 "improved nose", 1907;
//                   projectile_shell_16 "better designed shells", 1927).
using System;
using System.Collections.Generic;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TechYears
    {
        static readonly Dictionary<long, int> fc = new Dictionary<long, int>();
        static readonly Dictionary<long, int> nose = new Dictionary<long, int>();
        internal static void Reset() { fc.Clear(); nose.Clear(); }

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
            try
            {
                foreach (var t in s.techs)
                {
                    if (t == null) continue;
                    string nm = t.name;
                    if (nm == "projectile_shell_16") n = Math.Max(n, 2);
                    else if (nm == "projectile_shell_15") n = Math.Max(n, 1);
                }
            }
            catch { }
            nose[k] = n; return n;
        }
    }
}
