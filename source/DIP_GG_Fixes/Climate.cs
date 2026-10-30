// 1.11.431 (author 30.10 09:47: "the weather should depend on the season and the place on the globe - I seem to fight in fine
// weather all the time"): at the start of a campaign battle the weather and the wind are drawn from a climate table by latitude
// band (and a few seas of their own) and the season of the hemisphere - claude/klimat_pogoda_bitew.md: US Navy Marine Climatic
// Atlas (Europe, North Pacific, Mediterranean), NOAA pilot charts, Young 1999 wave climate, QuikSCAT wind frequencies. Spring
// = 0.35 winter + 0.65 summer, autumn = 0.65 winter + 0.35 summer. Tropical cyclone seasons raise the storms of 15-30 deg.
// The values are a game calibration derived from those data. Switch: BattleClimateWeather.
using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Climate
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BattleClimateWeather", true, null, "Campaign battles: the weather and the wind come from the climate of the battle's latitude, sea and season (North Sea gales in winter, a calm Mediterranean summer, the Southern Ocean, the monsoon, the cyclone seasons) instead of the game's draw");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Climate] " + m); }

        // weather Clear, Cloudy, Overcast, Rainy, Stormy; wind Calm, Light, Gentle, Moderate, Gale, StrongGale (%)
        class Row { public float[] W, Wi; public Row(float[] w, float[] wi) { W = w; Wi = wi; } }
        static Row R(float a, float b, float c, float d, float e, float f, float g, float h, float i, float j, float k)
        { return new Row(new[] { a, b, c, d, e }, new[] { f, g, h, i, j, k }); }
        static readonly Row Trop = R(20, 45, 22, 12, 1, 10, 15, 30, 40, 5, 0);
        static readonly Row SubCyc = R(30, 40, 15, 12, 3, 8, 15, 30, 42, 4.5f, 0.5f), SubCycPeak = R(29, 39, 15, 11, 6, 8, 14, 29, 41, 6.5f, 1.5f), Sub = R(25, 40, 20, 13, 2, 6, 12, 30, 45, 7, 0);
        static readonly Row MidW = R(15, 25, 33, 20, 7, 4, 8, 18, 40, 27, 3), MidS = R(30, 35, 24, 10, 1, 8, 15, 30, 40, 7, 0);
        static readonly Row HiNW = R(10, 15, 37, 18, 20, 2, 4, 10, 29, 45, 10), HiNS = R(13, 22, 50, 13, 2, 6, 12, 25, 42, 14, 1);
        static readonly Row SoW = R(5, 15, 42, 20, 18, 1, 3, 8, 28, 50, 10), SoS = R(5, 15, 45, 22, 13, 1, 3, 8, 32, 50, 6);
        static readonly Row PolW = R(8, 14, 38, 20, 20, 2, 4, 10, 29, 45, 10), PolS = R(10, 20, 50, 15, 5, 4, 8, 20, 40, 25, 3);
        static readonly Row NsW = R(17, 28, 33, 12, 10, 3, 6, 14, 37, 34, 6), NsS = R(30, 35, 25, 9, 1, 7, 14, 28, 42, 9, 0);
        static readonly Row MedW = R(25, 30, 25, 14, 6, 6, 12, 22, 38, 19, 3), MedS = R(60, 30, 7, 2, 1, 10, 18, 30, 32, 10, 0);
        static readonly Row Monsoon = R(10, 25, 35, 18, 12, 0, 2, 6, 30, 52, 10);

        static Row Mix(Row w, Row s, float kw)
        {
            var a = new float[5]; var b = new float[6];
            for (int i = 0; i < 5; i++) a[i] = w.W[i] * kw + s.W[i] * (1f - kw);
            for (int i = 0; i < 6; i++) b[i] = w.Wi[i] * kw + s.Wi[i] * (1f - kw);
            return new Row(a, b);
        }
        // 1 = winter, 0 = summer of the hemisphere; spring 0.35, autumn 0.65
        static float WinterK(int month, bool north)
        {
            int m = north ? month : ((month + 5) % 12) + 1;   // the southern months shifted by half a year
            if (m == 12 || m == 1 || m == 2) return 1f; if (m >= 6 && m <= 8) return 0f;
            return (m >= 3 && m <= 5) ? 0.35f : 0.65f;
        }
        static bool CycloneSeason(float lat, float lon, int month, out bool peak)
        {
            peak = false;
            if (lat > 0f)
            {
                if (lon > 100f && lon < 180f) { peak = month >= 8 && month <= 9; return month >= 7 && month <= 11; }   // NW Pacific
                if (lon > 50f && lon <= 100f) { peak = month == 5 || month == 11; return month >= 4 && month <= 12; } // North Indian Ocean
                peak = month == 9; return month >= 6 && month <= 11;                                                // Atlantic, NE Pacific
            }
            peak = month >= 1 && month <= 3; return month >= 11 || month <= 5;                                     // southern hemisphere
        }
        static Row Pick(float lat, float lon, int month, out string where)
        {
            float a = Mathf.Abs(lat); bool north = lat >= 0f; float k = WinterK(month, north);
            if (north && lat >= 49f && lat <= 61f && lon >= -6f && lon <= 12f) { where = "North Sea / Channel"; return Mix(NsW, NsS, k); }
            if (north && lat >= 30f && lat <= 46f && lon >= -6f && lon <= 37f) { where = "Mediterranean"; return Mix(MedW, MedS, k); }
            if (north && lat >= 53f && lat <= 66f && lon > 12f && lon <= 31f) { where = "Baltic"; return Mix(HiNW, HiNS, k); }   // 1.11.454: the Gulf of Bothnia at 62-64 N is not the Arctic
            if (north && lat >= 5f && lat <= 25f && lon >= 50f && lon <= 100f && month >= 6 && month <= 9) { where = "Arabian Sea / Bay of Bengal, SW monsoon"; return Monsoon; }
            if (a >= 60f) { where = north ? "Arctic seas" : "Antarctic waters"; return Mix(PolW, PolS, k); }
            if (a >= 45f) { where = north ? "45-60 N" : "Southern Ocean"; return north ? Mix(HiNW, HiNS, k) : Mix(SoW, SoS, k); }
            if (a >= 30f) { where = "30-45"; return Mix(MidW, MidS, k); }
            if (a >= 15f)
            {
                if (CycloneSeason(lat, lon, month, out bool peak)) { where = peak ? "15-30, cyclone season peak" : "15-30, cyclone season"; return peak ? SubCycPeak : SubCyc; }
                where = "15-30"; return Sub;
            }
            where = "tropics 0-15"; return Trop;
        }
        static int Draw(float[] p)
        {
            float sum = 0f; foreach (var x in p) sum += x; float r = UnityEngine.Random.value * sum;
            for (int i = 0; i < p.Length; i++) { r -= p[i]; if (r <= 0f) return i; }
            return p.Length - 1;
        }

        static IntPtr done; static float since = -1f; static int errors;
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (!battle) { since = -1f; return; }
                if (!NavalAviation.CampaignBattle()) return;
                bool custom = false; try { custom = GameManager.IsCustomBattle; } catch { }
                if (custom || CampaignMap.Instance == null || CampaignController.Instance == null) return;   // 1.11.446 (#100): custom battle result window
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || b.Pointer == done) return;
                if (since < 0f) since = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - since < 0.5f) return;
                done = b.Pointer;
                Vector3 pos = b.BattleWorldPos;
                Vector3 ll = CampaignMap.Instance.UnityCoordsToLatitudeLongitude(pos);
                // 1.11.452 (#102): the conversion returns (Mercator y in degrees, 0, longitude) - raw (71.5, 0, 3.2) for a North Sea battle,
                // (80.1, 0, 17.3) in the Gulf of Bothnia, (84.5, 0, 43.6) in the White Sea; the latitude is the inverse Mercator of x
                float lat = 2f * Mathf.Atan(Mathf.Exp(ll.x * Mathf.Deg2Rad)) * Mathf.Rad2Deg - 90f, lon = ll.z;
                int month = 6; try { month = CampaignController.Instance.CurrentDate.AsDate().Month; } catch { }
                var row = Pick(lat, lon, month, out var where);
                int w = Draw(row.W), wi = Draw(row.Wi);
                var dc = DayCycleAndWeather.Instance(); if (dc == null) return;
                dc.SetWeather((DayCycleAndWeather.WeatherType)w);
                dc.SetWind((DayCycleAndWeather.WindStrength)wi);
                Log($"battle at {lat:0.0}, {lon:0.0} (raw {ll}), month {month}: {where} -> {(DayCycleAndWeather.WeatherType)w}, wind {(DayCycleAndWeather.WindStrength)wi}");
            }
            catch (Exception e) { if (errors++ < 5) Log("failed: " + e.Message + " " + e.StackTrace); }
        }
    }
}
