// 1.11.429 (#85, author 30.10 09:31 "add the weather", 09:46 "the weather rules are OK"): the weather decides whether aircraft fly.
// The battle's wind, sea and weather (the game's accuracy data) are turned into a Beaufort number; each era has its limits,
// taken from the cases in claude/pogoda_lotnictwo_morskie_85.md (calibration for the game, not historical thresholds):
//   1915-20 seaplanes hoisted out and back: sea to Bft 3 (Bft 4: half the attempts fail), no flying in fog, rain or storm
//     (Campania flew in 3 of 7 Grand Fleet exercises 1915; Engadine at Jutland "relied on calm seas"; Cuxhaven 1914 called off twice);
//   1920s early carriers: to Bft 5 (Bft 6: 1 in 5 fails), visibility at least 5 km, no storm;
//   1930s carrier fleet: to Bft 6 (Bft 7: half fail), visibility at least 4 km;
//   1940s: to Bft 9 (Ark Royal flew off in a Force 9 gale, 26.5.1941), visibility at least 3 km, none at Bft 10 and over.
// Catapult spotting aircraft are recovered by crane from the sea: only to Bft 4 (about 1.5 m waves - SC Seahawk, 1945).
using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class AirWeather
    {
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirWeather] " + m); }
        static IntPtr logged;

        static string Sub(AccuracyData a) { try { return a == null ? "" : ((a.subName ?? "") + " " + (a.nameUi ?? "") + " " + (a.type ?? "")).ToLowerInvariant(); } catch { return ""; } }
        static int WindBft(string w)
        {
            if (w.Contains("strong")) return 9; if (w.Contains("gale")) return 8; if (w.Contains("moderate")) return 4;
            if (w.Contains("gentle")) return 3; if (w.Contains("light")) return 2; if (w.Contains("calm")) return 1; return -1;
        }
        static int WaveBft(string w)
        {
            if (w.Contains("very")) return 7; if (w.Contains("rough")) return 6; if (w.Contains("moderate")) return 4;
            if (w.Contains("slight")) return 3; if (w.Contains("smooth")) return 2; if (w.Contains("calm")) return 1; return -1;
        }
        // the battle's Beaufort number (-1 = unknown), and whether it rains or storms
        internal static int Bft(out bool rain, out bool storm, out string raw)
        {
            rain = false; storm = false; raw = "";
            try
            {
                var bm = BattleManager.Instance; if (bm == null) return -1;
                string wi = Sub(bm.GetWindData()), wa = Sub(bm.GetWavesData()), we = Sub(bm.GetWeatherData());
                raw = $"wind '{wi.Trim()}', sea '{wa.Trim()}', weather '{we.Trim()}'";
                rain = we.Contains("rain"); storm = we.Contains("storm");
                int b = Math.Max(WindBft(wi), WaveBft(wa));
                if (storm) b = Math.Max(b, 10);
                var cur = bm.CurrentBattle; IntPtr id = cur != null ? cur.Pointer : IntPtr.Zero;
                if (id != logged) { logged = id; Log($"battle weather: {raw} -> Beaufort {b}{(rain ? ", rain" : "")}{(storm ? ", storm" : "")}"); }
                return b;
            }
            catch { return -1; }
        }
        static float VisKm() { try { float d = BattleManager.Instance.GetDaytimeDetection(); return d > 1.5f ? d : 0f; } catch { return 0f; } }

        // can aircraft of this year's type fly off (and get back)? why = the reason when not
        internal static bool CanFly(int year, out string why)
        {
            why = "";
            int b = Bft(out bool rain, out bool storm, out _); float vis = VisKm();
            int max, soft; float minVis; bool wet;
            if (year < 1921) { max = 3; soft = 4; minVis = 5f; wet = true; }
            else if (year < 1931) { max = 5; soft = 6; minVis = 5f; wet = false; }
            else if (year < 1940) { max = 6; soft = 7; minVis = 4f; wet = false; }
            else { max = 9; soft = 9; minVis = 3f; wet = false; }
            if (storm && year < 1940) { why = "storm"; return false; }
            if (wet && rain) { why = "rain - the seaplanes do not go up"; return false; }
            if (vis > 0f && vis < minVis) { why = $"visibility {vis:0} km, below {minVis:0} km"; return false; }
            if (b >= 0)
            {
                if (b >= 10) { why = $"Beaufort {b} - nothing flies"; return false; }
                if (b > soft) { why = $"Beaufort {b}, the limit is {max}"; return false; }
                if (b > max)
                {
                    float fail = year < 1921 ? 0.5f : year < 1931 ? 0.2f : 0.5f;
                    if (UnityEngine.Random.value < fail) { why = $"Beaufort {b}: the attempt to fly off failed"; return false; }
                }
            }
            return true;
        }
        // catapult spotting aircraft: they must be recovered from the sea by crane
        internal static bool CanRecoverSeaplane(out string why)
        {
            why = ""; int b = Bft(out _, out bool storm, out _);
            if (storm) { why = "storm"; return false; }
            if (b > 4) { why = $"Beaufort {b} - the sea is too rough to recover a seaplane by crane"; return false; }
            return true;
        }
    }
}
