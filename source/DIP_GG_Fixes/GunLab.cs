// 1.11.173 (author's tool): gun and radar laboratory for the unattended battle test (AutoTestLab).
// One battle gives as much calibration data as possible:
//  1. every gun of every ship is evaluated by the game's own formulas at all five generations (Mk 1-5) - rate of fire, range,
//     muzzle velocity, shell weight, belt penetration - with that ship's modifiers; the generation the ship really uses is marked;
//  2. radar A/B: every second ship of each side gets fire-control radar, the others have it removed - otherwise identical
//     designs fight in the same battle; the ballistics log splits shells and hits by radar (R) / no radar (-).
// Radar is also recorded in normal battles (shooter's radar flag in the ballistics log).
using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class GunLab
    {
        internal static MelonPreferences_Entry<bool> On;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][GunLab] " + m); }
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AutoTestLab", false, null, "Author's tool: in the unattended battle test log every gun of every ship at all five generations and fit fire-control radar to every second ship of each side (radar A/B test)");
        }

        internal static bool HasRadar(Ship s)
        {
            if (s == null) return false;
            long key = s.Pointer.ToInt64();
            bool r; if (radarCache.TryGetValue(key, out r)) return r;
            r = false;
            try { var comps = s.components; if (comps != null) foreach (var kv in comps) { var cd = kv.Value; if (cd != null && cd.type == "radar") { r = true; break; } } } catch { }
            radarCache[key] = r; return r;
        }
        static readonly Dictionary<long, bool> radarCache = new Dictionary<long, bool>();
        internal static void ResetCache() { radarCache.Clear(); }

        static ComponentData BestRadar()
        {
            try
            {
                var all = G.GameData.components;
                foreach (var n in new[] { "rangefinder_radar_3", "rangefinder_radar_2", "rangefinder_radar_1" }) { ComponentData c; if (all.TryGetValue(n, out c) && c != null) return c; }
                foreach (var kv in all) if (kv.Value != null && kv.Value.type == "radar") return kv.Value;
            }
            catch (Exception e) { Log("radar component: " + e.Message); }
            return null;
        }

        internal static void Run()
        {
            if (On == null || !On.Value) return;
            try
            {
                var ships = BattleManager.Instance.CurrentBattle.Ships;
                CacheDesigns(ships); // before the radar A/B changes the ships
                var radar = BestRadar();
                var count = new Dictionary<string, int>();
                var done = new HashSet<string>();
                var sb = new StringBuilder("gun data at all five generations (game formulas with each ship's modifiers; * = generation fitted): calibre | ship | Mk | rounds/min/barrel | range m | muzzle velocity | shell weight | belt penetration mm");
                var rs = new StringBuilder("radar A/B: ");
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    string side = "?"; try { side = s.player != null && s.player.data != null ? s.player.data.name : "?"; } catch { }
                    // radar A/B per side and ship type
                    string ty = "?"; try { ty = s.shipType != null ? s.shipType.name : "?"; } catch { }
                    string ck = side + "|" + ty; int n; count.TryGetValue(ck, out n); count[ck] = n + 1;
                    if (radar != null)
                    {
                        try
                        {
                            if (n % 2 == 0) s.InstallComponent(radar, false); else s.UninstallComponent(radar.typex);
                            try { s.CStats(); } catch { }
                        }
                        catch (Exception e) { Log("radar install: " + e.Message); }
                    }
                    rs.Append($"{s.name} [{side} {ty}] {(HasRadarFresh(s) ? "R" : "-")}; ");
                    // gun table
                    var parts = s.parts; if (parts == null) continue;
                    for (int j = 0; j < parts.Count; j++)
                    {
                        var p = parts[j]; var pd = p != null ? p.data : null; if (pd == null || !pd.isGun) continue;
                        GunData gd = null; try { gd = G.GameData.GunData(pd); } catch { }
                        if (gd == null) continue;
                        string key = $"{gd.caliberInch:0.0}|{side}|{ty}"; if (!done.Add(key)) continue;
                        int fitted = 0; try { fitted = s.TechGunGrade(pd, false); } catch { }
                        for (int g = 1; g <= 5; g++)
                        {
                            float fr = -1, rg = -1, v = -1, w = -1, pen = -1;
                            try { fr = gd.Firerate(s, pd, g); } catch { }
                            try { rg = gd.Range(s, pd, g); } catch { }
                            try { v = gd.ShellVelocity(s, pd, g); } catch { }
                            try { w = gd.ShellWeight(s, pd, g); } catch { }
                            try { pen = gd.Penetration(s, pd, g, true); } catch { }
                            sb.Append($"\n   {gd.caliberInch,4:0.0} in | {side} {ty} | {g}{(g == fitted ? "*" : " ")} | {fr,6:0.00} | {rg,6:0} | {v,5:0} | {w,7:0.0} | {pen,6:0}");
                        }
                    }
                }
                Log(rs.ToString());
                Log(sb.ToString());
            }
            catch (Exception e) { Log("run: " + e); }
        }
        // 1.11.177: the designs of a test battle are saved as shared designs (file names marked [IATEST]) so the next test battle
        // of the same year and nations does not wait for the ship generator. They load on the next game start; after the tests
        // the [IATEST] files are moved out of the Designs folder (Iron_Admiral_release\tests_designs_cleanup.ps1).
        static void CacheDesigns(Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            int written = 0, skipped = 0;
            var seen = new HashSet<string>();
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i]; if (s == null) continue;
                try
                {
                    string nation = s.player != null && s.player.data != null ? s.player.data.name : null; if (nation == null) continue;
                    var store = s.ToStore(false); if (store == null) continue;
                    string key = nation + "|" + store.vesselName; if (!seen.Add(key)) continue;
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Game Labs", "Ultimate Admiral Dreadnoughts", "Designs");
                    if (System.IO.Directory.Exists(dir) && System.IO.Directory.GetFiles(dir, $"* {nation} {store.vesselName} [IATEST*").Length > 0) { skipped++; continue; }
                    string fileName;
                    string path = Storage.GetSavedDesignPath($"{store.YearCreated} {nation} {store.vesselName} [IATEST]", out fileName);
                    if (string.IsNullOrEmpty(path)) { skipped++; continue; }
                    store.playerName = nation; store.playerOriginal = nation; store.isSharedDesign = true; store.id = Il2CppSystem.Guid.NewGuid();
                    Storage.SaveSharedDesignShipByte(path, Util.SerializeObjectByte<Ship.Store>(store));
                    written++;
                }
                catch (Exception e) { Log("design cache: " + e.Message); }
            }
            Log($"design cache: {written} test design(s) saved as shared designs [IATEST], {skipped} already there");
        }
        static bool HasRadarFresh(Ship s) { radarCache.Remove(s.Pointer.ToInt64()); return HasRadar(s); }
    }
}
