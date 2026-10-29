// 1.11.394 (author 29.10 18:08-18:28: "do the ships the player designs for custom battles go to the shared designs too? - yes, add
// that"; "make the computer prefer the players' designs"; "the players' designs always on top"):
//  - a ship the player finishes in the custom battle constructor (GameManager.CustomBattleConstructorFinished - not when he
//    abandons it) is saved to the shared designs the way the game's own "save design" button does it (Ui.SaveSharedDesign),
//    for its nation and year. Switch: CustomBattleSaveDesigns.
//  - every design the player saves (that way or with the button in the shared-designs constructor) is noted as his in
//    UserData\IronAdmiral\player_designs.csv (id, nation, type, year, name).
//  - when a nation takes a shared design - in the campaign (CampaignController.GetSharedDesign) and in custom battles
//    (TweaksAndFixes' own call) - the player's designs of that nation and type come first, newest first, up to
//    PlayerDesignsYearsBack years old, technology permitting in the campaign; then the historical classes, then the rest.
//    Switch: PlayerDesignsFirst.
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PlayerDesigns
    {
        internal static MelonPreferences_Entry<bool> Save, First;
        internal static MelonPreferences_Entry<int> YearsBack;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Save = cat.CreateEntry("CustomBattleSaveDesigns", true, null, "Custom battles: every ship you finish in the constructor is saved to the shared designs (as with the save button), for its nation and year");
            First = cat.CreateEntry("PlayerDesignsFirst", true, null, "Your own designs (saved to the shared designs) are chosen first when a nation picks a shared design - in the campaign and in custom battles - before the historical classes and other designs");
            YearsBack = cat.CreateEntry("PlayerDesignsYearsBack", 12, null, "How many years back one of your designs may still be chosen by a nation");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][PlayerDesigns] " + m); }
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "player_designs.csv"); } }

        static HashSet<string> ids;
        internal static HashSet<string> Ids()
        {
            if (ids != null) return ids;
            ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath)) { var c = l.Split(','); if (c.Length >= 1 && c[0].Trim().Length > 30) ids.Add(c[0].Trim()); } }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][PlayerDesigns] list: " + e.Message); }
            Log($"{ids.Count} design(s) of the player known");
            return ids;
        }
        static void Note(Ship s, string how)
        {
            try
            {
                if (s == null) return;
                string id = s.id.ToString(); if (id.Length < 30 || Ids().Contains(id)) return;
                ids.Add(id);
                string nation = "", type = "", name = ""; int year = 0;
                try { nation = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
                try { type = s.shipType != null ? s.shipType.name : ""; } catch { }
                try { name = (s.vesselName ?? "").Replace(',', ' '); } catch { }
                try { year = (int)s.GetYear(s); } catch { }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, $"{id},{nation},{type},{year},{name}{Environment.NewLine}");
                Log($"{how}: {name} ({nation} {type} {year}) noted as the player's design - nations take it first");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][PlayerDesigns] note: " + e.Message); }
        }

        internal static Ship Pick(Player player, ShipType shipType, int year, bool checkTech, System.Collections.Generic.List<Il2CppSystem.Guid> ignore)
        {
            if (First == null || !First.Value) return null;
            return HistPriority.PickSet(Ids(), "player's design", "PlayerDesigns", player, shipType, year, checkTech, ignore, YearsBack.Value);
        }

        static bool saving;
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.CustomBattleConstructorFinished))]
        static class Patch_Finished
        {
            static void Prefix()
            {
                try
                {
                    if (Save == null || !Save.Value || saving) return;
                    Ship s = null; try { s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
                    if (s == null || G.ui == null) return;
                    saving = true;
                    G.ui.SaveSharedDesign(null);
                    Log($"custom battle design {s.vesselName} saved to the shared designs");
                    Note(s, "custom battle constructor");
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][PlayerDesigns] save: " + e.Message); }
                finally { saving = false; }
            }
        }

        // the game's own save button (shared-designs constructor): the saved design is the player's too
        [HarmonyPatch(typeof(Ui), nameof(Ui.SaveSharedDesign))]
        static class Patch_Saved
        {
            static void Postfix()
            {
                if (saving) return;
                try { Note(PlayerController.Instance != null ? PlayerController.Instance.Ship : null, "saved with the button"); } catch { }
            }
        }
    }
}
