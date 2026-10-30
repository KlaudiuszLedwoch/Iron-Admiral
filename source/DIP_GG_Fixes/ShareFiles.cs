// 1.11.445 (author 30.10 16:38-16:57: a gallery of the most interesting screenshots "from me and from the players", the players'
// designs coming in through GitHub too, "transferred by the mod itself - after consent"; option 2 chosen: the mod packs, the
// player drags): F10 > Tools > "Share screenshots and designs" puts a folder on the desktop with the newest JPG screenshots of
// the mod (last 7 days, up to 10, each under GitHub's 10 MB) and one zip with the player's own shared designs (not those that
// came with the mod, not the generated [gen] ones; the 30 newest), opens the folder and the GitHub page for them. Nothing is sent
// by the mod - the player looks at the files, deletes what he does not want and drags the rest into a comment.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class ShareFiles
    {
        internal const string Url = "https://github.com/KlaudiuszLedwoch/Iron-Admiral/issues/101";
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Share] " + m); }

        static HashSet<string> ModDesignNames()
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var src = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns");
                if (Directory.Exists(src)) foreach (var f in Directory.GetFiles(src, "*.bindesign", SearchOption.AllDirectories)) s.Add(Path.GetFileName(f));
            }
            catch { }
            return s;
        }
        static string DesignDir()
        {
            string root = null; try { root = Application.persistentDataPath; } catch { }
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Game Labs", "Ultimate Admiral Dreadnoughts");
            return Path.Combine(root, "Designs");
        }

        internal static string Create(out int shots, out int designs)
        {
            shots = 0; designs = 0;
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) desk = MelonUtils.GameDirectory;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string folder = Path.Combine(desk, "IronAdmiral_share_" + stamp); Directory.CreateDirectory(folder);
            try
            {
                var jd = SteamShots.JpgDir;
                if (Directory.Exists(jd))
                    foreach (var f in new DirectoryInfo(jd).GetFiles("*.jpg").Where(x => x.LastWriteTime > DateTime.Now.AddDays(-7) && x.Length < 10000000).OrderByDescending(x => x.LastWriteTimeUtc).Take(10))
                    { f.CopyTo(Path.Combine(folder, f.Name), true); shots++; }
            }
            catch (Exception e) { Log("screenshots: " + e.Message); }
            try
            {
                var dd = DesignDir(); var mod = ModDesignNames();
                if (Directory.Exists(dd))
                {
                    var own = new DirectoryInfo(dd).GetFiles("*.bindesign").Where(x => !x.Name.Contains("[gen]") && !mod.Contains(x.Name)).OrderByDescending(x => x.LastWriteTimeUtc).Take(30).ToList();
                    if (own.Count > 0)
                    {
                        string zip = Path.Combine(folder, "IronAdmiral_designs_" + stamp + ".zip");
                        using (var fs = new FileStream(zip, FileMode.Create))
                        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                            foreach (var f in own) { z.CreateEntryFromFile(f.FullName, f.Name, CompressionLevel.Optimal); designs++; }
                    }
                }
            }
            catch (Exception e) { Log("designs: " + e.Message); }
            try
            {
                File.WriteAllText(Path.Combine(folder, "README.txt"),
                    "Iron Admiral - screenshots and designs to share\n\n" +
                    "Look at the files and delete what you do not want to share. Nothing has been sent - the mod never sends anything.\n" +
                    "Then open " + Url + " , drag the .jpg files and the designs zip into a comment and say in one line what is happening\n" +
                    "(nation, year, battle) and the name you want to be credited with. Screenshots may go to the wiki gallery, designs into\n" +
                    "the mod's shared designs with your name in CREDITS.txt.\n" +
                    "GitHub limits: pictures up to 10 MB, zip up to 25 MB.\n", new UTF8Encoding(false));
            }
            catch { }
            try { Application.OpenURL("file:///" + folder.Replace('\\', '/')); } catch { }
            try { Application.OpenURL(Url); } catch { }
            Log($"share folder {Path.GetFileName(folder)}: {shots} screenshot(s), {designs} design(s)");
            return folder;
        }
    }
}
