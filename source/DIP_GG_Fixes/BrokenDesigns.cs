// 1.11.308 (#32, author 26.10: "this design is broken" - British BC "Resistance (Sep. 1922)" with its superstructure and turrets
// hanging in the air): an AI design whose ships come into battle with parts that find no mount on their hull (the parts spawn
// about 990 m under the sea and the battle check puts them back) is an old design from the save that no longer fits the hull's
// mount table. Such a design is marked broken at the battle check and the AI builds no more ships from it (the game's build check
// PlayerController.CanBuildShipsFromDesign answers "cannot build"), so it designs a new one; ships already built stay in service.
// The marks are kept in the campaign's mod save ("BD|nation|design"). The player's own designs are never marked.
// Switch: BrokenDesignsObsolete (F10, Campaign).
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class BrokenDesigns
    {
        internal static MelonPreferences_Entry<bool> On;
        static readonly HashSet<string> broken = new HashSet<string>();
        static readonly HashSet<string> blockLogged = new HashSet<string>();
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Designs] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BrokenDesignsObsolete", true, null, "Campaign: an AI design whose ships come into battle with parts that have no mount on the hull (turrets and superstructure in the air or under the sea) is not built any more - the AI designs a new one; ships already built stay");
        }

        static string Key(Ship d)
        {
            try { if (d == null || d.player == null) return null; return Economy.Key(d.player) + "|" + d.name; } catch { return null; }
        }

        // called by the battle check for a ship with parts that stayed without a mount
        internal static void Mark(Ship s)
        {
            try
            {
                if (On == null || !On.Value || s == null) return;
                bool main = false; try { main = s.player != null && s.player.isMain; } catch { }
                if (main) return;
                var d = s.design; if (d == null) return;
                string k = Key(d); if (k == null) return;
                if (broken.Add(k)) Log($"design {d.name} ({Economy.Name(s.player)}) marked broken: {s.name} came into battle with parts that have no mount on its hull - no more ships are built from it, the AI designs a new one");
            }
            catch { }
        }

        internal static void Check(Ship d, ref bool result, ref string reason)
        {
            try
            {
                if (!result || On == null || !On.Value || d == null || broken.Count == 0) return;
                string k = Key(d); if (k == null || !broken.Contains(k)) return;
                bool main = false; try { main = d.player != null && d.player.isMain; } catch { }
                if (main) return;
                result = false; reason = "Design does not fit its hull (parts without mounts)";
                if (blockLogged.Add(k)) Log($"design {d.name} ({Economy.Name(d.player)}): building refused - broken design");
            }
            catch { }
        }

        internal static void Reset() { broken.Clear(); blockLogged.Clear(); }
        internal static bool Parse(string line)
        {
            if (line == null || !line.StartsWith("BD|")) return false;
            string k = line.Substring(3); if (k.Length > 0) broken.Add(k);
            return true;
        }
        internal static IEnumerable<string> Serialize() { foreach (var k in broken) yield return "BD|" + k; }
    }
}
