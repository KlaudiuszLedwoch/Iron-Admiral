// HistoricalGuns (F10 Battle AI, on): historical rate of fire and range of naval guns BY CALIBRE AND GUN GENERATION (Mk 1-5).
// 1.11.183: absolute historical targets instead of fixed ratios. For each calibre and generation the target is the rated rate of fire
// and the range at the period mounting's elevation of real guns of that period (navweaps.com; reference guns and URLs in
// claude/kalibracja_dzialy_radar.md and DIP_CHANGES.md). The game's own value for the gun is multiplied by target / the gun's base
// value from guns.csv (GunData.firerate_N / range_N), so the ship's own modifiers (crew, technologies, mounting) stay on top.
// Generations (from the AutoTestLab battles): Mk 1 ~1890-1898, Mk 2 ~1899-1908, Mk 3 ~WWI, Mk 4 ~1920-1935, Mk 5 ~WWII.
// 0 = no historical gun of that calibre in that period (or no usable figure): the game's value stays.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HistoricalGuns
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static bool Suspend; // raw game values for AutoTestLab
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalGuns", true, null, "Battle: historical rate of fire and range of naval guns by calibre and gun generation (Mk) - each generation fires and reaches like real guns of its period (DIP compressed most ranges)");
        }
        // calibre band (inches, from..to) -> rate of fire (rounds/min/gun) and range (km) for Mk 1..5
        class Row { public float From, To; public float[] Rof, Km; }
        static readonly Row[] Rows =
        {
            new Row { From = 2.6f, To = 3.6f,  Rof = new[] { 15f, 15f, 17.5f, 17.5f, 17.5f }, Km = new[] { 0f, 8.8f, 8.5f, 12.5f, 13.3f } },
            new Row { From = 3.6f, To = 4.6f,  Rof = new[] { 8.5f, 12f, 11f, 12f, 0f },        Km = new[] { 9.6f, 12.2f, 10.5f, 16f, 15f } },
            new Row { From = 4.6f, To = 5.6f,  Rof = new[] { 8.5f, 7f, 8.5f, 10f, 13.5f },     Km = new[] { 11.8f, 17.4f, 14.5f, 16.3f, 16.9f } },
            new Row { From = 5.6f, To = 6.6f,  Rof = new[] { 6f, 6f, 6f, 7f, 9f },             Km = new[] { 11.4f, 15f, 14f, 24.3f, 21.6f } },
            new Row { From = 6.6f, To = 7.8f,  Rof = new[] { 2f, 3.75f, 3.3f, 5.5f, 0f },      Km = new[] { 11.5f, 13.9f, 18.1f, 19.3f, 0f } },
            new Row { From = 7.8f, To = 8.6f,  Rof = new[] { 2.4f, 3f, 5f, 3.7f, 4f },         Km = new[] { 14.6f, 18.4f, 19.1f, 29f, 30.7f } },
            new Row { From = 8.6f, To = 9.6f,  Rof = new[] { 2f, 2f, 3.5f, 0f, 0f },           Km = new[] { 12.4f, 15.5f, 14.1f, 0f, 0f } },
            new Row { From = 9.6f, To = 10.6f, Rof = new[] { 1.5f, 2.5f, 1.5f, 0f, 0f },       Km = new[] { 9.2f, 15.9f, 0f, 0f, 0f } },
            new Row { From = 10.6f, To = 11.6f, Rof = new[] { 0.5f, 2f, 3f, 2.5f, 3.5f },     Km = new[] { 15.9f, 18.8f, 18.5f, 36.5f, 40.9f } },
            new Row { From = 11.6f, To = 12.6f, Rof = new[] { 1.3f, 1.75f, 2f, 2.5f, 0f },    Km = new[] { 11.9f, 15.8f, 19.7f, 21.9f, 0f } },
            new Row { From = 12.6f, To = 13.6f, Rof = new[] { 0.4f, 0f, 1.75f, 0f, 0f },      Km = new[] { 11.3f, 0f, 21.8f, 0f, 0f } },
            new Row { From = 13.6f, To = 14.6f, Rof = new[] { 0f, 0f, 1.6f, 1.75f, 2f },      Km = new[] { 0f, 0f, 19.7f, 27.3f, 35.3f } },
            new Row { From = 14.6f, To = 15.6f, Rof = new[] { 0f, 0f, 2.25f, 0f, 2f },        Km = new[] { 0f, 0f, 22.4f, 0f, 39.2f } },
            new Row { From = 15.6f, To = 16.6f, Rof = new[] { 0f, 0f, 0f, 1.75f, 2f },        Km = new[] { 0f, 0f, 0f, 35.7f, 36.2f } },
            new Row { From = 17.6f, To = 18.6f, Rof = new[] { 0f, 0f, 1f, 0f, 1.75f },        Km = new[] { 0f, 0f, 26.4f, 0f, 0f } },
        };
        static Row Find(float c) { foreach (var r in Rows) if (c >= r.From && c < r.To) return r; return null; }
        static float BaseRof(GunData g, int k) { try { switch (k) { case 1: return g.firerate_1; case 2: return g.firerate_2; case 3: return g.firerate_3; case 4: return g.firerate_4; default: return g.firerate_5; } } catch { return 0f; } }
        static float BaseRange(GunData g, int k) { try { switch (k) { case 1: return g.range_1; case 2: return g.range_2; case 3: return g.range_3; case 4: return g.range_4; default: return g.range_5; } } catch { return 0f; } }
        static readonly Dictionary<long, float> cache = new Dictionary<long, float>();
        static int logged;
        static float Mult(GunData g, int grade, bool range)
        {
            if (Suspend || On == null || !On.Value || g == null) return 1f;
            int k = Math.Max(1, Math.Min(5, grade));
            long key = (g.Pointer.ToInt64() << 4) ^ (k << 1) ^ (range ? 1 : 0);
            float m; if (cache.TryGetValue(key, out m)) return m;
            m = 1f;
            var r = Find(g.caliberInch);
            if (r != null)
            {
                float target = range ? r.Km[k - 1] * 1000f : r.Rof[k - 1];
                float bas = range ? BaseRange(g, k) : BaseRof(g, k);
                if (range && bas > 0f && bas < 200f) bas *= 1000f; // guns.csv ranges may be in km
                if (target > 0f && bas > 0f) m = Math.Max(0.3f, Math.Min(3f, target / bas));
                if (logged++ < 60) MelonLogger.Msg($"[DIP GG Fixes][Guns] {g.caliberInch:0.0}\" Mk {k} {(range ? "range" : "rate of fire")}: base {bas:0.##}, historical {(target > 0f ? target.ToString("0.##") : "none")} -> x{m:0.00}");
            }
            cache[key] = m; return m;
        }
        internal static float RofMult(GunData g, int grade) { return Mult(g, grade, false); }
        internal static float RangeMult(GunData g, int grade) { return Mult(g, grade, true); }
    }
}
