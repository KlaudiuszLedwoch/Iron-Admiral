// 1.11.451 (author 30.10 17:46: "and the ones that are useless for given nations - because they can never be built - will you
// remove them?"): after the game has read the shared designs, every design stored for a nation whose hull that nation may never
// use (the hull's countries in parts.csv) is listed; its file in the shared design folder is moved - never deleted - to
// Designs_unbuildable_<date> next to it, so it is not read again at every start and every saved design. The mod's own design
// pack is left alone (Shippack, historical classes) except the author's exported designs; the moved file names are kept in
// UserData\IronAdmiral\unbuildable_designs.txt and are not installed again. Switch: MoveUnbuildableDesigns.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignCleanup
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("MoveUnbuildableDesigns", true, null, "Shared designs: a design saved for a nation that may never use its hull is moved out of the design folder (to Designs_unbuildable_<date>, never deleted) - fewer files to read at every start and every saved design");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Designs] " + m); }
        internal static string ListFile { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "unbuildable_designs.txt"); } }
        static HashSet<string> moved;
        internal static HashSet<string> Moved()
        {
            if (moved != null) return moved;
            moved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(ListFile)) foreach (var l in File.ReadAllLines(ListFile)) if (l.Trim().Length > 0) moved.Add(l.Trim()); } catch { }
            return moved;
        }
        static string DesignDir()
        {
            string root = null; try { root = Application.persistentDataPath; } catch { }
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Game Labs", "Ultimate Admiral Dreadnoughts");
            return Path.Combine(root, "Designs");
        }

        internal static void AfterLoad(GameData gd)
        {
            if (On == null || !On.Value || gd == null) return;
            try
            {
                var per = gd.sharedDesignsPerNation; if (per == null) return;
                var bad = new List<string>();   // file name prefixes "<year> <nation> <name>"
                foreach (var kv in per)
                {
                    var list = kv.Value; if (list == null) continue;
                    PlayerData pd = null; try { gd.players.TryGetValue(kv.Key, out pd); } catch { }
                    if (pd == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var s = list[i].Item1; if (s == null) continue;
                        string hn = null; try { hn = s.hullName; } catch { }
                        if (string.IsNullOrEmpty(hn)) continue;
                        PartData hull = null; try { gd.parts.TryGetValue(hn, out hull); } catch { }
                        if (hull == null) continue;
                        bool ok = true; try { var c = hull.countriesx; ok = c == null || c.Count == 0 || c.Contains(pd); } catch { }
                        if (!ok) { string nm = ""; try { nm = s.vesselName; } catch { } bad.Add($"{s.YearCreated} {kv.Key} {nm}"); }
                    }
                }
                if (bad.Count == 0) return;
                string src = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns");
                var pack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { if (Directory.Exists(src)) foreach (var f in Directory.GetFiles(src, "*.bindesign", SearchOption.AllDirectories)) if (!f.Contains(Path.DirectorySeparatorChar + "Author" + Path.DirectorySeparatorChar)) pack.Add(Path.GetFileName(f)); } catch { }
                string dir = DesignDir(); if (!Directory.Exists(dir)) return;
                string aside = null; int n = 0, keptPack = 0; var names = new List<string>();
                foreach (var f in Directory.GetFiles(dir, "*.bindesign"))
                {
                    string fn = Path.GetFileName(f), bn = Path.GetFileNameWithoutExtension(f);
                    bool hit = false; foreach (var b in bad) if (bn.StartsWith(b + " [", StringComparison.OrdinalIgnoreCase) || bn.Equals(b, StringComparison.OrdinalIgnoreCase)) { hit = true; break; }
                    if (!hit) continue;
                    if (pack.Contains(fn)) { keptPack++; continue; }
                    if (aside == null) { aside = Path.Combine(Path.GetDirectoryName(dir), "Designs_unbuildable_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm")); Directory.CreateDirectory(aside); }
                    string to = Path.Combine(aside, fn); if (File.Exists(to)) to += "." + DateTime.Now.Ticks;
                    File.Move(f, to); n++; Moved().Add(fn); if (names.Count < 10) names.Add(bn);
                }
                if (n > 0) { try { Directory.CreateDirectory(Path.GetDirectoryName(ListFile)); File.WriteAllLines(ListFile, Moved()); } catch { } }
                Log($"designs for a nation that may never use their hull: {bad.Count} found, {n} file(s) moved to {(aside != null ? Path.GetFileName(aside) : "-")}{(keptPack > 0 ? $", {keptPack} of the mod's pack left in place" : "")}{(names.Count > 0 ? " - " + string.Join("; ", names) : "")} (they load until the next start)");
            }
            catch (Exception e) { Log("unbuildable check: " + e.Message); }
        }
    }
}
