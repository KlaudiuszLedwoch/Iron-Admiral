// 1.11.365 (#79, player report 28.10: "the TR have 15 knot speed no matter what the era - with 1890s combat units frequently only
// doing 16-17 knots, this is problematic"). The game's ship type tr has one speed band (11-14 kn, shipTypes.csv) for every year.
// In battle a transport's top speed is now capped by the year of its design: up to 1899 9 kn, 1900-1909 10, 1910-1919 11,
// 1920-1929 12, from 1930 14 kn (setting TransportSpeedByYear; the caps are a first estimate for cargo ships and transports, to
// be checked against historical convoy speeds), with up to 1 kn less for some ships so a convoy is not one block. Warships are
// not affected. One log line per transport.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TransportSpeed
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TransportSpeedByYear", true, null, "Battle: transports are as fast as cargo ships of their year (up to 1899 9 kn, 1900s 10, 1910s 11, 1920s 12, from 1930 14 kn, some up to 1 kn slower) instead of 11-14 kn in every era");
        }
        static readonly Dictionary<IntPtr, float> cap = new Dictionary<IntPtr, float>();   // ship -> cap in m/s (0 = none)
        static IntPtr battle; static int logged;
        internal static float CapKn(float year)
        {
            if (year < 1900f) return 9f; if (year < 1910f) return 10f; if (year < 1920f) return 11f; if (year < 1930f) return 12f; return 14f;
        }
        static float CapFor(Ship s)
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            IntPtr bp = b != null ? b.Pointer : IntPtr.Zero; if (bp != battle) { battle = bp; cap.Clear(); logged = 0; }
            if (cap.TryGetValue(s.Pointer, out var c)) return c;
            c = 0f;
            try
            {
                var t = s.shipType;
                if (t != null && t.name == "tr")
                {
                    float y = s.GetYear(s); if (y <= 0f) y = 1910f;
                    float kn = CapKn(y) - (Math.Abs(s.Pointer.ToInt64() / 16) % 3) * 0.5f;   // 0, 0.5 or 1 kn less
                    c = kn / 1.94384f;
                    if (logged++ < 12) MelonLogger.Msg($"[DIP GG Fixes][Convoy] {s.name} ({y:0}): transport top speed {kn:0.0} kn for its year (design {s.speedMax * 1.94384f:0.0} kn)");
                }
            }
            catch { }
            cap[s.Pointer] = c; return c;
        }

        // 1.11.366 (#79 step 2, author 29.10: a mixed convoy): the game arms transports by national technology (armed_transports), so a
        // convoy is armed or unarmed as a whole. Now only part of the armed transports of each side keep their guns in battle - about
        // TransportArmedShare of them (at least one: the auxiliary cruiser of the convoy); the others do not fire.
        internal static MelonPreferences_Entry<float> ArmedShare;
        internal static void CreateArmedPrefs(MelonPreferences_Category cat)
        {
            ArmedShare = cat.CreateEntry("TransportArmedShare", 0.35f, null, "Battle: share of a convoy's armed transports that keep their guns (at least one per side, like an auxiliary cruiser); the others are merchant ships that do not fire (0 = off, 1 = all armed as before)");
        }
        static readonly HashSet<IntPtr> silent = new HashSet<IntPtr>();
        static IntPtr armBattle;
        static bool Silent(Ship s)
        {
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null;
            if (b == null) return false;
            if (b.Pointer != armBattle)
            {
                armBattle = b.Pointer; silent.Clear();
                try
                {
                    float share = ArmedShare != null ? ArmedShare.Value : 1f;
                    if (share < 1f && share > 0f)
                    {
                        var bySide = new Dictionary<IntPtr, List<Ship>>();
                        var all = b.Ships;
                        for (int i = 0; all != null && i < all.Count; i++)
                        {
                            var x = all[i]; var t = x != null ? x.shipType : null; if (t == null || t.name != "tr" || x.player == null) continue;
                            bool armed = false; var ps = x.parts;
                            for (int j = 0; ps != null && j < ps.Count; j++) { var pd = ps[j] != null ? ps[j].data : null; if (pd != null && (pd.isGun || pd.isTorpedo)) { armed = true; break; } }
                            if (!armed) continue;
                            if (!bySide.TryGetValue(x.player.Pointer, out var l)) { l = new List<Ship>(); bySide[x.player.Pointer] = l; }
                            l.Add(x);
                        }
                        foreach (var l in bySide.Values)
                        {
                            l.Sort((p, q) => p.Pointer.ToInt64().CompareTo(q.Pointer.ToInt64()));
                            int keep = Math.Max(1, (int)Math.Round(l.Count * share));
                            for (int i = keep; i < l.Count; i++) silent.Add(l[i].Pointer);
                            MelonLogger.Msg($"[DIP GG Fixes][Convoy] {Economy.Name(l[0].player)}: {keep} of {l.Count} armed transport(s) keep their guns, {l.Count - keep} sail as unarmed merchant ships");
                        }
                    }
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Convoy] armed share: " + e.Message); }
            }
            return silent.Contains(s.Pointer);
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.AimAndShoot))]
        static class Patch_AimAndShoot
        {
            static bool Prefix(Ship __instance)
            {
                try { if (__instance != null && GameManager.IsBattle && Silent(__instance)) return false; } catch { }
                return true;
            }
        }

        // 1.11.373 (test 29.10 12:25, 1935: all six US transports carried torpedo tubes and the torpedo-setting logic wanted to fire
        // them - none did in that battle): a transport sailing as an unarmed merchant ship does not fire torpedoes either
        [HarmonyPatch(typeof(Ship), nameof(Ship.allowedToShootTorpedoes), MethodType.Getter)]
        static class Patch_AllowedTorpedoes
        {
            static void Postfix(Ship __instance, ref bool __result)
            {
                try { if (__result && __instance != null && GameManager.IsBattle && Silent(__instance)) __result = false; } catch { }
            }
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.SpeedMax))]
        static class Patch_SpeedMax
        {
            static void Postfix(Ship __instance, ref float __result)
            {
                try
                {
                    if (On == null || !On.Value || __instance == null || !GameManager.IsBattle || __result <= 0f) return;
                    float c = CapFor(__instance);
                    if (c > 0f && __result > c) __result = c;
                }
                catch { }
            }
        }
    }
}
