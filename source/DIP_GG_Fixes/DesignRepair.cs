// 1.11.103: ships of the player whose design is not in the player's design list (prizes taken before 1.11.6, ships received
// in other ways) cannot be refitted - the refit starts from the ship's design in the list. 22.10: a captured French BB of the
// Magnanime class had no design in the German list. Once per loaded campaign (a few seconds after the map appears) every such
// ship gets a copy of its design in the player's list (one copy per foreign design), the same way prizes do since 1.11.6.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignRepair
    {
        internal static MelonPreferences_Entry<bool> On;
        static IntPtr session; static float due = -1f; static bool done;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("RepairMissingDesigns", true, null, "Campaign: the player's ships whose design is missing from the design list (e.g. old prizes) get a copy of their design, so they can be refitted");
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            bool map = false; try { map = GameManager.IsWorldMap; } catch { }
            if (!map) return;
            var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) return;
            var ptr = cc.CampaignData.Pointer;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (ptr != session) { session = ptr; done = false; due = now + 5f; }
            if (done || now < due) return;
            done = true;
            try { Run(); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignRepair] " + e.Message); }
        }

        static void Run()
        {
            Player main = null;
            var ps = CampaignController.Instance.CampaignData.Players;
            for (int i = 0; i < ps.Count; i++) if (ps[i] != null && ps[i].isMain) { main = ps[i]; break; }
            if (main == null) return;
            var inList = new HashSet<IntPtr>();
            var en = main.designsAll.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (un.MoveNext()) { var x = en.Current; if (x != null) inList.Add(x.Pointer); }
            var ships = new List<Ship>();
            var fe = main.fleetAll.GetEnumerator(); var fu = fe.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (fu.MoveNext()) { var s = fe.Current; if (s != null) ships.Add(s); }
            var pc = PlayerController.Instance; if (pc == null) return;
            var cloned = new Dictionary<IntPtr, Ship>();
            int fixedN = 0, checkedN = 0;
            foreach (var s in ships)
            {
                checkedN++;
                Ship d = null; try { d = s.design; } catch { }
                if (d == null || inList.Contains(d.Pointer)) continue;
                try
                {
                    if (!cloned.TryGetValue(d.Pointer, out var c))
                    {
                        c = pc.CloneDesign(d, main);
                        cloned[d.Pointer] = c;
                        if (c != null) inList.Add(c.Pointer);
                        MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {s.name}: design {d.name} (owner {(d.player != null ? d.player.data.name : "?")}) was not in the {main.data.name} design list - copied as {(c != null ? c.name : "null")}");
                    }
                    if (c != null) { s.design = c; fixedN++; }
                }
                catch (Exception e) { MelonLogger.Warning($"[DIP GG Fixes][DesignRepair] {s.name}: " + e.Message); }
            }
            MelonLogger.Msg($"[DIP GG Fixes][DesignRepair] {main.data.name}: {checkedN} ships checked, {fixedN} given a design in the list ({cloned.Count} design copies)");
        }
    }
}
