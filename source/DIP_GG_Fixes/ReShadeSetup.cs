// 1.11.155 (beta): optional ReShade with the Iron Admiral naval film look (F10 > Other > ReShadeNavalLook, off by default).
// Switched on: the package listed in reshade/manifest.txt on the mod's GitHub page (ReShade 6.8.0 as dxgi.dll, a few SweetFX
// and AMD CAS shaders, the preset) is downloaded, checked against its SHA-256 and unpacked into the game folder; files already
// there are moved to Mods_backup_IronAdmiral first. Switched off: the files of the package are moved to the backup folder
// (nothing is deleted). A ReShade the player installed himself (dxgi.dll without the Iron Admiral marker) is never touched.
// Takes effect at the next game start.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using MelonLoader;

namespace DipGgFixes
{
    internal static class ReShadeSetup
    {
        internal static MelonPreferences_Entry<bool> On;
        const string ManifestUrl = "https://raw.githubusercontent.com/KlaudiuszLedwoch/Iron-Admiral/main/reshade/manifest.txt";
        const string Marker = "IronAdmiral_ReShade.txt";
        static readonly string[] RootFiles = { "dxgi.dll", "ReShade.ini", "IronAdmiral_Naval.ini", Marker, "LICENSE_ReShade.md", "LICENSE_SweetFX.txt" };
        static int applied = -1; static volatile bool busy;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ReShade] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ReShadeNavalLook", false, null, "Graphics: ReShade with the Iron Admiral naval film look (warm light, cool sea, film contrast and grain). Switched on, it is downloaded from the mod's GitHub page; switched off, its files go to a backup folder. Takes effect at the next game start; Home opens the ReShade menu, Scroll Lock switches the effects on and off");
        }

        static string Game { get { return MelonUtils.GameDirectory; } }

        internal static void Tick()
        {
            if (On == null || busy) return;
            int want = On.Value ? 1 : 0; if (want == applied) return;
            applied = want; busy = true;
            System.Threading.Tasks.Task.Run(() => { try { if (want == 1) Install(); else Remove(); } catch (Exception e) { Log("error: " + e.Message); } finally { busy = false; } });
        }

        static string Backup()
        {
            var d = Path.Combine(Game, "Mods_backup_IronAdmiral", "reshade_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(d); return d;
        }

        static void Install()
        {
            string g = Game, dxgi = Path.Combine(g, "dxgi.dll"), mark = Path.Combine(g, Marker);
            if (File.Exists(dxgi) && !File.Exists(mark)) { Log("a dxgi.dll that is not from Iron Admiral is already in the game folder (own ReShade or another tool) - left alone, nothing installed"); return; }
            if (File.Exists(dxgi) && File.Exists(mark)) { Log("naval film look already installed"); return; }
            string url = null, sha = null;
            using (var hc = new System.Net.Http.HttpClient())
            {
                hc.Timeout = TimeSpan.FromSeconds(60);
                hc.DefaultRequestHeaders.UserAgent.ParseAdd("IronAdmiral-DIP_GG_Fixes");
                var man = hc.GetStringAsync(ManifestUrl).GetAwaiter().GetResult();
                foreach (var ln in man.Replace("\r", "").Split('\n'))
                {
                    var p = ln.Trim().Split(new[] { ' ' }, 2); if (p.Length < 2) continue;
                    if (p[0] == "url") url = p[1].Trim(); else if (p[0] == "sha256") sha = p[1].Trim().ToLowerInvariant();
                }
                if (url == null || sha == null) { Log("manifest without url/sha256 - nothing installed"); return; }
                if (!url.StartsWith("https://github.com/KlaudiuszLedwoch/Iron-Admiral/", StringComparison.OrdinalIgnoreCase)) { Log("manifest points outside the mod's GitHub page - refused"); return; }
                var data = hc.GetByteArrayAsync(url).GetAwaiter().GetResult();
                string h; using (var s = SHA256.Create()) h = BitConverter.ToString(s.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
                if (h != sha) { Log($"package checksum does not match ({h} vs {sha}) - nothing installed"); return; }
                string bk = null; int n = 0, skipped = 0;
                string root = Path.GetFullPath(g).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                using (var ms = new MemoryStream(data))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    foreach (var e in zip.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/'); if (name.EndsWith("/")) continue;
                        string target = Path.GetFullPath(Path.Combine(g, name.Replace('/', Path.DirectorySeparatorChar)));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        bool shader = name.StartsWith("reshade-shaders/", StringComparison.OrdinalIgnoreCase);
                        if (File.Exists(target))
                        {
                            if (shader) { skipped++; continue; } // a shader of the same name is already there - kept
                            if (bk == null) bk = Backup();
                            File.Move(target, Path.Combine(bk, Path.GetFileName(target)));
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var src = e.Open()) using (var dst = File.Create(target)) src.CopyTo(dst);
                        n++;
                    }
                }
                Log($"naval film look installed: {n} file(s){(skipped > 0 ? $", {skipped} shader(s) of the same name kept" : "")}{(bk != null ? ", earlier files moved to " + bk : "")} - active from the next game start (Home = ReShade menu)");
            }
        }

        static void Remove()
        {
            string g = Game, mark = Path.Combine(g, Marker);
            if (!File.Exists(mark)) return; // not installed by Iron Admiral - never touched
            string bk = Backup(); int n = 0;
            foreach (var f in RootFiles)
            {
                var p = Path.Combine(g, f); if (!File.Exists(p)) continue;
                try { File.Move(p, Path.Combine(bk, f)); n++; } catch (Exception e) { Log($"{f}: {e.Message}"); }
            }
            Log($"naval film look switched off: {n} file(s) moved to {bk} - ReShade is gone from the next game start (the shaders stay in reshade-shaders)");
        }
    }
}
