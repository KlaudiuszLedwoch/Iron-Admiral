// 1.11.364 (#81, player screenshot 29.10: a pre-dreadnought with guns stacked in three levels, no drawback in the designer).
// 1.11.390: the drawbacks reworked to those confirmed in the sources on the US Kearsarge class (1898), the only ships built with
// an 8 in turret fixed on top of the 13 in turret (the Virginias repeated it):
//  - the upper turret could not train independently: it turned with the lower one, so it could only follow the lower guns'
//    aim (USNI Naval History, "Double Turret") -> the upper gun's hit chance is lowered by StackedGunsAimPenalty;
//  - the ammunition hoists for the upper guns ran through the lower gunhouse and cramped the work of the lower guns
//    (GlobalSecurity, BB-5 Kearsarge) -> the lower gun reloads slower by StackedGunsReloadPenalty per gun above it;
//  - one hit could silence both turrets, half the main armament, at once (USNI) -> when the lower gun is ruined, the guns
//    above it are silenced as well (they cannot train any more).
// Ordinary superfiring turrets stand several metres fore or aft of the lower one and are not affected. One log line per ship.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class StackedGuns
    {
        internal static MelonPreferences_Entry<bool> On, Silence;
        internal static MelonPreferences_Entry<float> Penalty, Offset, AimPenalty;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("StackedGunsPenalty", true, null, "Gunnery: superposed guns (a gun mounted almost directly on top of another, as on the US Kearsarge class) - the upper gun trains with the lower one and aims worse, the hoists through the lower gunhouse slow its reload, and a hit that ruins the lower gun silences the upper one; ordinary superfiring turrets are not affected");
            Penalty = cat.CreateEntry("StackedGunsReloadPenalty", 0.25f, null, "Gunnery: reload time added to a lower gun for every gun mounted above it - the upper guns' ammunition hoists cramp its gunhouse (0.25 = +25 %)");
            AimPenalty = cat.CreateEntry("StackedGunsAimPenalty", 0.15f, null, "Gunnery: hit chance taken off an upper gun of a superposed pair - it cannot train on its own and follows the lower guns' aim (0.15 = -15 %)");
            Silence = cat.CreateEntry("StackedGunsSilence", true, null, "Gunnery: when the lower gun of a superposed pair is ruined, the gun above it is silenced too - they turn on one mounting");
            Offset = cat.CreateEntry("StackedGunsOffset", 3f, null, "Gunnery: largest horizontal distance (m) between two guns for the upper one to count as mounted above the lower one");
        }

        class Info
        {
            internal readonly Dictionary<IntPtr, int> Above = new Dictionary<IntPtr, int>();        // lower gun -> guns above it
            internal readonly Dictionary<IntPtr, List<Part>> Below = new Dictionary<IntPtr, List<Part>>(); // upper gun -> guns below it
            internal readonly HashSet<IntPtr> Silenced = new HashSet<IntPtr>();
        }

        // built once per ship and battle
        static readonly Dictionary<IntPtr, Info> cache = new Dictionary<IntPtr, Info>();
        static IntPtr battle;
        static Info For(Ship s)
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            IntPtr bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != battle) { battle = bp; cache.Clear(); }
            if (cache.TryGetValue(s.Pointer, out var m)) return m;
            m = new Info(); cache[s.Pointer] = m;
            try
            {
                var parts = s.parts; var st = s.transform; if (parts == null || st == null) return m;
                var guns = new List<(Part p, Vector3 v, string n)>();
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; var pd = p != null ? p.data : null; if (pd == null || !pd.isGun || p.transform == null) continue;
                    guns.Add((p, st.InverseTransformPoint(p.transform.position), pd.name));
                }
                float off = Offset != null ? Offset.Value : 3f; var names = new List<string>();
                foreach (var lo in guns)
                {
                    int above = 0;
                    foreach (var hi in guns)
                    {
                        if (hi.p.Pointer == lo.p.Pointer) continue;
                        float dx = hi.v.x - lo.v.x, dz = hi.v.z - lo.v.z;
                        if (hi.v.y - lo.v.y >= 1f && dx * dx + dz * dz <= off * off)
                        {
                            above++;
                            if (!m.Below.TryGetValue(hi.p.Pointer, out var l)) m.Below[hi.p.Pointer] = l = new List<Part>();
                            l.Add(lo.p);
                        }
                    }
                    if (above > 0) { m.Above[lo.p.Pointer] = above; names.Add($"{lo.n} x{above}"); }
                }
                if (m.Above.Count > 0) MelonLogger.Msg($"[DIP GG Fixes][Gunnery] {s.name}: superposed guns - {m.Above.Count} gun(s) with guns mounted above them ({string.Join(", ", names)}): lower reload +{(Penalty != null ? Penalty.Value : 0.25f) * 100f:0} % per gun above (hoists), upper hit chance -{(AimPenalty != null ? AimPenalty.Value : 0.15f) * 100f:0} % (no independent training){(Silence == null || Silence.Value ? ", upper silenced when the lower is ruined" : "")}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Gunnery] superposed guns: " + e.Message); }
            return m;
        }

        // the upper gun is silenced when a gun below it is ruined
        static bool Silenced(Ship s, Info m, Part upper)
        {
            if (Silence == null || !Silence.Value) return false;
            if (m.Silenced.Contains(upper.Pointer)) return true;
            if (!m.Below.TryGetValue(upper.Pointer, out var lows)) return false;
            var pdm = s.partsDamage; if (pdm == null) return false;
            foreach (var lo in lows)
            {
                if (lo == null || !pdm.ContainsKey(lo)) continue;
                var d = pdm[lo];
                if (d != null && d.isRuined)
                {
                    m.Silenced.Add(upper.Pointer);
                    MelonLogger.Msg($"[DIP GG Fixes][Gunnery] {s.name}: {(upper.data != null ? upper.data.name : "upper gun")} silenced - the gun below it ({(lo.data != null ? lo.data.name : "?")}) is ruined and the superposed mounting cannot train");
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(Part), nameof(Part.WeaponReloadTime))]
        static class Patch_Reload
        {
            static void Postfix(Part __instance, ref float __result)
            {
                try
                {
                    if (On == null || !On.Value || __instance == null || !GameManager.IsBattle) return;
                    var s = __instance.ship; if (s == null) return;
                    var m = For(s);
                    if (m.Above.TryGetValue(__instance.Pointer, out int n) && n > 0) __result *= 1f + Penalty.Value * n;
                    if (m.Below.Count > 0 && Silenced(s, m, __instance)) __result = Mathf.Max(__result, 3600f);
                }
                catch { }
            }
        }

        // Ship.HitChance(dynamic, from, fromPoint, target, targetPoint, gun, gunData, ...): argument 5 is the firing gun
        [HarmonyPatch(typeof(Ship), nameof(Ship.HitChance))]
        static class Patch_Aim
        {
            [HarmonyPriority(Priority.Low)]
            static void Postfix(Ship __1, Part __5, ref float __result)
            {
                try
                {
                    if (On == null || !On.Value || __1 == null || __5 == null || !GameManager.IsBattle) return;
                    var m = For(__1); if (m.Below.Count == 0 || !m.Below.ContainsKey(__5.Pointer)) return;
                    if (Silenced(__1, m, __5)) { __result = 0f; return; }
                    __result *= Mathf.Clamp01(1f - AimPenalty.Value);
                }
                catch { }
            }
        }
    }
}
