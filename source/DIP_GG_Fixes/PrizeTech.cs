// 1.11.333 (#50, player report hurepoix 26.10: a captured, more advanced US ship was refitted, then copied, and new ships were
// built with technologies China had not researched - 5 in Mk IV guns against its own Mk 2, better armour and fire control).
// The mod gives the captor a copy of the prize's design (PrizeDesign.Ensure, 1.11.x) so the ship can be refitted and repaired.
// The parts of that design the captor's nation has not researched are noted when the design is copied. The prize itself may keep
// them (a refit of the captured ship), but a new design - or a copy of the prize design - with such a part is not valid until the
// nation has researched it: the designer shows the part in red with the reason. Applies to the player; the AI does not copy prizes.
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PrizeTech
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("CapturedTechNotCopied", true, null, "Ship design: parts of a captured ship that your nation has not researched stay on that ship (refits keep them), but a new design or a copy of the prize design cannot use them until you research them");
        }
        static readonly HashSet<string> foreign = new HashSet<string>(StringComparer.Ordinal);   // "nation|part"
        static bool loaded; static int logs;
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "prize_parts.txt"); } }
        static void Load()
        {
            if (loaded) return; loaded = true;
            try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath)) if (l.Contains("|")) foreign.Add(l.Trim()); } catch { }
        }
        static void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllLines(FilePath, foreign); } catch { } }

        // called when a prize design is copied to the captor
        internal static void Note(Ship design, Player captor)
        {
            if (On == null || !On.Value || design == null || captor == null) return;
            try
            {
                bool main = false; try { main = captor.isMain; } catch { }
                if (!main) return;
                Load();
                string nation = captor.data != null ? captor.data.name : "?";
                var names = new List<string>();
                var ps = design.parts; if (ps == null) return;
                for (int i = 0; i < ps.Count; i++)
                {
                    var pd = ps[i] != null ? ps[i].data : null; if (pd == null) continue;
                    bool av = true; try { av = Ship.IsPartAvailable(pd, captor, design.shipType, design); } catch { }
                    if (!av && foreign.Add(nation + "|" + pd.name)) names.Add(pd.name);
                }
                if (names.Count > 0) { Save(); MelonLogger.Msg($"[DIP GG Fixes][Capture] {design.name}: {names.Count} part(s) not researched by {nation} - kept on the prize, not usable in new designs: {string.Join(", ", names)}"); }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Capture] prize parts: " + e.Message); }
        }

        internal static void Check(Ship s, ref bool result, ref Il2CppSystem.Collections.Generic.Dictionary<Part, string> partErr, ref string reason)
        {
            if (On == null || !On.Value || s == null) return;
            Load(); if (foreign.Count == 0) return;
            bool camp = false, cons = false; try { camp = GameManager.IsCampaign; cons = GameManager.IsConstructor && !GameManager.IsSharedDesignConstructor; } catch { }
            if (!camp || !cons) return;
            var p = s.player; if (p == null) return;
            bool main = false; try { main = p.isMain; } catch { } if (!main) return;
            bool refit = false; try { refit = s.isRefitDesign || s.isRefit; } catch { }
            if (refit) return;
            string nation = p.data != null ? p.data.name : "?";
            var ps = s.parts; if (ps == null) return;
            for (int i = 0; i < ps.Count; i++)
            {
                var part = ps[i]; var pd = part != null ? part.data : null; if (pd == null) continue;
                if (!foreign.Contains(nation + "|" + pd.name)) continue;
                bool av = true; try { av = Ship.IsPartAvailable(pd, p, s.shipType, s); } catch { }
                if (av) continue;
                result = false;
                const string why = "captured technology - not researched by your nation";
                try { if (partErr == null) partErr = new Il2CppSystem.Collections.Generic.Dictionary<Part, string>(); if (!partErr.ContainsKey(part)) partErr.Add(part, why); } catch { }
                if (string.IsNullOrEmpty(reason)) reason = why;
                if (logs++ < 10) MelonLogger.Msg($"[DIP GG Fixes][Capture] design {s.name}: {pd.name} is captured technology {nation} has not researched - not valid in a new design");
            }
        }
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.IsValidCostReqParts))]
    internal static class Patch_IsValidCostReqParts_PrizeTech
    {
        static void Postfix(Ship __instance, ref bool __result, ref string __0, ref Il2CppSystem.Collections.Generic.Dictionary<Part, string> __2)
        {
            try { PrizeTech.Check(__instance, ref __result, ref __2, ref __0); } catch { }
        }
    }
}
