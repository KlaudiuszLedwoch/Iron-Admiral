// 1.11.174 (beta): historical rate of fire and range of medium guns BY GUN GENERATION (HistoricalGuns, F10 Battle AI, on).
// Game values per calibre and generation measured with AutoTestLab (1916 test, DIP values, claude/kalibracja_dzialy_radar.md):
//   Mk 3 (fitted in 1916): 3" 16.3/min 7.9 km, 4" 12.1/min 8.1 km, 5" 6.9/min 9.4 km, 6" 5.2/min 10.4 km; 12" Mk 2 1.84/min 15.4 km
//   Mk 5 (WWII):           3" 24.5/min 10.1 km, 4" 14.7/min 10.4 km, 5" 13.1/min 12.1 km, 6" 8.7/min 13.3 km
// Historical targets (navweaps.com): WWI generation - 3" 15-20/min 8.5 km, 4" 8.8 km, 5"/51 8-9/min ~14.5 km,
//   6" ~4/min in action 13.4-14.5 km, 12" ~1.5/min 17.4 km; WWII generation - 3" 13.3 km, 4" 15 km, 5"/38 12-15/min 16.6-17.3 km,
//   6" 19.7-23.6 km.
// Multipliers = target / game value for the measured generations (Mk 3 and Mk 5; Mk 2 for 12"); Mk 4 is interpolated between them,
// Mk 1-2 stay unchanged where no measured target exists (marked "interp." / "-" below). Values change with further measurements.
using System;
using MelonLoader;

namespace DipGgFixes
{
    internal static class HistoricalGuns
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalGuns", true, null, "Battle: historical rate of fire and range of medium guns by gun generation (Mk) - medium guns reach as far as their real counterparts of the same period (DIP compressed their ranges), WWI-era 6\" and 12\" fire at their real rate");
        }
        // 1.11.178: all five generations from navweaps.com (rate of fire = the page's rated figure; range = the page's range table at
        // the elevation of the period's mountings). Game generations: Mk 1 ~1890s, Mk 2 ~1899-1908 (fitted in 1906 tests),
        // Mk 3 ~WWI (fitted in 1916 tests), Mk 4 ~1920-1935, Mk 5 ~WWII. Reference guns per cell: claude/kalibracja_dzialy_radar.md.
        //                                Mk1    Mk2    Mk3    Mk4    Mk5
        static readonly float[] Range3 = { 1.00f, 1.26f, 1.08f, 1.30f, 1.32f };  // Mk1: only high-angle range tables found - unchanged
        static readonly float[] Range4 = { 1.24f, 1.65f, 1.09f, 1.60f, 1.44f };
        static readonly float[] Range5 = { 1.15f, 2.10f, 1.54f, 1.60f, 1.40f };
        static readonly float[] Range6 = { 1.11f, 1.70f, 1.35f, 2.00f, 1.75f };
        static readonly float[] Range12 = { 0.85f, 1.00f, 1.20f, 1.07f, 1.00f };
        static readonly float[] Rof3 = { 1.50f, 1.15f, 1.00f, 1.00f, 1.00f };
        static readonly float[] Rof4 = { 1.20f, 1.10f, 1.00f, 1.00f, 1.00f };
        static readonly float[] Rof5 = { 1.40f, 1.20f, 1.23f, 1.00f, 1.00f };
        static readonly float[] Rof6 = { 1.20f, 1.40f, 1.15f, 1.15f, 1.00f };
        static readonly float[] Rof12 = { 1.00f, 0.82f, 1.00f, 1.00f, 1.00f };
        static float Pick(float[] t, int grade) { int i = Math.Max(1, Math.Min(5, grade)) - 1; return t[i]; }
        static float[] RangeTable(float c)
        {
            if (c >= 2.6f && c < 3.6f) return Range3;
            if (c >= 3.6f && c < 4.6f) return Range4;
            if (c >= 4.6f && c < 5.6f) return Range5;
            if (c >= 5.6f && c < 6.6f) return Range6;
            if (c >= 11.6f && c < 12.6f) return Range12;
            return null;
        }
        static float[] RofTable(float c)
        {
            if (c >= 2.6f && c < 3.6f) return Rof3;
            if (c >= 3.6f && c < 4.6f) return Rof4;
            if (c >= 4.6f && c < 5.6f) return Rof5;
            if (c >= 5.6f && c < 6.6f) return Rof6;
            if (c >= 11.6f && c < 12.6f) return Rof12;
            return null;
        }
        internal static float RofMult(float calIn, int grade)
        {
            if (On == null || !On.Value) return 1f;
            var t = RofTable(calIn); return t == null ? 1f : Pick(t, grade);
        }
        internal static float RangeMult(float calIn, int grade)
        {
            if (On == null || !On.Value) return 1f;
            var t = RangeTable(calIn); return t == null ? 1f : Pick(t, grade);
        }
    }
}
