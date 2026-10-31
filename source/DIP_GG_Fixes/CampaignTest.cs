// 1.11.434 (author 30.10 13:36-13:40: "make a test campaign - the campaign I used longest ago", i.e. slot 2): an unattended campaign
// test for the campaign features that custom battles cannot show (#96 air-only battle, battle weather by climate #189, campaign
// air strikes #187/#188, #94 lost territory wars, the battle hour #57). AutoTestCampaign = "slot=2;turns=3;fight=1;seconds=600":
// from the main menu the campaign of that slot is loaded (GameManager.ContinueCampaign), turns are played (CampaignController.
// NextTurn); a battle popup is fought (Fight, then Battle) or auto-resolved; a fought battle runs for "seconds" and is then
// finished (BattleManager.FinishCombat, interrupted). The test runner backs up every save before and restores all slots but the
// test slot after (pm\run_queue.ps1). Only slot 2 is ever loaded - any other slot is refused.
using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class CampaignTest
    {
        internal static MelonPreferences_Entry<string> Spec;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Spec = cat.CreateEntry("AutoTestCampaign", "", null, "Author's tool: unattended campaign test from the main menu, e.g. slot=2;turns=3;fight=1;seconds=600 (only the test campaign slot 2; empty = off)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][CampaignTest] " + m); }
        static bool started, running;
        static bool turnDone;
        static readonly string[] Tabs = { "FLEET", "FINANCES", "POLITICS", "WORLD" };
        // a top-bar button of the campaign screen found by its label (as the player sees it)
        static bool TopTab(string label)
        {
            try
            {
                foreach (var b in UnityEngine.Object.FindObjectsOfType<Button>())
                {
                    if (b == null || !b.gameObject.activeInHierarchy || !b.interactable) continue;
                    string txt = null;
                    try { var tm = b.GetComponentInChildren<Il2CppTMPro.TMP_Text>(); if (tm != null) txt = tm.text; } catch { }
                    if (txt == null) { try { var tx = b.GetComponentInChildren<Text>(); if (tx != null) txt = tx.text; } catch { } }
                    if (txt != null && txt.Trim().ToUpperInvariant() == label) { b.onClick.Invoke(); Log("window " + label); return true; }
                }
            }
            catch (Exception e) { Log("window " + label + ": " + e.Message); }
            return false;
        }
        static int Int(string spec, string key, int def) { foreach (var kv in spec.Split(';')) { var p = kv.Split('='); if (p.Length == 2 && p[0].Trim() == key && int.TryParse(p[1].Trim(), out var v)) return v; } return def; }

        internal static void Tick()
        {
            if (Spec == null || string.IsNullOrEmpty(Spec.Value) || started) return;
            bool menu = false; try { menu = GameManager.IsMainMenu && !GameManager.IsLoadingAny; } catch { }
            if (!menu) return;
            started = true; running = true;
            MelonCoroutines.Start(Run(Spec.Value));
        }
        internal static bool Running => running;

        static IEnumerator Wait(Func<bool> cond, float max)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < t0 + max) { bool ok = false; try { ok = cond(); } catch { } if (ok) yield break; yield return null; }
        }
        static bool WorldReady() { return GameManager.IsWorldMap && !GameManager.IsLoadingAny; }
        static BattlePopup Popup() { try { var p = UnityEngine.Object.FindObjectOfType<BattlePopup>(); return p != null && p.gameObject.activeInHierarchy ? p : null; } catch { return null; } }
        static bool Click(Button b, string what) { try { if (b != null && b.gameObject.activeInHierarchy && b.interactable) { b.onClick.Invoke(); Log("clicked " + what); return true; } } catch (Exception e) { Log($"click {what}: {e.Message}"); } return false; }

        static string Str(string spec, string key) { foreach (var kv in spec.Split(';')) { var p = kv.Split('='); if (p.Length == 2 && p[0].Trim() == key) return p[1].Trim(); } return null; }

        // message boxes (new campaign confirmations, events) are answered with their OK button; checked at most once a second
        static float nextBoxScan;
        static void ConfirmBoxes()
        {
            if (Time.realtimeSinceStartup < nextBoxScan) return;
            nextBoxScan = Time.realtimeSinceStartup + 1f;
            try
            {
                foreach (var mb in UnityEngine.Object.FindObjectsOfType<MessageBoxUI>())
                {
                    if (mb == null || !mb.gameObject.activeInHierarchy) continue;
                    string h = ""; try { h = (mb.Header != null ? mb.Header.text : "") + " | " + (mb.Data != null ? mb.Data.text : ""); } catch { }
                    h = h.Replace("\n", " "); if (h.Length > 160) h = h.Substring(0, 160);
                    Click(mb.Ok, "OK on message '" + h + "'");
                }
            }
            catch { }
        }

        // 1.11.512: a new campaign in slot 2 from the main menu: the game's new-campaign window, the nation and the start year chosen
        // with its own arrows, the fleet generated by the game (not designed by the player), then its Start button
        static IEnumerator NewCampaign(string spec)
        {
            int year = Int(spec, "year", 0); string nation = Str(spec, "nation");
            try { GameManager.Instance.SetCampaignSlotIndex(2); } catch (Exception e) { Log("slot: " + e.Message); }
            int idx = -1; try { idx = GameManager.Instance.currentCampaignSlotIndex; } catch { }
            if (idx != 2) { Log($"campaign slot index is {idx}, not 2 - new campaign refused"); yield break; }
            Log("new campaign in slot 2: opening the new-campaign window");
            try { GameManager.Instance.ToNewCampaignSetup(); } catch (Exception e) { Log("ToNewCampaignSetup: " + e.Message); }
            Func<bool> shown = () => G.ui != null && G.ui.NewGameWindow != null && G.ui.NewGameWindow.Root != null && G.ui.NewGameWindow.Root.activeInHierarchy;
            yield return Wait(shown, 60f);
            if (!shown()) { try { G.ui.NewGameUI(); } catch (Exception e) { Log("NewGameUI: " + e.Message); } yield return Wait(shown, 60f); }
            if (!shown()) { Log("the new-campaign window did not open"); yield break; }
            var w = G.ui.NewGameWindow;
            for (float d = Time.realtimeSinceStartup + 1f; Time.realtimeSinceStartup < d;) yield return null;
            int n = 0; try { n = w.availablePlayers != null ? w.availablePlayers.Count : 0; } catch { }
            if (!string.IsNullOrEmpty(nation))
            {
                for (int i = 0; i <= n; i++)
                {
                    string cur = null; try { cur = w.selectedNation != null ? w.selectedNation.name : null; } catch { }
                    if (cur == nation) break;
                    try { w.ChangeNation(1); } catch (Exception e) { Log("ChangeNation: " + e.Message); break; }
                    yield return null;
                }
            }
            if (year > 0)
            {
                int last = int.MinValue;
                for (int i = 0; i < 200; i++)
                {
                    int y = 0; try { y = w.GetStartingYear(); } catch { break; }
                    if (y == year || y == last) break;
                    last = y;
                    try { w.ChangeYear(y < year ? 1 : -1); } catch (Exception e) { Log("ChangeYear: " + e.Message); break; }
                    yield return null;
                    int y2 = 0; try { y2 = w.GetStartingYear(); } catch { }
                    if ((y < year && y2 > year) || (y > year && y2 < year)) break;   // the year steps over the wanted one
                }
            }
            // the fleet is generated by the game, not designed by the player
            for (int i = 0; i < 6; i++) { bool own = false; try { own = w.GetCreateOwnFleet(); } catch { break; } if (!own) break; try { w.ChangeFleetCreation(1); } catch { break; } yield return null; }
            string sel = "?"; try { sel = w.selectedNation != null ? w.selectedNation.name : "?"; } catch { }
            string info = "";
            try { info = $"nation {sel}, start year {w.GetStartingYear()}, difficulty {w.GetDifficulty()}, own fleet {w.GetCreateOwnFleet()}, historical AI {w.GetUseHistoricalAi()}, random AI {w.GetUseRandomAi()}, shared designs {w.GetSharedDesignsUsage()}, campaign designs {w.GetCampaignDesignsUsage()}, nations {n}"; } catch (Exception e) { info = "settings: " + e.Message; }
            Log("new campaign: " + info + (nation != null && sel != nation ? $" (wanted nation {nation} not found)" : "") + (year > 0 ? $" (wanted year {year})" : ""));
            if (!Click(w.StartCampaign, "Start campaign")) { Log("the Start button could not be pressed"); yield break; }
            // 1.11.513: Start opens "Select slot to start a new campaign" (UISaveLoadWindow) with the first slot highlighted - only the
            // row with slot index 2 is ever chosen (the window's own action for that row); nothing else in that window is pressed
            UISaveLoadWindow sw = null;
            for (float s0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup < s0 + 60f;)
            {
                try { foreach (var x in UnityEngine.Object.FindObjectsOfType<UISaveLoadWindow>()) if (x != null && x.gameObject.activeInHierarchy) { sw = x; break; } } catch { }
                if (sw != null || WorldReady()) break;
                yield return null;
            }
            if (sw != null)
            {
                for (float d = Time.realtimeSinceStartup + 1f; Time.realtimeSinceStartup < d;) yield return null;
                UISaveBattleSlot target = null; var rows = new System.Collections.Generic.List<string>();
                try
                {
                    var slots = sw.SaveSlots;
                    for (int i = 0; slots != null && i < slots.Count; i++)
                    {
                        var sl = slots[i]; if (sl == null) continue;
                        string nm = "", dt = ""; try { nm = sl.SaveName != null ? sl.SaveName.text : ""; dt = sl.SaveDate != null ? sl.SaveDate.text : ""; } catch { }
                        rows.Add($"{sl.SlotIndex}: {nm} {dt}");
                        if (sl.SlotIndex == 2) target = sl;
                    }
                }
                catch (Exception e) { Log("slot window: " + e.Message); }
                Log("slot window: " + string.Join("; ", rows));
                if (target == null) { Log("slot 2 is not in the slot window - new campaign refused"); try { sw.Close(); } catch { } yield break; }
                try
                {
                    var slots = sw.SaveSlots; int pos = -1; for (int i = 0; i < slots.Count; i++) if (slots[i] != null && slots[i].Pointer == target.Pointer) pos = i;
                    if (pos >= 0) sw.selectedSlotIndex = pos;
                    for (int i = 0; i < slots.Count; i++) try { if (slots[i] != null) slots[i].Select(i == pos); } catch { }
                    sw.DoAction(target); Log($"slot 2 chosen for the new campaign (row {pos})");
                }
                catch (Exception e) { Log("slot 2: " + e.Message); yield break; }
            }
            else Log("no slot window appeared");
            float t0 = Time.realtimeSinceStartup;
            // a confirmation is answered only while the game's campaign slot is 2; any other slot ends the test at once
            while (Time.realtimeSinceStartup < t0 + 6000f)
            {
                int si = -1; try { si = GameManager.Instance.currentCampaignSlotIndex; } catch { }
                if (si != 2) { Log($"campaign slot changed to {si} - test stopped without confirming anything"); Application.Quit(); yield break; }
                ConfirmBoxes(); if (WorldReady()) break; yield return null;
            }
            if (WorldReady())
            {
                for (float d = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < d;) { ConfirmBoxes(); yield return null; }
                Log($"new campaign started in {Time.realtimeSinceStartup - t0:0} s");
            }
        }

        static IEnumerator Run(string spec)
        {
            int slot = Int(spec, "slot", 2), turns = Int(spec, "turns", 3), fight = Int(spec, "fight", 1), secs = Int(spec, "seconds", 600);
            if (slot != 2) { Log($"slot {slot} refused - only the test campaign slot 2 may be loaded"); running = false; yield break; }
            if (Int(spec, "new", 0) == 1)
            {
                // 1.11.512 (#121, author 31.10: "new campaign in slot 2" - the old test save of 2025 names nations the mod no longer has)
                yield return NewCampaign(spec);
                if (!WorldReady()) { Log("the new campaign did not start in 100 min - quitting"); Application.Quit(); yield break; }
            }
            else
            {
                string name = null;
                try { GameManager.Instance.SetCampaignSlotIndex(slot); name = GameManager.Instance.GetCampaignSaveName(slot); } catch (Exception e) { Log("slot: " + e.Message); }
                Log($"loading the test campaign from slot {slot} ({name})");
                try { GameManager.Instance.ContinueCampaign(null, false, name); } catch (Exception e) { Log("ContinueCampaign: " + e.Message); running = false; yield break; }
                yield return Wait(WorldReady, 900f);
                if (!WorldReady()) { Log("the campaign did not load in 15 min - quitting"); Application.Quit(); yield break; }
            }
            string date = ""; try { date = CampaignController.Instance.CurrentDate.AsDate().ToString("yyyy-MM"); } catch { }
            Log($"campaign loaded: {date}");
            // 1.11.508 (#121): think=N waits N s on the map before each turn, as a player would, so the design pass on the map
            // (DesignAhead) can be compared with the end-of-turn times
            int think = Int(spec, "think", 0);
            bool browse = Int(spec, "browse", 0) > 0;
            for (int t = 1; t <= turns; t++)
            {
                if (think > 0)
                {
                    // 1.11.518 (author 31.10, 17:36 "test it yourself"): browse=1 plays the player during the waiting - every 6 s the
                    // next top-bar window (Fleet, Finances, Politics, World) is opened; frames longer than 250 ms are counted (stutter)
                    Log($"turn {t}/{turns}: {think} s on the map{(browse ? " (browsing the Fleet, Finances, Politics and World windows)" : "")}");
                    int hitches = 0; float worst = 0f, nextClick = Time.realtimeSinceStartup + 6f; int tab = 0, clicks = 0;
                    for (float w = Time.realtimeSinceStartup + think; Time.realtimeSinceStartup < w;)
                    {
                        ConfirmBoxes(); yield return null;
                        float dt = Time.unscaledDeltaTime; if (dt > 0.25f) hitches++; if (dt > worst) worst = dt;
                        if (browse && Time.realtimeSinceStartup >= nextClick) { nextClick = Time.realtimeSinceStartup + 6f; if (TopTab(Tabs[tab % Tabs.Length])) clicks++; tab++; }
                    }
                    if (browse) TopTab("WORLD");
                    Log($"turn {t}/{turns}: on the map {think} s - {hitches} frame(s) over 250 ms, longest {worst * 1000f:0} ms{(browse ? $", {clicks} window(s) opened" : "")}");
                }
                Log($"turn {t}/{turns}: next turn");
                // 1.11.515 (#121): NextTurn(null) threw NullReferenceException at the end of the turn (onDone is called), so the next
                // turns stopped half-way and the times were wrong - the turn now gets a callback and is over when it is called
                // 1.11.516: a callback did not help (NullReferenceException again, the callback never came) - the turn is now started
                // the way the player starts it, with the game's own Next Turn button, and is over when the game's turn coroutine is gone
                turnDone = false; float tEnd = -1f; bool seenRunning = false;
                try { G.ui.NextTurnButton.onClick.Invoke(); } catch (Exception e) { Log("Next Turn button: " + e.Message); break; }
                float t0 = Time.realtimeSinceStartup; bool done = false;
                while (!done && Time.realtimeSinceStartup < t0 + 1200f)
                {
                    yield return null;
                    var pop = Popup();
                    if (pop != null)
                    {
                        if (fight > 0) { if (!Click(pop.Battle, "Battle")) Click(pop.Fight, "Fight"); }
                        else Click(pop.AutoResolve, "Auto-resolve");
                        for (float w = Time.realtimeSinceStartup + 2f; Time.realtimeSinceStartup < w;) yield return null;
                        continue;
                    }
                    bool battle = false; try { battle = GameManager.IsBattle && !GameManager.IsLoadingAny; } catch { }
                    if (battle)
                    {
                        Log($"battle running for {secs} s");
                        float b0 = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup < b0 + secs) { bool still = false; try { still = GameManager.IsBattle; } catch { } if (!still) break; yield return null; }
                        try { var bm = BattleManager.Instance; if (GameManager.IsBattle && bm != null) { bm.FinishCombat(bm.CurrentBattle, true); Log("battle finished (interrupted)"); } } catch (Exception e) { Log("FinishCombat: " + e.Message); }
                        yield return Wait(WorldReady, 600f);
                        continue;
                    }
                    ConfirmBoxes();
                    // 1.11.512: the turn is over when the game's turn steps have been quiet for 5 s (the map stays on screen meanwhile)
                    bool runningNow = false; try { runningNow = G.ui.nextTurnCoroutine != null; } catch { }
                    if (runningNow) seenRunning = true;
                    else if (seenRunning || Time.realtimeSinceStartup > t0 + 30f) { bool quiet = DesignAheadTiming.LastTurnStep < 0 || Time.realtimeSinceStartup - DesignAheadTiming.LastTurnStep > 3f; if (quiet && WorldReady()) turnDone = true; }
                    if (turnDone && tEnd < 0) { tEnd = Time.realtimeSinceStartup; Log($"turn {t}: the game's end of turn took {tEnd - t0:0.0} s"); }
                    if (turnDone && WorldReady() && Popup() == null && Time.realtimeSinceStartup > tEnd + 3f) { done = true; Log($"turn {t} ended after {tEnd - t0:0.0} s"); }
                }
                try { date = CampaignController.Instance.CurrentDate.AsDate().ToString("yyyy-MM"); } catch { }
                Log($"turn {t} done: {date}");
            }
            Log("campaign test finished - quitting");
            running = false;
            Application.Quit();
        }
    }
}
