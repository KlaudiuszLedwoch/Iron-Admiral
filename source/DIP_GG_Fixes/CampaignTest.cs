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

        static IEnumerator Run(string spec)
        {
            int slot = Int(spec, "slot", 2), turns = Int(spec, "turns", 3), fight = Int(spec, "fight", 1), secs = Int(spec, "seconds", 600);
            if (slot != 2) { Log($"slot {slot} refused - only the test campaign slot 2 may be loaded"); running = false; yield break; }
            string name = null;
            try { GameManager.Instance.SetCampaignSlotIndex(slot); name = GameManager.Instance.GetCampaignSaveName(slot); } catch (Exception e) { Log("slot: " + e.Message); }
            Log($"loading the test campaign from slot {slot} ({name})");
            try { GameManager.Instance.ContinueCampaign(null, false, name); } catch (Exception e) { Log("ContinueCampaign: " + e.Message); running = false; yield break; }
            yield return Wait(WorldReady, 900f);
            if (!WorldReady()) { Log("the campaign did not load in 15 min - quitting"); Application.Quit(); yield break; }
            string date = ""; try { date = CampaignController.Instance.CurrentDate.AsDate().ToString("yyyy-MM"); } catch { }
            Log($"campaign loaded: {date}");
            for (int t = 1; t <= turns; t++)
            {
                Log($"turn {t}/{turns}: next turn");
                try { var cc = CampaignController.Instance; GameManager.Instance.StartCoroutine(cc.NextTurn(null)); } catch (Exception e) { Log("NextTurn: " + e.Message); break; }
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
                    if (WorldReady() && Time.realtimeSinceStartup > t0 + 20f && Popup() == null) done = true;
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
