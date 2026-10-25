// 1.11.211 (author, 24.10): high-explosive shells against thin plating. The game gives an HE shell only a tenth of the AP penetration
// (he_penetration_mod 0.1): a 5-inch HE shell at 3-5 km "penetrates" ~10 mm, while a 1920s destroyer carries ~12-22 mm of plating
// in the game. Below 80 % of the armour the hit does no damage at all (armor_damage_threshold), so 5-inch HE bounced off destroyers,
// and 5-inch AP passed straight through them (over-penetration, a tenth of the damage) - the author's 24.10 battle: a cruiser's
// 127 mm guns at 3-5 km did nothing to a destroyer with either shell.
// History: nose-fuzed HE and common shells burst on contact with light plating and blew holes in it - this is why every navy fired
// HE at destroyers and kept AP for armoured ships (AP shells through destroyers often failed to arm: Samar 1944).
// Here an HE shell bursting on plating no thicker than a fraction of its calibre (HeBlastPlateFraction, 1/4 by default: 127 mm ->
// 32 mm, 102 mm -> 25 mm, 152 mm -> 38 mm) counts as a full penetration of that plate instead of being stopped by it.
// AP over-penetration against unarmoured ships is left as it is (historical). Switch: HeBlastThinPlating (F10, Battle).
using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ThinPlatingHE
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Frac;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("HeBlastThinPlating", true, null, "Battle: HE shells blow through thin plating (destroyers, torpedo boats, unarmoured ends) up to a fraction of their calibre, instead of bouncing off it");
            Frac = cat.CreateEntry("HeBlastPlateFraction", 0.25f, null, "Battle: thickest plate an HE shell blows through, as a fraction of its calibre (0.25: a 127 mm shell holes 32 mm)");
        }
        static int blastFrame = -1, count, errors, battleCount; static bool inBattle;

        // IsPenetrationArmorDamaged(bool useHe, float angle, float penetrationMm, float armorMm, Part from)
        internal static void ArmorDamaged(bool useHe, float pen, float armor, Part from, ref bool result)
        {
            if (On == null || !On.Value || !useHe || from == null || armor <= 0f || pen >= armor) return;
            try
            {
                var d = from.data; if (d == null) return;
                float cal = d.caliber; if (cal <= 0f) return;
                if (armor > cal * Mathf.Clamp(Frac != null ? Frac.Value : 0.25f, 0.05f, 0.5f)) return;
                result = true; blastFrame = Time.frameCount; count++;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] HE blast: " + e.Message); }
        }
        // GetArmorModForPenetrations(string hitType, bool useHe, float angle): a partial hit ("armor") right after a blast-through counts as a full one
        internal static void ArmorMod(string hitType, bool useHe, ref float result)
        {
            if (!useHe || blastFrame != Time.frameCount || hitType != "armor") return;
            if (result < 1f) result = 1f;
        }
        internal static void Tick()
        {
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (b && !inBattle) { inBattle = true; count = 0; }
            else if (!b && inBattle) { inBattle = false; if (On != null && On.Value) MelonLogger.Msg($"[DIP GG Fixes][Damage] battle summary - HE shells that blew through thin plating (would have been stopped by the game): {count}"); }
        }
    }

    [HarmonyPatch(typeof(Ship), "IsPenetrationArmorDamaged")]
    internal static class Patch_HeBlastArmorDamaged
    {
        private static void Postfix(bool __0, float __2, float __3, Part __4, ref bool __result) { try { ThinPlatingHE.ArmorDamaged(__0, __2, __3, __4, ref __result); } catch { } }
    }
    [HarmonyPatch(typeof(Ship), "GetArmorModForPenetrations")]
    internal static class Patch_HeBlastArmorMod
    {
        private static void Postfix(string __0, bool __1, ref float __result) { try { ThinPlatingHE.ArmorMod(__0, __1, ref __result); } catch { } }
    }
}
