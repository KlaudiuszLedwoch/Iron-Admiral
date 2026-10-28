// 1.11.317 (#59): the game decides by a roll on the impact angle whether a shell strikes the side or the deck (Ship.CalcSideHitChance)
// and reads the side or the deck penetration accordingly. For a deck hit it still asks Ship.GetArmorZoneForPart(part, sideHit: false),
// which for parts in the bow and stern sections answered a belt zone (BeltBow / BeltStern / Belt): in the 27.10 test (beta.25) 125 of
// 380 hits on belt zones were deck hits - a 381 mm shell at 7 deg fall with its deck penetration of 85 mm checked against the 39 mm
// end belt and stopped. A deck hit now meets the deck plate of the same part of the ship (Belt -> Deck, BeltBow -> DeckBow,
// BeltStern -> DeckStern); the side hits are unchanged. Switch: DeckHitsMeetDeck (F10, Battle).
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DeckZoneFix
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static int Mapped;
        static int logged;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DeckHitsMeetDeck", true, null, "Battle: a shell the game rolled as a deck hit meets the deck plate of that part of the ship, not the belt (the game answered BeltBow / BeltStern / Belt for deck hits on the ends)");
        }
        internal static void Fix(bool sideHit, bool conningTower, ref Ship.A zone)
        {
            if (sideHit || conningTower || On == null || !On.Value) return;
            Ship.A to = zone;
            if (zone == Ship.A.Belt) to = Ship.A.Deck;
            else if (zone == Ship.A.BeltBow) to = Ship.A.DeckBow;
            else if (zone == Ship.A.BeltStern) to = Ship.A.DeckStern;
            if (to == zone) return;
            if (logged++ < 5) MelonLogger.Msg($"[DIP GG Fixes][Hit] deck hit on zone {zone} - checked against {to} (#59)");
            zone = to; Mapped++;
        }
    }

    [HarmonyPatch(typeof(Ship), "GetArmorZoneForPart")]
    internal static class Patch_DeckZoneFix
    {
        private static void Postfix(bool __1, bool __2, ref Ship.A __result) { try { DeckZoneFix.Fix(__1, __2, ref __result); } catch { } }
    }
}
