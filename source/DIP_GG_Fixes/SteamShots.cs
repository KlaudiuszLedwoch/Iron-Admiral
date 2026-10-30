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
            if (On == null || !On.Value || string.IsNullOrEmpty(file)) return;
            if (delay > 0f) { MelonCoroutines.Start(Later(file, w, h, delay)); return; }
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
