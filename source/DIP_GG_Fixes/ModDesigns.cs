// 1.11.68: shared designs shipped with the mod. Mods\IronAdmiralDesigns\*.bindesign are copied once into the game's shared
// design folder (LocalLow\Game Labs\Ultimate Admiral Dreadnoughts\Designs) at start, before the game loads shared designs.
// A file that already exists there is never overwritten and nothing is ever deleted - the player's own designs stay as they
// are. Switch: InstallModDesigns.
using System;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class ModDesigns
    {
        internal static MelonPreferences_Entry<bool> On, Shippack;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("InstallModDesigns", true, null, "Copy the shared ship designs that come with the mod (Mods/IronAdmiralDesigns) into the game's shared design folder at start - existing files are never overwritten");
            Shippack = cat.CreateEntry("InstallShippackDesigns", true, null, "Also copy the 920 designs of \"Ultimate Admiral's Shippack\" by Badkapp (MarineWerft, Nexus mod 45) that fit Iron Admiral's hulls (Mods/IronAdmiralDesigns/Shippack) - existing files are never overwritten");
        }
        static string DesignDir()
        {
            string root = null;
            try { root = Application.persistentDataPath; } catch { }
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Game Labs", "Ultimate Admiral Dreadnoughts");
            return Path.Combine(root, "Designs");
        }
        static bool SameFile(string a, string b)
        {
            try { var fa = new FileInfo(a); var fb = new FileInfo(b); if (fa.Length != fb.Length) return false; return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)); }
            catch { return true; }
        }
        internal static void Install()
        {
            if (On == null || !On.Value) return;
            try
            {
                string src = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns");
                if (!Directory.Exists(src)) return;
                string dst = DesignDir(); Directory.CreateDirectory(dst);
                int copied = 0, kept = 0;
                var files = new System.Collections.Generic.List<string>(Directory.GetFiles(src, "*.bindesign"));
                string sp = Path.Combine(src, "Shippack");
                if (Shippack != null && Shippack.Value && Directory.Exists(sp)) files.AddRange(Directory.GetFiles(sp, "*.bindesign"));
                string hp = Path.Combine(src, "Historical");   // historical classes (1.11.77)
                if (Directory.Exists(hp)) files.AddRange(Directory.GetFiles(hp, "*.bindesign"));
                int replaced = 0; string aside = null;
                foreach (var f in files)
                {
                    string to = Path.Combine(dst, Path.GetFileName(f));
                    if (File.Exists(to))
                    {
                        // 1.11.81: the Shippack files of 1.2.47-1.2.50 carried technologies that do not exist in DIP (submarine_exp_*,
                        // submarine_hull_*), so the game could not load any of them ("no such TechnologyData", "Couldn't load ...").
                        // A copy of a Shippack file that differs from the corrected one is moved aside (never deleted), then replaced.
                        if (f.StartsWith(sp, StringComparison.OrdinalIgnoreCase) && !SameFile(f, to))
                        {
                            if (aside == null) { aside = Path.Combine(Path.GetDirectoryName(dst), "Designs_replaced_by_IronAdmiral", DateTime.Now.ToString("yyyy-MM-dd_HHmm")); Directory.CreateDirectory(aside); }
                            string bak = Path.Combine(aside, Path.GetFileName(f));
                            if (!File.Exists(bak)) File.Move(to, bak); else File.Move(to, bak + "." + DateTime.Now.Ticks);
                            File.Copy(f, to); replaced++; continue;
                        }
                        kept++; continue;
                    }
                    File.Copy(f, to); copied++;
                }
                MelonLogger.Msg($"[DIP GG Fixes][Designs] mod shared designs: {copied} copied to the game's design folder, {kept} already there (not overwritten)" + (replaced > 0 ? $", {replaced} older Shippack copies replaced (previous files moved to {aside})" : ""));
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Designs] install failed: " + e.Message); }
        }
    }
}
