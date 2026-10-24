// 1.11.171 (beta): historical rate of fire and range of medium guns (HistoricalGuns, F10 Battle AI, on by default).
// Measured in a 1916 test battle (grade 3 guns, DIP values), rounds per minute per barrel / max range:
//   3" 16.3 / 7.9 km, 4" 11.4 / 8.1 km, 5" 6.9 / 9.4 km, 6" 5.2 / 10.4 km, 12" 1.84 / 15.4 km, 15" 1.58 / 19.2 km.
// Historical (navweaps.com): 3" 15-20/min, 8.5 km (WWI); 4" 8-15/min, 8.8 km; 5"/51 8-9/min, ~14.5 km at 15 deg;
//   6" ~4/min in action (3 sustained), 13.4-14.5 km (WWI); 12" ~1.5/min, 17.4 km (WWI, 13.5 deg); 15"/42 ~2/min.
// So: 5" fires 1.2x faster and reaches 1.45x further, 6" fires 0.8x as fast and reaches 1.35x further, 12" fires 0.85x as fast.
// Only calibres measured against sources are changed; others stay as they are.
// 1.11.172: off by default - one multiplier per calibre for every gun generation (Mk) is wrong; to be redone per calibre and generation.
using System;
using MelonLoader;

namespace DipGgFixes
{
    internal static class HistoricalGuns
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HistoricalGuns", false, null, "Battle: historical rate of fire and range of medium guns - 5\" guns fire faster and reach further, 6\" guns fire a little slower but reach further, 12\" guns fire a little slower (DIP compressed medium gun ranges)");
        }
        internal static float RofMult(float calIn)
        {
            if (On == null || !On.Value) return 1f;
            if (calIn >= 4.6f && calIn < 5.6f) return 1.2f;
            if (calIn >= 5.6f && calIn < 6.6f) return 0.8f;
            if (calIn >= 11.6f && calIn < 12.6f) return 0.85f;
            return 1f;
        }
        internal static float RangeMult(float calIn)
        {
            if (On == null || !On.Value) return 1f;
            if (calIn >= 4.6f && calIn < 5.6f) return 1.45f;
            if (calIn >= 5.6f && calIn < 6.6f) return 1.35f;
            return 1f;
        }
    }
}
