// 1.11.196 (beta, author's decision 24.10): the AI's ship generator (TweaksAndFixes shipgen) made faster and its results kept.
// 24.10 log: Japan, hull bb_1_japan (Dreadnought I, 1921): 4 attempts, 189 s, failure - every attempt ended the same way,
// 2 main turrets of the 4 the hull requires, 4 barrels of 6; two destroyer designs took 20 s each.
// 1. Early stop: when an attempt fails because the hull cannot take the required main battery (both the turret and the barrel
//    minimum of the hull missed), the remaining attempts are dropped - they end the same way (the failure memory, 1.11.99,
//    already remembers the nation/hull pair for later turns).
// 2. Newest hulls: while the AI designs a ship, only hulls whose unlocking technology is among the newest the nation has for
//    that ship type (within ShipGenHullWindow years of technology data) count as unlocked - no 1905 dreadnought hull for a
//    nation with 1920 hull technology. Technology years come from technologies.csv (unlock(...) lists), never the calendar.
// 3. Kept designs: a design the generator finished successfully is saved as a shared design of its nation (file tag [gen]),
//    at most ShipGenShareMax per nation and ship type - on later turns the AI finds a fitting shared design and skips the
//    generator altogether (TAF: shared designs first, then the generator).
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ShipGenSpeed
    {
        internal static MelonPreferences_Entry<bool> EarlyStop, NewHulls, ShareAi;
        internal static MelonPreferences_Entry<int> HullWindow, ShareMax;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ShipGen] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            EarlyStop = cat.CreateEntry("ShipGenEarlyStop", true, null, "Campaign: the AI ship generator drops its remaining attempts when an attempt fails because the hull cannot take the required main battery (shorter end of turn)");
            NewHulls = cat.CreateEntry("ShipGenNewestHulls", true, null, "Campaign: the AI designs new ships only on hulls whose unlocking technology is among the newest it has for that ship type (no obsolete hulls, fewer failed attempts)");
            HullWindow = cat.CreateEntry("ShipGenHullWindow", 10, null, "Newest hulls: a hull counts as current when its unlocking technology is at most this many years (technology data) older than the newest hull technology the nation has for that ship type");
            ShareAi = cat.CreateEntry("ShipGenShareDesigns", true, null, "Campaign: a design the AI generator finished successfully is saved as a shared design of its nation ([gen] files) - on later turns the AI reuses it instead of running the generator");
            ShareMax = cat.CreateEntry("ShipGenShareMax", 8, null, "Kept generator designs: at most this many per nation and ship type");
        }

        // ---- 1. early stop ----
        static readonly Dictionary<IntPtr, int> lastTry = new Dictionary<IntPtr, int>();
        internal static void Attempt(Ship._GenerateRandomShip_d__573 it, Ship ship, string key, bool running)
        {
            var p = it.Pointer;
            if (!running) { lastTry.Remove(p); return; }
            if (EarlyStop == null || !EarlyStop.Value) return;
            int tryN = it._tryN_5__5, total = it._triesTotal_5__4;
            int prev; if (!lastTry.TryGetValue(p, out prev)) prev = 0;
            if (tryN <= prev) return;
            lastTry[p] = tryN;
            if (tryN + 1 >= total) return; // one attempt left anyway
            string why; if (!Structural(ship, out why)) return;
            // 1.11.197: the coroutine has already decided to go on with the next attempt when this runs (24.10 log: "Attempt 2 / 1"
            //           after the total was cut to the failed attempt), so the total is cut to one more attempt - the success rule
            //           of the report (attempts used up = failure) stays consistent
            it._triesTotal_5__4 = tryN + 1; ShipGenMemory.saved += total - tryN - 1;
            Log($"{key.Replace("|", ": hull ")} - attempt {tryN} of {total} failed on the hull itself ({why}) - one more attempt instead of {total - tryN}");
        }
        // the hull cannot take the required main battery: fewer main turrets than the hull minimum AND fewer barrels than its minimum
        // (the game needs only one of the two) - some main guns must be present, so a ship cleared for the next attempt never counts
        static bool Structural(Ship ship, out string why)
        {
            why = null;
            try
            {
                var hull = ship.hull; if (hull == null || hull.data == null) return false;
                int minT = hull.data.minMainTurrets, minB = hull.data.minMainBarrels;
                if (minT <= 0 && minB <= 0) return false;
                var parts = ship.parts; float top = 0f; int turrets = 0, barrels = 0;
                for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d == null || !d.isGun) continue; if (d.caliber > top) top = d.caliber; }
                for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d == null || !d.isGun) continue; if (Mathf.Abs(d.caliber - top) < 0.01f) { turrets++; barrels += Math.Max(1, d.barrels); } }
                if (turrets == 0) return false;
                bool tOk = minT <= 0 || turrets >= minT, bOk = minB <= 0 || barrels >= minB;
                if (tOk || bOk) return false;
                why = $"main turrets {turrets} of {minT}, barrels {barrels} of {minB}";
                return true;
            }
            catch { return false; }
        }

        // ---- 2. newest hulls ----
        internal static bool Active; internal static IntPtr ActivePlayer;
        static bool computing;
        static Dictionary<string, int> unlockYear;            // hull -> year of its unlocking technology
        static Dictionary<string, string> hullType;           // hull -> ship type (type(..) in parts.csv param)
        static readonly Dictionary<string, int> newest = new Dictionary<string, int>(); // player|type -> newest unlock year the player has (cached per month)
        static int newestMonth = -1; static int logged; static readonly HashSet<string> loggedKeys = new HashSet<string>();
        static void LoadTables()
        {
            if (unlockYear != null) return;
            unlockYear = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); hullType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string mods = Path.Combine(MelonUtils.GameDirectory, "Mods");
                foreach (var line in File.ReadAllLines(Path.Combine(mods, "technologies.csv")))
                {
                    if (line.StartsWith("@") || line.StartsWith("#") || line.Length < 5) continue;
                    var f = Split(line); if (f.Count < 8) continue;
                    int y; if (!int.TryParse(f[4].Trim(), out y)) continue;
                    foreach (var eff in f[7].Split(','))
                    {
                        int i = eff.IndexOf("unlock(", StringComparison.Ordinal); if (i < 0) continue;
                        int j = eff.IndexOf(')', i); if (j < 0) continue;
                        foreach (var h in eff.Substring(i + 7, j - i - 7).Split(';')) { var n = h.Trim(); if (n.Length == 0) continue; int old; if (!unlockYear.TryGetValue(n, out old) || y < old) unlockYear[n] = y; }
                    }
                }
                foreach (var line in File.ReadAllLines(Path.Combine(mods, "parts.csv")))
                {
                    if (line.StartsWith("@") || line.StartsWith("#") || line.Length < 5) continue;
                    var f = Split(line); if (f.Count < 15 || f[2].Trim() != "hull") continue;
                    var prm = f[14]; int i = prm.IndexOf("type(", StringComparison.Ordinal); if (i < 0) continue;
                    int j = prm.IndexOf(')', i); if (j < 0) continue;
                    hullType[f[0].Trim()] = prm.Substring(i + 5, j - i - 5).Trim();
                }
                Log($"newest hulls: {unlockYear.Count} hull unlock year(s) from technologies.csv, {hullType.Count} hull type(s) from parts.csv");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] hull tables: " + e.Message); }
        }
        static List<string> Split(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (char c in line) { if (c == '"') q = !q; else if (c == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
            r.Add(sb.ToString()); return r;
        }
        static int MonthIndex() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; } }

        // postfix of Player.IsHullUnlocked(PartData): during AI design generation an old hull does not count as unlocked
        internal static void Filter(Player player, PartData hull, ref bool result)
        {
            if (!result || computing || !Active || NewHulls == null || !NewHulls.Value || player == null || hull == null) return;
            if (player.Pointer != ActivePlayer) return;
            LoadTables();
            string name = hull.name; int y; string type;
            if (!unlockYear.TryGetValue(name, out y) || !hullType.TryGetValue(name, out type)) return; // start hulls and unknown hulls stay
            string pk = player.data != null ? player.data.name : player.Pointer.ToString();
            string key = pk + "|" + type;
            int m = MonthIndex(); if (m != newestMonth) { newest.Clear(); newestMonth = m; }
            int top;
            if (!newest.TryGetValue(key, out top))
            {
                top = 0; computing = true;
                try
                {
                    foreach (var kv in hullType)
                    {
                        if (kv.Value != type) continue;
                        int hy; if (!unlockYear.TryGetValue(kv.Key, out hy) || hy <= top) continue;
                        PartData pd = null; try { pd = G.GameData.parts[kv.Key]; } catch { }
                        if (pd == null) continue;
                        bool ok = false; try { ok = player.IsHullUnlocked(pd); } catch { }
                        if (ok) top = hy;
                    }
                }
                finally { computing = false; }
                newest[key] = top;
            }
            int window = HullWindow != null ? Math.Max(0, HullWindow.Value) : 10;
            if (top > 0 && y < top - window)
            {
                result = false;
                if (logged < 60 && loggedKeys.Add(pk + "|" + name)) { logged++; Log($"{pk}: hull {name} ({y}) left out for the AI design - newest {type} hull technology {top}, window {window} years"); }
            }
        }

        // ---- 3. kept designs ----
        internal static void Share(Ship ship, string key)
        {
            if (ShareAi == null || !ShareAi.Value || ship == null) return;
            try
            {
                var player = ship.player; if (player == null || player.data == null || !player.isAi) return;
                bool valid = false; try { valid = ship.IsValid(false); } catch { }
                if (!valid) { Log($"{key.Replace("|", ": hull ")} - design not kept: the game does not accept it as valid"); return; } // 1.11.197
            if (!ShipPartsCheck.AllMounted(ship)) { Log($"{key.Replace("|", ": hull ")} - design not kept: a part has no mount on this hull (it would load as a bare hull in battle)"); return; } // 1.11.217
                var st = ship.shipType; string type = st != null ? st.name : "ship";
                var store = ship.ToStore(false); if (store == null) return;
                string nation = player.data.name;
                string origId = store.id.ToString(); string shortId = origId.Length >= 8 ? origId.Substring(0, 8) : origId;
                string fileName; string path = Storage.GetSavedDesignPath($"{store.YearCreated} {nation} {type} {store.vesselName} [{shortId}][gen]", out fileName);
                if (string.IsNullOrEmpty(path)) return;
                string dir = Path.GetDirectoryName(path); int have = 0;
                try { foreach (var f in Directory.GetFiles(dir, $"* {nation} {type} *[gen].bindesign")) have++; } catch { }
                int max = ShareMax != null ? ShareMax.Value : 8;
                if (have >= max) { Log($"{key.Replace("|", ": hull ")} - design not kept: {have} [gen] design(s) of {nation} {type} already"); return; }
                store.playerName = nation; store.playerOriginal = nation; store.isSharedDesign = true; store.id = Il2CppSystem.Guid.NewGuid();
                Storage.SaveSharedDesignShipByte(path, Util.SerializeObjectByte<Ship.Store>(store));
                Log($"{key.Replace("|", ": hull ")} - design kept as shared design {fileName} ({have + 1} of {max} for {nation} {type}); it loads on the next game start");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] keep design: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Player), "IsHullUnlocked", new Type[] { typeof(PartData) })]
    internal static class Patch_IsHullUnlocked_ShipGen
    {
        private static void Postfix(Player __instance, PartData __0, ref bool __result)
        {
            try { ShipGenSpeed.Filter(__instance, __0, ref __result); }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] hull filter: " + e.Message); }
        }
    }
}
