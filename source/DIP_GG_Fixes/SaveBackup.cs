// 1.11.477 (author 30.10 21:37, after a campaign was lost - a beta diagnostic broke loading and the auto save overwrote the save:
// "add backing up of my campaign from now on, best at every start of the game - add it as a feature of the mod for the other
// players too"): at every start of the game, before anything is loaded, the campaign saves (save_*.bin) and settings.bin are
// copied to <saves folder>\IronAdmiral_backups\<date_time>\. A start with saves identical to the last backup makes no new copy.
// The newest SaveBackupKeep (30) backups are kept, older ones are removed (only these backup folders, never the saves). A save
// that shrank to less than a fifth of its last backup is reported in the log - that is how a lost campaign looks.
// Restoring: copy the file back from the backup folder with the game closed. Switch: SaveBackup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MelonLoader;

namespace DipGgFixes
{
    internal static class SaveBackup
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<int> Keep;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SaveBackup", true, null, "At every start of the game the campaign saves are copied to IronAdmiral_backups next to them (a copy only when they changed; the newest 30 kept)");
            Keep = cat.CreateEntry("SaveBackupKeep", 30, null, "How many backups of the campaign saves are kept");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][SaveBackup] " + m); }
        internal static string SavesDir
        {
            get
            {
                string lowRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
                return Path.Combine(lowRoot, "Game Labs", "Ultimate Admiral Dreadnoughts");
            }
        }
        static string Hash(string f) { using (var s = File.OpenRead(f)) using (var h = SHA1.Create()) return BitConverter.ToString(h.ComputeHash(s)); }

        // MelonPreferences are read only after OnInitializeMelon - the backup runs on the first frame, still before any campaign is loaded
        static bool done;
        internal static void Tick()
        {
            if (done) return; done = true;
            try { Run(); } catch (Exception e) { Log("failed: " + e.Message); }
        }
        static void Run()
        {
            if (On == null || !On.Value) return;
            string dir = SavesDir; if (!Directory.Exists(dir)) { Log("saves folder not found: " + dir); return; }
            var files = Directory.GetFiles(dir, "save_*.bin").Where(f => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"^save_\d+\.bin$")).ToList();
            string settings = Path.Combine(dir, "settings.bin"); if (File.Exists(settings)) files.Add(settings);
            if (files.Count == 0) { Log("no campaign saves yet"); return; }
            string root = Path.Combine(dir, "IronAdmiral_backups"); Directory.CreateDirectory(root);
            var old = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).ToList();
            var last = old.FirstOrDefault();
            // the same saves as in the last backup: nothing to do
            bool same = last != null;
            if (last != null)
                foreach (var f in files)
                {
                    string b = Path.Combine(last.FullName, Path.GetFileName(f));
                    if (!File.Exists(b) || new FileInfo(b).Length != new FileInfo(f).Length || Hash(b) != Hash(f)) { same = false; break; }
                }
            if (same) { Log($"saves unchanged since the last backup ({last.Name}) - no new copy"); return; }
            if (last != null)
                foreach (var f in files)
                {
                    string b = Path.Combine(last.FullName, Path.GetFileName(f)); if (!File.Exists(b) || f.EndsWith("settings.bin")) continue;
                    long now = new FileInfo(f).Length, before = new FileInfo(b).Length;
                    if (before > 200000 && now * 5 < before) Log($"WARNING: {Path.GetFileName(f)} shrank from {before / 1024} KB to {now / 1024} KB since the backup {last.Name} - if the campaign lost its ships, restore it from that folder with the game closed");
                }
            string to = Path.Combine(root, DateTime.Now.ToString("yyyy-MM-dd_HHmmss")); Directory.CreateDirectory(to);
            long total = 0; foreach (var f in files) { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); total += new FileInfo(f).Length; }
            Log($"{files.Count} file(s), {total / 1024} KB backed up to {to}");
            int keep = Math.Max(3, Keep != null ? Keep.Value : 30);
            var all = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.Name).ToList();
            for (int i = keep; i < all.Count; i++) { try { all[i].Delete(true); } catch { } }
        }
    }
}
