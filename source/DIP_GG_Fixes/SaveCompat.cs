// 1.11.42: save compatibility check and conversion.
// A campaign save stores ships and designs by the names of their game data (hull, parts, components, technologies, turret
// calibres). When a mod update removes or renames one of them, the game cannot rebuild the ship from the save and the load
// fails or the ship is lost. While a save is being loaded, every ship / design record (Ship.FromStore) and every nation record
// (Player.FromStore) is checked against the game data currently installed; references that no longer exist are removed from the
// record before the game reads it, so the ship loads without the missing item (a removed part, component or technology simply
// is not there; the game's own rules apply to the rest). The save file itself is changed only when the game saves next; before
// the first conversion in a game run all save_*.bin files are copied to UserData/IronAdmiral_SaveBackups/<date>_<version>.
// A missing hull cannot be replaced safely: it is reported, the record is left as it is.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class SaveCompat
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static int ships, players, fixedRecords, removed, hullMissing;
        static readonly Dictionary<string, int> what = new Dictionary<string, int>();
        static readonly List<string> examples = new List<string>();
        static bool backedUp; static float lastActivity; static bool pending;
        static int errors;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SaveCompatibilityFix", true, null,
                "While a save loads, ship designs and nations are checked against the installed game data; parts, components, technologies or turret calibres that no longer exist are removed so the save loads (backup of the saves before the first change). Missing hulls are only reported");
        }

        static GameData GD() { try { return G.GameData; } catch { return null; } }

        // 1.11.45 safety guard: a real data change removes a handful of names. If one kind of check finds many different missing
        // names, the check itself is wrong for this data (1.11.44 removed all 2217 turret calibres) - stop changing that kind.
        static readonly Dictionary<string, HashSet<string>> distinct = new Dictionary<string, HashSet<string>>();
        static readonly HashSet<string> blocked = new HashSet<string>();
        static bool Allow(string kind, string name)
        {
            if (blocked.Contains(kind)) return false;
            HashSet<string> set; if (!distinct.TryGetValue(kind, out set)) { set = new HashSet<string>(); distinct[kind] = set; }
            set.Add(name ?? "null");
            int count; what.TryGetValue(kind, out count);
            if (set.Count > 15 || count >= 300)
            {
                blocked.Add(kind);
                MelonLogger.Warning($"[DIP GG Fixes][SaveCompat] {kind}: {set.Count} different missing names / {count} removals - the check looks wrong for this data, no further {kind} changes in this game run");
                return false;
            }
            return true;
        }

        static void Note(string kind, string name, string owner)
        {
            removed++;
            int n; what.TryGetValue(kind, out n); what[kind] = n + 1;
            if (examples.Count < 12) examples.Add($"{owner}: {kind} '{name}'");
            pending = true; lastActivity = UnityEngine.Time.realtimeSinceStartup;
        }

        internal static void Backup()
        {
            if (backedUp) return; backedUp = true;
            try
            {
                string src = UnityEngine.Application.persistentDataPath;
                string root = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "IronAdmiral_SaveBackups");
                string ver = typeof(SaveCompat).Assembly.GetName().Version.ToString(3);
                string dst = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + ver);
                Directory.CreateDirectory(dst);
                int n = 0;
                foreach (var f in Directory.GetFiles(src, "save_*.bin")) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); n++; }
                MelonLogger.Msg($"[DIP GG Fixes][SaveCompat] backup of {n} save file(s) before conversion: {dst}");
                var dirs = new DirectoryInfo(root).GetDirectories().OrderByDescending(d => d.CreationTimeUtc).ToList();
                for (int i = 10; i < dirs.Count; i++) { try { dirs[i].Delete(true); } catch { } }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][SaveCompat] backup failed: " + e.Message); }
        }

        internal static void FixShip(Ship.Store s)
        {
            if (On == null || !On.Value || s == null) return;
            var gd = GD(); if (gd == null || gd.parts == null) return;
            ships++;
            int before = removed;
            string owner = "?"; try { owner = (s.refitDesignName ?? "") + " " + (s.shipType ?? "") + " " + (s.hullName ?? ""); owner = owner.Trim(); } catch { }
            try
            {
                if (!string.IsNullOrEmpty(s.hullName) && !gd.parts.ContainsKey(s.hullName))
                {
                    hullMissing++; pending = true; lastActivity = UnityEngine.Time.realtimeSinceStartup;
                    if (examples.Count < 12) examples.Add($"{owner}: hull '{s.hullName}' missing - not converted");
                    return;
                }
                bool needBackup = false;
                var parts = s.parts;
                if (parts != null)
                    for (int i = parts.Count - 1; i >= 0; i--)
                    {
                        var p = parts[i]; string n = null; try { n = p != null ? p.name : null; } catch { }
                        if ((p == null || (!string.IsNullOrEmpty(n) && !gd.parts.ContainsKey(n))) && Allow("part", n)) { if (!needBackup) { needBackup = true; Backup(); } parts.RemoveAt(i); Note("part", n ?? "null", owner); }
                    }
                var comps = s.components;
                if (comps != null && gd.components != null)
                    for (int i = comps.Count - 1; i >= 0; i--)
                    {
                        string n = null; try { n = comps[i].Value; } catch { }
                        if (!string.IsNullOrEmpty(n) && !gd.components.ContainsKey(n) && Allow("component", n)) { if (!needBackup) { needBackup = true; Backup(); } comps.RemoveAt(i); Note("component", n, owner); }
                    }
                var techs = s.techs;
                if (techs != null && gd.technologies != null)
                    for (int i = techs.Count - 1; i >= 0; i--)
                    {
                        string n = techs[i];
                        if (!string.IsNullOrEmpty(n) && !gd.technologies.ContainsKey(n) && Allow("ship technology", n)) { if (!needBackup) { needBackup = true; Backup(); } techs.RemoveAt(i); Note("ship technology", n, owner); }
                    }
                // 1.11.45: turret calibre records are NOT checked - their turretPartDataName is not a key of GameData.parts (1.11.44 removed all 2217 of them and emptied the fleet)
                if (!string.IsNullOrEmpty(s.shipType) && gd.shipTypes != null && !gd.shipTypes.ContainsKey(s.shipType) && examples.Count < 12)
                { examples.Add($"{owner}: ship type '{s.shipType}' missing - left to the game"); pending = true; }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][SaveCompat] ship check failed: " + e.Message); }
            if (removed > before) fixedRecords++;
        }

        internal static void FixPlayer(Player.Store s)
        {
            if (On == null || !On.Value || s == null) return;
            var gd = GD(); if (gd == null || gd.technologies == null) return;
            players++;
            int before = removed;
            string owner = "nation " + (s.name ?? "?");
            try
            {
                var techs = s.techonologies;
                if (techs != null)
                    for (int i = techs.Count - 1; i >= 0; i--)
                    {
                        string n = null; try { n = techs[i] != null ? techs[i].name : null; } catch { }
                        if (!string.IsNullOrEmpty(n) && !gd.technologies.ContainsKey(n) && Allow("technology", n)) { Backup(); techs.RemoveAt(i); Note("technology", n, owner); }
                    }
                // 1.11.45: research priorities are technology TYPES (gun_large, engine_boiler...), not technology names - not checked
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][SaveCompat] nation check failed: " + e.Message); }
            if (removed > before) fixedRecords++;
        }

        // called every frame: once loading has been quiet for 3 s, write the summary (and a notice for the player)
        internal static void Tick()
        {
            if (!pending && ships == 0) return;
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (t - lastActivity < 3f && pending) return;
            bool world = false; try { world = GameManager.IsWorld || GameManager.IsWorldMap; } catch { }
            if (!world) return;
            bool load = false; try { load = GameManager.IsLoadingScreenActive; } catch { }
            if (load) return;
            string kinds = string.Join(", ", what.Select(kv => $"{kv.Value} {kv.Key}{(kv.Value == 1 ? "" : "s")}"));
            if (removed == 0 && hullMissing == 0)
            {
                // ship records are also rebuilt during play (designs, refits); report a clean check only for a whole save
                if (ships >= 30) MelonLogger.Msg($"[DIP GG Fixes][SaveCompat] save compatible: {ships} ship/design records and {players} nations checked, nothing to convert");
            }
            else
            {
                MelonLogger.Msg($"[DIP GG Fixes][SaveCompat] save converted: {ships} ship/design records and {players} nations checked, {fixedRecords} records changed, removed {kinds}{(hullMissing > 0 ? $"; {hullMissing} record(s) with a missing hull left as they were" : "")}");
                foreach (var ex in examples) MelonLogger.Msg("[DIP GG Fixes][SaveCompat]   " + ex);
                try
                {
                    EconomyUi.Notice("Save compatibility",
                        $"This save was made with game data that has changed since. {fixedRecords} ship designs or nations referred to items that no longer exist ({kinds}); they were removed so the campaign loads. " +
                        (hullMissing > 0 ? $"{hullMissing} record(s) use a hull that no longer exists and could not be converted. " : "") +
                        "Your save files were backed up to UserData/IronAdmiral_SaveBackups first; the save itself changes only when the game saves next.");
                }
                catch { }
            }
            ships = players = fixedRecords = removed = hullMissing = 0; what.Clear(); examples.Clear(); pending = false;
        }
    }

    // 1.11.46: the Harmony prefixes on Ship.FromStore / Player.FromStore are REMOVED. With them in place every ship and design record
    // failed to load (NullReferenceException inside the patched FromStore, 1564 errors for 782 records, empty fleet and design list),
    // even when the prefix changed nothing. The save check stays disabled until it is rebuilt on a different hook.
}
