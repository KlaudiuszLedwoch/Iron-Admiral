// 1.11.441 (author 30.10 16:07: "it bothers me a lot that gun turrets have no armour by default - I keep forgetting it; the roof
// should be 25 mm and the walls as the gun's calibre"): in the ship designer, when a new gun calibre gets its turret armour entry
// (Ship.AddShipTurretArmor) and the entry is still empty, the turret gets a 25 mm roof and walls as thick as the gun's calibre,
// both within the limits the designer allows for that gun (MaxArmorForZone). Casemate guns and values the player has set are
// left alone; AI designs are not touched. Switch: TurretArmorDefault.
using System;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class TurretArmorDefault
    {
        internal static MelonPreferences_Entry<bool> On; internal static MelonPreferences_Entry<float> Roof;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TurretArmorDefault", true, null, "Ship designer: a newly added gun calibre gets turret armour at once - a 25 mm roof and walls as thick as the gun's calibre (within the designer's limits); casemates and the player's own values are left alone");
            Roof = cat.CreateEntry("TurretArmorDefaultRoofMm", 25f, null, "Ship designer: the default turret roof armour (mm) for a newly added gun calibre");
            SmallOn = cat.CreateEntry("SmallGunShieldArmor", true, null, "Ship designer: small guns (2-4 in) for which the game allows no turret armour at all can have a light gun house or shield - walls up to SmallGunShieldSideMm, roof up to SmallGunShieldTopMm");
            SmallSide = cat.CreateEntry("SmallGunShieldSideMm", 38f, null, "Ship designer: the most wall armour (mm) of a small gun's shield or gun house (1.5 in; historical shields and light gun houses were 1/2-2 in)");
            SmallTop = cat.CreateEntry("SmallGunShieldTopMm", 25f, null, "Ship designer: the most roof armour (mm) of a small gun's shield or gun house");
        }
        internal static MelonPreferences_Entry<bool> SmallOn; internal static MelonPreferences_Entry<float> SmallSide, SmallTop;
        static int calls;
        // 1.11.443 (author 30.10 16:15-16:31: "2 and 3 in guns - no armour can be set at all", "4 in too", "it shows 0 and nothing can be
        // typed", "yes, I want to be able to add armour - it can be limited"): the game's limit for the turret armour of small guns is
        // 0; where it is 0 the limit becomes a light gun house - walls up to 38 mm, roof up to 25 mm, never more than the calibre
        [HarmonyPatch(typeof(Ship), "MaxArmorForZone", new Type[] { typeof(Ship.A), typeof(PartData) })]
        static class PatchMax
        {
            static void Postfix(Ship.A __0, PartData __1, ref float __result)
            {
                try
                {
                    if (SmallOn == null || !SmallOn.Value || __1 == null || __result > 0.01f) return;
                    if (__0 != Ship.A.TurretSide && __0 != Ship.A.TurretTop) return;
                    float cal = __1.caliber; if (cal <= 0f || cal > 160f) return;
                    __result = Mathf.Min(cal, __0 == Ship.A.TurretSide ? SmallSide.Value : SmallTop.Value);
                    if (smallLogged++ < 6) Log($"{__1.name} ({cal:0} mm): the game allows no {(__0 == Ship.A.TurretSide ? "wall" : "roof")} armour - limit now {__result:0} mm (gun house / shield)");
                }
                catch { }
            }
        }
        static int smallLogged;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][TurretArmor] " + m); }
        static int logged, errors;

        static void Apply(Ship ship, PartData pd)
        {
            if (On == null || !On.Value || ship == null || pd == null) return;
            bool cons = false; try { cons = GameManager.IsConstructor; } catch { }
            bool mine = true; try { mine = ship.player == null || ship.player.isMain; } catch { }
            if (calls++ < 8) Log($"new calibre {pd.name}: designer {cons}, player's ship {mine}, turret armour entries {(ship.shipTurretArmor != null ? ship.shipTurretArmor.Count : -1)}");
            if (!cons || !mine) return;
            var ta = ship.shipTurretArmor;
            for (int i = 0; ta != null && i < ta.Count; i++)
            {
                var t = ta[i]; if (t == null || t.isCasemateGun) continue;
                bool same = false; try { same = t.turretPartData != null && (t.turretPartData.Pointer == pd.Pointer || t.turretPartData.name == pd.name); } catch { }
                if (!same) continue;
                if (t.topTurretArmor > 0f || t.sideTurretArmor > 0f) return;   // already set (a copied or loaded design, or the player)
                float cal = 0f; try { cal = pd.caliber; } catch { }
                if (cal <= 0f) return;
                float maxTop = 9999f, maxSide = 9999f;
                try { maxTop = Mathf.Max(0f, ship.MaxArmorForZone(Ship.A.TurretTop, pd)); } catch { }   // 1.11.443: 0 = the designer allows no armour (small guns) - nothing is set
                try { maxSide = Mathf.Max(0f, ship.MaxArmorForZone(Ship.A.TurretSide, pd)); } catch { }
                if (logged < 30) Log($"{pd.name} ({cal:0} mm): designer limits - roof {maxTop:0} mm, walls {maxSide:0} mm");
                t.topTurretArmor = Mathf.Min(Roof.Value, maxTop);
                t.sideTurretArmor = Mathf.Min(cal, maxSide);
                if (logged++ < 10) Log($"{pd.name}: new turret armour - roof {t.topTurretArmor:0} mm, walls {t.sideTurretArmor:0} mm (calibre {cal:0} mm)");
                return;
            }
        }

        [HarmonyPatch(typeof(Ship), "AddShipTurretArmor", new Type[] { typeof(Part) })]
        static class PatchPart
        {
            static void Postfix(Ship __instance, Part __0) { try { Apply(__instance, __0 != null ? __0.data : null); } catch (Exception e) { if (errors++ < 5) Log(e.Message); } }
        }
        [HarmonyPatch(typeof(Ship), "AddShipTurretArmor", new Type[] { typeof(PartData), typeof(Ship) })]
        static class PatchData
        {
            static void Postfix(Ship __instance, PartData __0) { try { Apply(__instance, __0); } catch (Exception e) { if (errors++ < 5) Log(e.Message); } }
        }
    }
}
