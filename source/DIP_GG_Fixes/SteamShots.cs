// 1.11.436 (author 30.10 15:20: "the screenshots should be taken like F12 - captured by Steam"): every screenshot the mod takes
// (air strikes, spotting aircraft, highlights) is also added to the player's Steam screenshot library of the game, as F12 does
// (Steamworks ISteamScreenshots.AddScreenshotToLibrary) - it can then be uploaded from Steam and linked in a Steam post with a
// thumbnail. The camera of the shot stays the mod's (chase, observer, bridge), not the player's view. Switch: ScreenshotsToSteam.
using System;
using System.Collections;
using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class SteamShots
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ScreenshotsToSteam", true, null, "Screenshots of the mod (air strikes, spotting aircraft, highlights) are also added to the game's Steam screenshot library, as F12 does - upload them from Steam and link them in a post with a thumbnail");
            Jpg = cat.CreateEntry("ScreenshotsJpg", true, null, "Every screenshot of the mod also gets a JPG copy (UserData\\IronAdmiral\\screens\\jpg, about 1 MB, under GitHub's 10 MB limit) - drag it straight into a GitHub comment; the problem report adds the newest ones");
        }
        // 1.11.438 (author 30.10 15:33: "attach the logs directly and screenshots in jpg automatically"): a JPG copy (quality 85) of
        // every PNG the mod writes, in UserData\IronAdmiral\screens\jpg. GitHub shows a jpg in the comment as a picture (images up to
        // 10 MB); a 1080p shot is about 0.5-2 MB. Unity's ImageConversion is looked up at run time.
        internal static MelonPreferences_Entry<bool> Jpg;
        internal static string JpgDir { get { return System.IO.Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "screens", "jpg"); } }
        static MethodInfo loadImg, encJpg; static bool lookedJ; static int jpgs;
        internal static string JpgCopy(string file)
        {
            if (Jpg == null || !Jpg.Value || string.IsNullOrEmpty(file) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
            Texture2D tex = null;
            try
            {
                if (!lookedJ)
                {
                    lookedJ = true;
                    foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = a.GetType("UnityEngine.ImageConversion"); if (t == null) continue;
                        foreach (var m in t.GetMethods())
                        {
                            var ps = m.GetParameters();
                            if (m.Name == "LoadImage" && ps.Length == 2) loadImg = m;
                            if (m.Name == "EncodeToJPG" && ps.Length == 2) encJpg = m;
                        }
                        if (loadImg != null) break;
                    }
                    Log(loadImg != null && encJpg != null ? "JPG copies on" : "ImageConversion.LoadImage/EncodeToJPG not found - no JPG copies");
                }
                if (loadImg == null || encJpg == null) return null;
                if (!System.IO.File.Exists(file)) { if (errors++ < 5) Log("jpg: file not there yet: " + file); return null; }
                var raw = System.IO.File.ReadAllBytes(file);
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> arr = raw;
                tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!(bool)loadImg.Invoke(null, new object[] { tex, arr })) { if (errors++ < 5) Log("jpg: could not read " + file); return null; }
                byte[] bytes = null;
                foreach (int q in new[] { 85, 70, 55 })
                {
                    var res = encJpg.Invoke(null, new object[] { tex, q }) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>;
                    if (res == null) return null;
                    bytes = new byte[res.Length]; for (int i = 0; i < bytes.Length; i++) bytes[i] = res[i];
                    if (bytes.Length < 9500000) break;
                }
                System.IO.Directory.CreateDirectory(JpgDir);
                var outF = System.IO.Path.Combine(JpgDir, System.IO.Path.GetFileNameWithoutExtension(file) + ".jpg");
                System.IO.File.WriteAllBytes(outF, bytes);
                if (jpgs++ < 5) Log("jpg copy " + System.IO.Path.GetFileName(outF) + " (" + (bytes.Length / 1024) + " KB)");
                return outF;
            }
            catch (Exception e) { if (errors++ < 5) Log("jpg: " + (e.InnerException?.Message ?? e.Message)); return null; }
            finally { try { if (tex != null) UnityEngine.Object.Destroy(tex); } catch { } }
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][SteamShots] " + m); }
        static MethodInfo add; static bool looked; static int errors, added;

        static MethodInfo Add()
        {
            if (looked) return add; looked = true;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType("Il2CppSteamworks.SteamScreenshots") ?? a.GetType("Steamworks.SteamScreenshots");
                    if (t == null) continue;
                    add = t.GetMethod("AddScreenshotToLibrary", new[] { typeof(string), typeof(string), typeof(int), typeof(int) });
                    if (add != null) break;
                }
                Log(add != null ? "Steam screenshot library found" : "Steamworks screenshots not found - screenshots stay in UserData only");
            }
            catch (Exception e) { Log("lookup: " + e.Message); }
            return add;
        }

        // the file must be written already; a file written at the end of the frame (ScreenCapture) is added a moment later
        internal static void ToLibrary(string file, int w, int h, float delay = 0f)
        {
            if (string.IsNullOrEmpty(file)) return;
            bool steam = On != null && On.Value, jpg = Jpg != null && Jpg.Value;
            if (!steam && !jpg) return;
            if (delay > 0f) { MelonCoroutines.Start(Later(file, w, h, delay)); return; }
            if (jpg) JpgCopy(file);
            if (!steam) return;
            var m = Add(); if (m == null) return;
            try
            {
                if (!System.IO.File.Exists(file)) { if (errors++ < 5) Log("file not there yet: " + file); return; }
                m.Invoke(null, new object[] { file, null, w, h });
                if (added++ < 5) Log("added to the Steam screenshot library: " + System.IO.Path.GetFileName(file));
            }
            catch (Exception e) { if (errors++ < 5) Log("add: " + (e.InnerException?.Message ?? e.Message)); }
        }
        static IEnumerator Later(string file, int w, int h, float delay)
        {
            float t = Time.realtimeSinceStartup + delay;
            while (Time.realtimeSinceStartup < t) yield return null;
            ToLibrary(file, w, h, 0f);
        }
    }
}
