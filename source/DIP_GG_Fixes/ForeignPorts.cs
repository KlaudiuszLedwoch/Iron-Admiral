// 1.11.134 (beta): shipbuilding capacity from ports outside the home provinces. The base game counts only the ports of the
// home provinces; conquered provinces and colonies with ports add nothing. Here they add a share (ForeignPortCapacity,
// default 0.5) of what a home port of the same size gives: port capacity x shipbuilding_capacity_ratio x share x the
// nation's shipbuilding capacity penalty. Each port counts once. Applies to the player and the AI alike. 0 = off.
// (Idea taken from UADVanillaPlus, foreign-port capacity.)
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ForeignPorts
    {
        internal static MelonPreferences_Entry<float> Share;
        class C { public float Extra; public float Until; public int Month; public int Ports; }
        static readonly Dictionary<IntPtr, C> cache = new Dictionary<IntPtr, C>();
        static readonly HashSet<string> logged = new HashSet<string>();
        static int errors; static bool busy;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Share = cat.CreateEntry("ForeignPortCapacity", 0.5f, null, "Shipbuilding capacity: ports in conquered provinces and colonies add this share of what a home port of the same size gives (0 = off, base game)");
        }

        internal static void Post(Player p, ref float result)
        {
            if (busy || Share == null || Share.Value <= 0f || p == null) return;
            try
            {
                int month = 0; try { month = Fog.Month(); } catch { }
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (!cache.TryGetValue(p.Pointer, out var c) || c.Month != month || now > c.Until)
                {
                    busy = true;
                    try { c = Compute(p); } finally { busy = false; }
                    c.Month = month; c.Until = now + 10f; cache[p.Pointer] = c;
                    string key = $"{p.Pointer}|{month}|{c.Ports}";
                    if (c.Extra > 0f && logged.Add(key) && logged.Count < 400)
                        MelonLogger.Msg($"[DIP GG Fixes][Economy] {p.data?.name}: {c.Ports} port(s) outside the home provinces add {c.Extra:0} t of shipbuilding capacity");
                }
                result += c.Extra;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Economy] foreign port capacity: " + e.Message); }
        }

        static C Compute(Player p)
        {
            var c = new C();
            var home = new HashSet<IntPtr>();
            var hp = p.homeProvinces;
            if (hp != null) for (int i = 0; i < hp.Count; i++) { var pr = hp[i]; if (pr == null || pr.Ports == null) continue; for (int j = 0; j < pr.Ports.Count; j++) { var pe = pr.Ports[j]; if (pe != null) home.Add(pe.Pointer); } }
            var seen = new HashSet<IntPtr>(); float cap = 0f;
            var wp = p.provincesWithPort;
            if (wp != null) for (int i = 0; i < wp.Count; i++)
                {
                    var pr = wp[i]; if (pr == null || pr.Ports == null) continue;
                    for (int j = 0; j < pr.Ports.Count; j++)
                    {
                        var pe = pr.Ports[j]; if (pe == null || home.Contains(pe.Pointer) || !seen.Add(pe.Pointer)) continue;
                        int pc = 0; try { pc = pe.GetPortCapacity(); } catch { }
                        if (pc > 0) { c.Ports++; cap += pc; }
                    }
                }
            if (cap <= 0f) return c;
            float ratio = 0.11f; try { ratio = MonoBehaviourExt.Param("shipbuilding_capacity_ratio", 0.11f); } catch { }
            float pen = 1f; try { pen = p.shipbuildingCapacityPenalty; } catch { }
            c.Extra = Math.Max(0f, cap * ratio * Share.Value * pen);
            return c;
        }
    }

    [HarmonyPatch]
    internal static class Patch_ForeignPortCapacity
    {
        static MethodBase TargetMethod() { return SafePatches.Find(typeof(Player), "ShipbuildingCapacityLimit", Type.EmptyTypes); }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Player __instance, ref float __result) { ForeignPorts.Post(__instance, ref __result); }
    }
}
