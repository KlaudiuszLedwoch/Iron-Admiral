// 1.11.253 (#33, author's decision 25.10, 10:08): damage of an AP shell that passes clean through (the game's hit type "over").
// The author's 25.10 battle: CL Enterprise took 58 hits of 201.6 mm AP from 3.6-4.4 km; 45 passed through and did 313 damage in
// total (about 7 each, against about 790 for a hit that burst inside) - the game's damage modifier for "over" leaves almost nothing.
// A shell passing through still holes the ship twice, throws splinters and cuts what lies on its path (see OVERPEN_SOURCES below).
// Here the modifier of an AP pass-through is raised to a fraction of the modifier of a normal penetration (OverPenDamageFraction).
// The fraction comes from the historical hit-by-hit damage reports (claude/fizyka_balistyka_33.md, section "Przelot na wylot").
// HE shells are left to the game. Switch: OverPenDamage (F10, Battle).
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class OverPen
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Frac;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("OverPenDamage", true, null, "Battle: an AP shell passing clean through a ship does a fraction of the damage of a shell bursting inside (holes, splinters, cut cables and pipes), instead of almost nothing");
            Frac = cat.CreateEntry("OverPenDamageFraction", 0.20f, null, "Battle: damage of an AP pass-through as a fraction of a normal penetration (from the US Navy war damage reports, see the changelog)");
        }
        [ThreadStatic] static bool busy;
        static int raised, logged, errors;
        internal static int Raised { get { return raised; } }
        internal static void ResetCount() { raised = 0; }

        // Ship.GetArmorModForPenetrations(string hitType, bool useHe, float angle)
        internal static void ArmorMod(Ship ship, string hitType, bool useHe, float angle, ref float result)
        {
            if (busy || On == null || !On.Value || useHe || hitType != "over" || ship == null) return;
            busy = true;
            try
            {
                float normal = 1f;
                try { normal = ship.GetArmorModForPenetrations("normal", false, angle); } catch { }
                if (!(normal > 0f)) normal = 1f;
                float want = normal * Math.Max(0f, Math.Min(1f, Frac != null ? Frac.Value : 0.20f));
                if (result < want)
                {
                    if (logged++ < 3) MelonLogger.Msg($"[DIP GG Fixes][Damage] AP pass-through: game damage modifier {result:0.###} raised to {want:0.###} ({(Frac != null ? Frac.Value : 0.20f):0.##} of a normal penetration {normal:0.###})");
                    result = want; raised++;
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] pass-through: " + e.Message); }
            busy = false;
        }
    }

    [HarmonyPatch(typeof(Ship), "GetArmorModForPenetrations")]
    internal static class Patch_OverPenArmorMod
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(Ship __instance, string __0, bool __1, float __2, ref float __result) { try { OverPen.ArmorMod(__instance, __0, __1, __2, ref __result); } catch { } }
    }
}
