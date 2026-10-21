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
                foreach (var f in files)
                {
                    string to = Path.Combine(dst, Path.GetFileName(f));
                    if (File.Exists(to)) { kept++; continue; }
                    File.Copy(f, to); copied++;
                }
                MelonLogger.Msg($"[DIP GG Fixes][Designs] mod shared designs: {copied} copied to the game's design folder, {kept} already there (not overwritten)");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Designs] install failed: " + e.Message); }
        }
    }
}
