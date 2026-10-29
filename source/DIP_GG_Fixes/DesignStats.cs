// 1.11.397 (author 29.10 18:26: "it would also be nice if every design had battle statistics - how many victories, how many
// defeats..."): a service record for every design (ship class), kept across games in UserData\IronAdmiral\design_stats.csv.
//  - at the end of every battle - campaign (BattleManager.FinishCombat: winner or draw) and custom (CustomBattleComplete: victory
//    or defeat of the player's side) - each ship that fought adds to the record of its design: one battle, a victory, draw or
//    defeat for its side, and one ship lost if it sank;
//  - the design is the ship's class (Ship.design) - every ship built to it counts - or the ship itself if it has none;
//  - in the designer, a small panel under the top bar shows the record of the design on the slipway.
// Switch: DesignStats.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignStats
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DesignStats", true, null, "Every design keeps a battle record - battles, victories, draws, defeats, ships lost - in campaign and custom battles; shown in the designer (UserData\\IronAdmiral\\design_stats.csv)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][DesignStats] " + m); }
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "design_stats.csv"); } }

        class Rec { public string Id, Name, Nation, Type; public int Battles, Wins, Draws, Losses, Lost; }
        static Dictionary<string, Rec> recs;
        static Dictionary<string, Rec> All()
        {
            if (recs != null) return recs;
            recs = new Dictionary<string, Rec>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                    {
                        var c = l.Split(','); if (c.Length < 9 || c[0] == "id") continue;
                        int I(string s) { int.TryParse(s, out int v); return v; }
                        recs[c[0]] = new Rec { Id = c[0], Name = c[1], Nation = c[2], Type = c[3], Battles = I(c[4]), Wins = I(c[5]), Draws = I(c[6]), Losses = I(c[7]), Lost = I(c[8]) };
                    }
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignStats] read: " + e.Message); }
            return recs;
        }
        static void SaveAll()
        {
            try
            {
                var sb = new StringBuilder("id,name,nation,type,battles,wins,draws,losses,lost" + Environment.NewLine);
                foreach (var r in recs.Values) sb.Append($"{r.Id},{r.Name},{r.Nation},{r.Type},{r.Battles},{r.Wins},{r.Draws},{r.Losses},{r.Lost}{Environment.NewLine}");
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignStats] write: " + e.Message); }
        }

        static string Clean(string s) { return (s ?? "").Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim(); }
        static Ship DesignOf(Ship s) { Ship d = null; try { d = s.design; } catch { } return d ?? s; }
        static string Key(Ship s) { try { var d = DesignOf(s); return d.id.ToString(); } catch { return null; } }

        static IntPtr recorded;   // the battle already recorded (one record per battle)
        // outcome for a ship: +1 its side won, 0 draw, -1 lost
        static void Record(string how, Func<Ship, int> outcome)
        {
            if (On == null || !On.Value) return;
            var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || b.Ships == null) return;
            if (b.Pointer == recorded) return; recorded = b.Pointer;
            var all = All(); int n = 0, w = 0, l = 0, d = 0, lost = 0; var seen = new HashSet<IntPtr>();
            for (int i = 0; i < b.Ships.Count; i++)
            {
                var s = b.Ships[i]; if (s == null || !seen.Add(s.Pointer)) continue;
                string key = Key(s); if (string.IsNullOrEmpty(key) || key.Length < 30) continue;
                if (!all.TryGetValue(key, out var r))
                {
                    var ds = DesignOf(s); string nat = "", type = "";
                    try { nat = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
                    try { type = s.shipType != null ? s.shipType.name : ""; } catch { }
                    r = new Rec { Id = key, Name = Clean(ds.vesselName), Nation = nat, Type = type }; all[key] = r;
                }
                int o = 0; try { o = outcome(s); } catch { }
                r.Battles++; if (o > 0) { r.Wins++; w++; } else if (o < 0) { r.Losses++; l++; } else { r.Draws++; d++; }
                bool sunk = false; try { sunk = s.isSinking || s.isDead; } catch { }
                if (sunk) { r.Lost++; lost++; }
                n++;
            }
            SaveAll();
            Log($"{how}: {n} ship(s) added to their designs' records - {w} on the winning side, {l} on the losing side, {d} in a draw, {lost} sunk");
        }

        // campaign and other battles that go through FinishCombat: the winner is given, or a draw
        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.FinishCombat))]
        static class Patch_Finish
        {
            static void Prefix(bool interrupted, Player winner, bool needDraw)
            {
                try
                {
                    if (GameManager.IsCustomBattle) return;   // custom battles: CustomBattleComplete
                    Record(interrupted ? "battle left" : "battle finished", s => (needDraw || winner == null) ? 0 : (s.player != null && s.player.Pointer == winner.Pointer ? 1 : -1));
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignStats] " + e.Message); }
            }
        }
        // custom battles: victory or defeat of the player's side
        [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.CustomBattleComplete))]
        static class Patch_Custom
        {
            static void Prefix(bool victory)
            {
                try
                {
                    Record("custom battle " + (victory ? "won" : "lost"), s => { bool mine = false; try { mine = s.player != null && s.player.isMain; } catch { } return mine == victory ? 1 : -1; });
                }
                catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][DesignStats] " + e.Message); }
            }
        }

        // the record of the design on the slipway, in the designer
        static GUIStyle sT, sL; static string shownKey, shownText; static float nextLook;
        internal static void OnGUI()
        {
            if (On == null || !On.Value) return;
            bool cons = false; try { cons = GameManager.IsConstructor; } catch { }
            if (!cons) { shownKey = null; return; }
            if (Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 1f;
                Ship s = null; try { s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
                string key = s != null ? Key(s) : null;
                if (key != shownKey)
                {
                    shownKey = key; shownText = null;
                    if (key != null && All().TryGetValue(key, out var r) && r.Battles > 0)
                        shownText = $"Service record of {r.Name}: {r.Battles} battle(s) - {r.Wins} won, {r.Draws} drawn, {r.Losses} lost; {r.Lost} ship(s) of the class sunk";
                }
            }
            if (shownText == null) return;
            float k = UiScale.F > 0.01f ? UiScale.F : 1f, W = Screen.width / k;
            if (sL == null) { sL = new GUIStyle(); sL.fontSize = 13; sL.alignment = TextAnchor.MiddleCenter; sL.normal.textColor = new Color(1f, 0.92f, 0.7f); }
            float w = 620f, x = (W - w) / 2f, y = 92f;
            GUI.Box(new Rect(x, y, w, 24f), "");
            GUI.Label(new Rect(x, y, w, 24f), shownText, sL);
        }
    }
}
