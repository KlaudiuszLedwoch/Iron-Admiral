// 1.11.364 (#81, player screenshot 29.10: a pre-dreadnought with guns stacked in three levels, no drawback in the designer).
// Superposed turrets were tried on the US Kearsarge (1898) and Virginia (1904) classes: an 8 in turret fixed on top of the main
// turret. Both levels trained together, and on the Virginias the blast of one set of guns disrupted the crew of the other; the
// arrangement was regarded as a failure. The game's designer and battle know nothing of it.
// Rule (author 29.10: a penalty, not a ban): in battle, a gun that has another gun mounted almost directly above it (horizontal
// offset under StackedGunsOffset m, at least 1 m higher) reloads slower by StackedGunsReloadPenalty for every gun above it -
// blast over its gunhouse and crew. Ordinary superfiring turrets stand several metres fore or aft of the lower one and are not
// affected. One log line per ship with stacked guns.
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
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Penalty, Offset;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("StackedGunsPenalty", true, null, "Gunnery: a gun with another gun mounted almost directly above it (superposed turrets, as on the US Kearsarge and Virginia classes) reloads slower - the blast of the upper guns disturbs its crew; ordinary superfiring turrets are not affected");
            Penalty = cat.CreateEntry("StackedGunsReloadPenalty", 0.25f, null, "Gunnery: reload time added to a gun for every gun mounted almost directly above it (0.25 = +25 %)");
            Offset = cat.CreateEntry("StackedGunsOffset", 3f, null, "Gunnery: largest horizontal distance (m) between two guns for the upper one to count as mounted above the lower one");
        }

        // ship -> (lower gun part -> number of guns above it); built once per ship and battle
        static readonly Dictionary<IntPtr, Dictionary<IntPtr, int>> cache = new Dictionary<IntPtr, Dictionary<IntPtr, int>>();
        static IntPtr battle;
        static Dictionary<IntPtr, int> For(Ship s)
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            IntPtr bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != battle) { battle = bp; cache.Clear(); }
            if (cache.TryGetValue(s.Pointer, out var m)) return m;
            m = new Dictionary<IntPtr, int>(); cache[s.Pointer] = m;
            try
            {
                var parts = s.parts; var st = s.transform; if (parts == null || st == null) return m;
                var guns = new List<(Part p, Vector3 v, string n)>();
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; var pd = p != null ? p.data : null; if (pd == null || !pd.isGun || p.transform == null) continue;
                    guns.Add((p, st.InverseTransformPoint(p.transform.position), pd.name));
                }
                float off = Offset != null ? Offset.Value : 3f; int pairs = 0; var names = new List<string>();
                foreach (var lo in guns)
                {
                    int above = 0;
                    foreach (var hi in guns)
                    {
                        if (hi.p.Pointer == lo.p.Pointer) continue;
                        float dx = hi.v.x - lo.v.x, dz = hi.v.z - lo.v.z;
                        if (hi.v.y - lo.v.y >= 1f && dx * dx + dz * dz <= off * off) above++;
                    }
                    if (above > 0) { m[lo.p.Pointer] = above; pairs += above; names.Add($"{lo.n} x{above}"); }
                }
                if (m.Count > 0) MelonLogger.Msg($"[DIP GG Fixes][Gunnery] {s.name}: superposed guns - {m.Count} gun(s) with guns mounted above them ({string.Join(", ", names)}): reload +{(Penalty != null ? Penalty.Value : 0.25f) * 100f:0} % per gun above (blast over the lower gunhouse)");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Gunnery] superposed guns: " + e.Message); }
            return m;
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
                    if (For(s).TryGetValue(__instance.Pointer, out int n) && n > 0) __result *= 1f + Penalty.Value * n;
                }
                catch { }
            }
        }
    }
}
