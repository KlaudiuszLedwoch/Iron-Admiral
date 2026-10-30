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
            Prefer = cat.CreateEntry("DesignStatsPreferBest", true, null, "Nations prefer the designs with the best battle record: after your own designs a proven design is taken before the historical classes, and among equals the better record goes first");
            ProvenBattles = cat.CreateEntry("DesignStatsProvenBattles", 3, null, "How many battles a design must have fought to count as proven");
            ProvenScore = cat.CreateEntry("DesignStatsProvenScore", 0.55f, null, "Lowest score of a proven design (share of battles won, smoothed, less half the share of ships sunk; 0.5 = no record)");
        }
        internal static MelonPreferences_Entry<bool> Prefer; internal static MelonPreferences_Entry<int> ProvenBattles; internal static MelonPreferences_Entry<float> ProvenScore;
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

        // 1.11.398 (author 29.10 19:01: "now for the AI - prefer the designs with the best statistics"): a design's score from its
        // record - the share of battles won (a draw counts half), smoothed so that one lucky battle does not outrank a long good
        // record ((won + 1) / (battles + 2)), less half the share of its ships sunk per battle. A design with no record scores 0.5.
        internal static float Score(string id, out int battles)
        {
            battles = 0;
            if (On == null || !On.Value || string.IsNullOrEmpty(id) || !All().TryGetValue(id, out var r) || r.Battles <= 0) return 0.5f;
            battles = r.Battles;
            float won = r.Wins + 0.5f * r.Draws;
            return (won + 1f) / (r.Battles + 2f) - 0.5f * Mathf.Min(1f, (float)r.Lost / r.Battles);
        }
        // 1.11.398: after the player's own designs, a nation takes a proven design - a shared design of its own with at least
        // DesignStatsProvenBattles battles and a score of DesignStatsProvenScore or more, the best record first - before the
        // historical classes and the rest. Within the player's designs and the historical classes the best record goes first too.
        internal static Ship PickProven(Player player, ShipType shipType, int year, bool checkTech, System.Collections.Generic.List<Il2CppSystem.Guid> ignore)
        {
            if (On == null || !On.Value || Prefer == null || !Prefer.Value) return null;
            return HistPriority.PickSet(null, "proven design", "DesignStats", player, shipType, year, checkTech, ignore, 12, Math.Max(1, ProvenBattles.Value), ProvenScore.Value);
        }
        internal static string Describe(string id)
        {
            if (string.IsNullOrEmpty(id) || !All().TryGetValue(id, out var r) || r.Battles <= 0) return "no battle record";
            return $"{r.Battles} battle(s), {r.Wins} won, {r.Draws} drawn, {r.Losses} lost, {r.Lost} sunk";
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
            float k = UiScale.F > 0.01f ? UiScale.F : 1f, W = Screen.width / k;
            if (sL == null) { sL = new GUIStyle(); sL.fontSize = 13; sL.alignment = TextAnchor.MiddleCenter; sL.normal.textColor = new Color(1f, 0.92f, 0.7f); }
            float w = 620f, x = (W - w) / 2f, y = 92f;
            if (shownText != null) { GUI.Box(new Rect(x, y, w, 24f), ""); GUI.Label(new Rect(x, y, w, 24f), shownText, sL); }
            // 1.11.455: the ranking
            if (GUI.Button(new Rect(x + w - 150f, y + 26f, 150f, 20f), rankOpen ? "Close ranking" : "Design ranking")) { rankOpen = !rankOpen; rankAt = -1f; }
            if (!rankOpen) return;
            string type = null; try { var s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; type = s != null && s.shipType != null ? s.shipType.name : null; } catch { }
            if (Time.unscaledTime - rankAt > 2f) { rankAt = Time.unscaledTime; rankRows = Ranking(rankAll ? null : type); }
            if (sR == null) { sR = new GUIStyle(); sR.fontSize = 12; sR.richText = true; sR.normal.textColor = new Color(0.92f, 0.92f, 0.92f); }
            float rh = 18f, h = 52f + rh * Math.Max(1, rankRows.Count), ry = y + 50f;
            GUI.Box(new Rect(x, ry, w, h), "");
            GUI.Label(new Rect(x + 8f, ry + 4f, 400f, 20f), $"<b>Design ranking - {(rankAll || type == null ? "all types" : type.ToUpperInvariant())}</b> (score = share won, smoothed, less half the ships sunk)", sR);
            if (type != null && GUI.Button(new Rect(x + w - 110f, ry + 4f, 100f, 18f), rankAll ? "This type" : "All types")) { rankAll = !rankAll; rankAt = -1f; }
            if (GUI.Button(new Rect(x + w - 220f, ry + 4f, 105f, 18f), sunkList ? "Ranking" : "Ships sunk")) { sunkList = !sunkList; }
            if (sunkList) { DrawSunk(x, ry, w, rh); return; }
            GUI.Label(new Rect(x + 8f, ry + 26f, w - 16f, 20f), "<color=#C0C0C0>#   design                                   nation        type   battles   won / drawn / lost   lost  kills  score</color>", sR);
            if (rankRows.Count == 0) GUI.Label(new Rect(x + 8f, ry + 46f, w - 16f, rh), "No design has a battle record yet.", sR);
            for (int i = 0; i < rankRows.Count; i++)
            {
                var r = rankRows[i]; float yy = ry + 46f + i * rh;
                GUI.Label(new Rect(x + 8f, yy, 24f, rh), (i + 1).ToString(), sR);
                GUI.Label(new Rect(x + 32f, yy, 250f, rh), r.Name, sR);
                GUI.Label(new Rect(x + 285f, yy, 90f, rh), r.Nation, sR);
                GUI.Label(new Rect(x + 378f, yy, 40f, rh), r.Type.ToUpperInvariant(), sR);
                GUI.Label(new Rect(x + 420f, yy, 40f, rh), r.Battles.ToString(), sR);
                GUI.Label(new Rect(x + 462f, yy, 80f, rh), $"{r.Wins} / {r.Draws} / {r.Losses}", sR);
                GUI.Label(new Rect(x + 540f, yy, 26f, rh), r.Lost.ToString(), sR);
                GUI.Label(new Rect(x + 566f, yy, 26f, rh), SinkLog.KillsOf(r.Id).ToString(), sR);
                GUI.Label(new Rect(x + 592f, yy, 30f, rh), Score(r.Id, out _).ToString("0.00"), sR);
            }
        }
        static bool sunkList;
        // 1.11.456: the latest sinkings, newest first - the ship, her class, type, tonnage, nation and who sank her
        static void DrawSunk(float x, float ry, float w, float rh)
        {
            var all = SinkLog.Rows(); int n = Math.Min(20, all.Count);
            GUI.Box(new Rect(x, ry + 24f, w, 26f + rh * Math.Max(1, n)), "");
            GUI.Label(new Rect(x + 8f, ry + 28f, w - 16f, 20f), $"<b>Ships sunk</b> <color=#C0C0C0>({all.Count} in all, newest first)</color>", sR);
            if (n == 0) GUI.Label(new Rect(x + 8f, ry + 48f, w - 16f, rh), "No ship sunk yet - sinkings are recorded from this version on.", sR);
            for (int i = 0; i < n; i++)
            {
                var r = all[all.Count - 1 - i];
                string by = r.By == "unknown" ? "<color=#C0C0C0>no hit recorded</color>" : $"{r.By}{(r.ByClass.Length > 0 && !r.By.Contains(r.ByClass) ? " (" + r.ByClass + ")" : "")}";
                GUI.Label(new Rect(x + 8f, ry + 48f + i * rh, w - 16f, rh), $"<color=#C0C0C0>{r.Date}</color>  {r.Type.ToUpperInvariant()} <b>{r.Ship}</b> ({r.Tons:N0} t, {r.Nation}) - sunk by {by}{(r.Weapon.Length > 0 ? ", " + r.Weapon : "")}", sR);
            }
        }
        static bool rankOpen, rankAll; static float rankAt = -1f; static List<Rec> rankRows = new List<Rec>(); static GUIStyle sR;
        static List<Rec> Ranking(string type)
        {
            var l = new List<Rec>();
            foreach (var r in All().Values) if (r.Battles > 0 && (type == null || string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase))) l.Add(r);
            l.Sort((a, b) => { int c = Score(b.Id, out _).CompareTo(Score(a.Id, out _)); return c != 0 ? c : b.Battles.CompareTo(a.Battles); });
            if (l.Count > 20) l.RemoveRange(20, l.Count - 20);
            return l;
        }
    }
}
