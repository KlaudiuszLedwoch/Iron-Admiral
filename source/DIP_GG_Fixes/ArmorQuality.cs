// 1.11.250 (author 25.10: "still too little damage, and penetration too"; AutoTest 1916 3 BB vs 2 BB + CA, 25.10: 30 minutes at
// 6-12 km, ~120 hits of 12-13 in, every ship below 5 % damage).
// Cause: the armour strength of the armour-quality technologies is counted twice. The penetration table (penetration.csv, rebuilt
// for Iron Admiral from historical data: Okun, navweaps) gives what a shell pierces in Krupp cemented armour (KC / Class A), e.g. a
// 12-in shell 439 mm at 10 km. The game then multiplies every plate by the strength of the ship's armour technology (armor_str in
// technologies.csv: Krupp +100 %, KC 1914 +120 %, +15 % from the 1916 forging), so a 200 mm KC belt met a shell as 461 mm and a
// 16-in belt as 907 mm. The vanilla game had a penetration table about 1.4-2x higher to go with those strengths.
// Here the armour a shell meets is the plate times its quality relative to KC, from the historical comparisons:
//   wrought iron 0.40, compound 0.50, nickel steel 0.60, Harvey 0.80 (10.2 in KC = 12 in Harvey), Krupp cemented 1.00,
//   later KC types 1.03-1.12 (Okun: WWII Class A / KC n/A about 1.0-1.1 of the WWI plate).
// The game's own factor (effective / plate) is mapped onto this scale point by point (game 1.0/1.25/1.5/1.7/2.0/2.2/2.3/2.4/2.5/
// 2.7/2.8 = technologies armor_quality_1..11). Technology still decides the armour, never the calendar year.
// Switch: ArmorQualityHistorical (F10, Battle). Log: [Armor] armour quality ... once per battle per ship class.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ArmorQuality
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ArmorQualityHistorical", true, null, "Battle: armour quality relative to Krupp cemented armour (iron 0.4, compound 0.5, nickel steel 0.6, Harvey 0.8, Krupp 1.0, later types up to 1.12) instead of the game's strength bonus on top of a penetration table that is already measured against Krupp armour - shells pierce what they pierced historically");
        }
        static readonly float[] gameF = { 1.0f, 1.25f, 1.5f, 1.7f, 2.0f, 2.2f, 2.3f, 2.4f, 2.5f, 2.7f, 2.8f };
        static readonly float[] histF = { 0.40f, 0.50f, 0.60f, 0.80f, 1.00f, 1.03f, 1.05f, 1.07f, 1.08f, 1.10f, 1.12f };
        internal static float Hist(float g)
        {
            if (g <= gameF[0]) return histF[0] * g / gameF[0];
            for (int i = 1; i < gameF.Length; i++)
                if (g <= gameF[i]) { float t = (g - gameF[i - 1]) / (gameF[i] - gameF[i - 1]); return histF[i - 1] + t * (histF[i] - histF[i - 1]); }
            return histF[histF.Length - 1] + (g - gameF[gameF.Length - 1]) * 0.1f;
        }
        static readonly HashSet<string> logged = new HashSet<string>(); static int errors; static IntPtr battle;
        internal static float Apply(Ship s, Ship.A zone, float eff)
        {
            if (On == null || !On.Value || s == null || eff <= 0f) return eff;
            try
            {
                var a = s.armor; if (a == null) return eff;
                float raw; if (!a.TryGetValue(zone, out raw) || raw <= 0.5f) return eff;
                float g = eff / raw;
                if (g < 0.9f || g > 4f) return eff; // not a plate-times-quality value (inner layers, odd zones) - left to the game
                float h = Hist(g);
                float res = raw * h;
                try
                {
                    IntPtr b = IntPtr.Zero; var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer;
                    if (b != battle) { battle = b; logged.Clear(); }
                    string key = (s.design != null ? s.design.name : s.name) + "|" + zone;
                    if ((zone == Ship.A.Belt || zone == Ship.A.Deck) && logged.Count < 60 && logged.Add(key))
                        MelonLogger.Msg($"[DIP GG Fixes][Armor] armour quality {s.name} {zone}: plate {raw:0.0} mm, game strength x{g:0.00} -> {eff:0.0} mm; historical quality x{h:0.00} of Krupp cemented -> {res:0.0} mm");
                }
                catch { }
                return res;
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Armor] armour quality: " + e.Message); return eff; }
        }
    }
}
