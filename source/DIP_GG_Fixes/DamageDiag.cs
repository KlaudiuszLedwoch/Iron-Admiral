// 1.11.276 (#33 step 3c, author 25.10, 18:06: with the physical penetration and the real plate the damage of a full penetration
// tripled - 12 in into a destroyer 3560, 5 in 260): the game's damage per hit seems to grow with the margin of penetration over
// the plate. To replace that with a burst-charge model the game's own computation must be known. Diagnostics only: the game's
// static GetExplosionDamage(float, float, float, float, float) is logged (arguments and result) for the first calls of a battle,
// together with the shell that is being resolved, and the gun's base damage (Part.WeaponDamageCrude) goes into the hit log.
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DamageDiag
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DamageDiagLog", true, null, "Battle log: the game's explosion-damage computation (arguments and result) for the first 60 hits of a battle, with the shell being resolved - for the calibration of the damage model");
        }
        static int logged; internal static string Owner = "?";
        internal static void BattleStart() { logged = 0; }
        internal static MethodBase Find()
        {
            foreach (var t in new[] { typeof(Ship), typeof(Shell), typeof(Part), typeof(Projectile), typeof(BattleManager), typeof(Ui), typeof(PartData), typeof(GameData) })
            {
                try
                {
                    var m = t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).FirstOrDefault(x => x.Name == "GetExplosionDamage" && x.GetParameters().Length == 5);
                    if (m != null) { Owner = t.Name; return m; }
                }
                catch { }
            }
            return null;
        }
        internal static void Log(float a, float b, float c, float d, float e, float result)
        {
            if (On == null || !On.Value || logged >= 60) return;
            logged++;
            string shell = "?";
            try
            {
                var cur = PhysPen.Current;
                if (cur != null) { var sh = PhysPen.ShotOf(cur); if (sh != null) shell = $"{sh.G.CalMm:0.#} mm {cur.shellType} {sh.G.Kg:0.#} kg at {sh.Range:0} m, {sh.Vs:0} m/s, phys belt {PhysPen.BeltPen(sh.G.Kg, sh.Vs, sh.Fall, sh.G.Q, sh.G.CalMm):0} mm; gun base damage crude {GunBase(sh):0.#} x bonus {GunBonus(sh):0.###}"; }
            }
            catch { }
            MelonLogger.Msg($"[DIP GG Fixes][Damage] {Owner}.GetExplosionDamage({a:0.###}, {b:0.###}, {c:0.###}, {d:0.###}, {e:0.###}) = {result:0.###}; shell {shell}");
        }
        static float GunBase(PhysPen.Shot sh) { try { return Part.WeaponDamageCrude(sh.Pd, sh.Shooter); } catch { return 0f; } }
        static float GunBonus(PhysPen.Shot sh) { try { return Part.WeaponDamageTotalBonus(sh.Pd, sh.Shooter); } catch { return 0f; } }
    }

    [HarmonyPatch]
    internal static class Patch_ExplosionDamage_Diag
    {
        static MethodBase TargetMethod() { return DamageDiag.Find(); }
        static bool Prepare() { return DamageDiag.Find() != null; }
        static void Postfix(float __0, float __1, float __2, float __3, float __4, float __result) { try { DamageDiag.Log(__0, __1, __2, __3, __4, __result); } catch { } }
    }
}
