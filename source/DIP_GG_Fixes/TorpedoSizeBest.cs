// 1.11.296 (#47, part 1): the ship generator draws the ship's components at random by weight (Ship.GetComponentsToInstall), so a
// design could get an old torpedo diameter while a newer one was available (a player report: light cruisers with 19 in torpedoes
// while 24 in were researched). After the draw, the torpedo diameter is replaced by the largest one the ship may use
// (Ship.IsComponentAvailable - nation, year, technology). The generator then places parts and checks the weight as usual.
// Only generated designs are affected - the player's own designs in the designer keep the player's choice.
using System;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoSizeBest
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiBestTorpedoDiameter", true, null, "Generated designs (AI and auto-design) get the largest torpedo diameter the nation has researched instead of a random older one");
        }
        static int logged;
        static int Index(string name)
        {
            // torpedo_diameter_N
            int i = name.LastIndexOf('_'); int n; return i >= 0 && int.TryParse(name.Substring(i + 1), out n) ? n : -1;
        }
        internal static void Apply(Ship s, Il2CppSystem.Collections.Generic.List<ComponentData> list)
        {
            if (On == null || !On.Value || s == null || list == null) return;
            int at = -1; ComponentData cur = null;
            for (int i = 0; i < list.Count; i++) { var c = list[i]; if (c != null && c.type == "torpedo_size") { at = i; cur = c; break; } }
            if (at < 0) return;
            ComponentData best = cur; int bi = Index(cur.name);
            var all = G.GameData.components;
            foreach (var kv in all)
            {
                var c = kv.Value; if (c == null || c.type != "torpedo_size") continue;
                int k = Index(c.name); if (k <= bi) continue;
                bool ok = false; try { ok = s.IsComponentAvailable(c); } catch { }
                if (ok) { best = c; bi = k; }
            }
            if (best == cur) return;
            list[at] = best;
            if (logged++ < 20) MelonLogger.Msg($"[DIP GG Fixes][ShipGen] {(s.shipType != null ? s.shipType.name : "?")} {(s.player != null ? s.player.data.name : "?")}: torpedo diameter {cur.name} -> {best.name} (largest available)");
        }
    }

    [HarmonyPatch(typeof(Ship), "GetComponentsToInstall")]
    internal static class Patch_GetComponentsToInstall_Torpedo
    {
        static int fails;
        static void Postfix(Ship __instance, Il2CppSystem.Collections.Generic.List<ComponentData> __result)
        {
            try { TorpedoSizeBest.Apply(__instance, __result); }
            catch (Exception e) { if (fails++ < 3) MelonLogger.Warning("[DIP GG Fixes][ShipGen] torpedo diameter: " + e.Message); }
        }
    }
}
