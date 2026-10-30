// 1.11.89: unattended battle test (author's tool, off by default). From the main menu it sets up a custom battle from the
// AutoTestBattle preference, runs it for AutoTestSeconds of real time at the requested speed, writes a summary line per ship to the
// log and quits the game. Format: "1916;germany:bb=4;usa:bb=1,ca=1,cl=3,dd=6" (year; player side; AI side).
using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class AutoTest
    {
        internal static MelonPreferences_Entry<string> Spec;
        internal static MelonPreferences_Entry<float> Seconds, Speed;
        internal static MelonPreferences_Entry<bool> Shots;
        static bool started, running;
        static int shotN; static float nextBubbleShot;

        // 1.11.90: screenshots for the mod page (UserData\IronAdmiral\screens). ScreenCapture is looked up at run time, so the
        // build needs no extra reference.
        internal static void Shot(string tag)
        {
            try
            {
                var dir = System.IO.Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "screens");
                System.IO.Directory.CreateDirectory(dir);
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) tag = tag.Replace(ch, '_');
                tag = tag.Replace(' ', '_');
                var f = System.IO.Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{++shotN:00}_{tag}.png");
                Type t = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType("UnityEngine.ScreenCapture"); if (t != null) break; }
                if (t == null) { Log("screenshot: ScreenCapture not found"); return; }
                var m = t.GetMethod("CaptureScreenshot", new[] { typeof(string) });
                m.Invoke(null, new object[] { f });
                Log("screenshot " + f); SteamShots.ToLibrary(f, Screen.width, Screen.height, 1.5f);
            }
            catch (Exception e) { Log("screenshot: " + e.Message); }
        }

        static IEnumerator Later(float s, Action a)
        {
            for (float w = Time.realtimeSinceStartup + s; Time.realtimeSinceStartup < w;) yield return null;
            try { a(); } catch (Exception e) { Log("later: " + e.Message); }
        }

        // a dispatch is being drawn on screen above a ship: capture this frame (at most one per 12 s); the battle camera follows
        // the player's ships, so only dispatches in view are captured
        internal static void BubbleDrawn(string ship)
        {
            if (!running || Shots == null || !Shots.Value || Time.realtimeSinceStartup < nextBubbleShot || shotN > 45) return;
            nextBubbleShot = Time.realtimeSinceStartup + 8f;
            Shot("dispatch_" + ship);
        }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Spec = cat.CreateEntry("AutoTestBattle", "", null, "Author's tool: unattended custom battle test from the main menu, e.g. 1916;germany:bb=4;usa:bb=1,ca=1,cl=3,dd=6 (empty = off). The game quits when the test ends.");
            Seconds = cat.CreateEntry("AutoTestSeconds", 900f, null, "Author's tool: real-time length of the unattended battle test (s)");
            Speed = cat.CreateEntry("AutoTestTimeScale", 4f, null, "Author's tool: Unity time scale during the unattended battle test");
            Shots = cat.CreateEntry("AutoTestScreenshots", false, null, "Author's tool: take screenshots during the unattended battle test (UserData\\IronAdmiral\\screens)");
            LoadTimeout = cat.CreateEntry("AutoTestLoadTimeout", 2400f, null, "Author's tool: the unattended battle test quits when the battle has not loaded after this many seconds (design generation can take long)");
            Place = cat.CreateEntry("AutoTestPlace", "", null, "Author's tool: at the start of the unattended battle test place the AI ships abeam of the player's first ship, e.g. parallel:800 (800 m, same course); empty = the game's placement");
            StartWait = cat.CreateEntry("AutoTestStartWait", 600f, null, "Author's tool: in a screenshot run, how long (s) the test waits for the player to press Start Battle before it unpauses by itself");
            ShotEvery = cat.CreateEntry("AutoTestShotEvery", 120f, null, "Author's tool: in a screenshot run, seconds between overview screenshots");
            Orders = cat.CreateEntry("AutoTestOrders", "", null, "Author's tool: division orders given to the player's light divisions at the start of the unattended battle test, in turn, e.g. Screen,Scout,Torpedo,Free (empty = none)");
        }
        internal static MelonPreferences_Entry<string> Orders;
        // 1.11.283 (author 25.10, 20:24): forum report "1893, two light cruisers, parallel at under 1000 m" - the test can place the
        // AI ships abeam of the player's ships, wait less for "Start Battle" and take screenshots more often. Tools only.
        internal static MelonPreferences_Entry<string> Place;
        internal static MelonPreferences_Entry<float> StartWait, ShotEvery;
        static void PlaceShips()
        {
            if (Place == null || string.IsNullOrEmpty(Place.Value)) return;
            try
            {
                var p = Place.Value.Split(':'); if (p[0].Trim() != "parallel") { Log("place: unknown mode " + Place.Value); return; }
                float d = 800f; if (p.Length > 1) float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                var ships = BattleManager.Instance.CurrentBattle.Ships; var mine = new List<Ship>(); var ai = new List<Ship>();
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (s == null) continue; if (s.player != null && s.player.isMain) mine.Add(s); else ai.Add(s); }
                if (mine.Count == 0 || ai.Count == 0) { Log("place: no ships"); return; }
                var lead = mine[0].transform; var fwd = lead.forward; fwd.y = 0f; fwd.Normalize(); var right = Vector3.Cross(Vector3.up, fwd);
                for (int i = 0; i < ai.Count; i++)
                {
                    var s = ai[i]; var np = lead.position + right * d - fwd * (i * 600f); np.y = s.transform.position.y;
                    s.transform.SetPositionAndRotation(np, lead.rotation);
                    try { var rb = s.GetComponent<Rigidbody>(); if (rb != null) { rb.position = np; rb.rotation = lead.rotation; rb.velocity = Vector3.zero; } } catch { }
                    Log($"place: {s.name} set abeam of {mine[0].name} at {d:0} m, same course");
                }
            }
            catch (Exception e) { Log("place: " + e.Message); }
        }
        // "parallel:1000:hold" - the ships are kept side by side for the whole test: every second both divisions are sent ahead on the
        // player's course and an AI ship that has drifted more than 150 m from its slot abeam is moved half way back (author 25.10, 20:31)
        static float nextHold; static int holdMoves, holdErr;
        static void Hold()
        {
            if (Place == null || string.IsNullOrEmpty(Place.Value) || !Place.Value.EndsWith(":hold") || Time.realtimeSinceStartup < nextHold) return;
            nextHold = Time.realtimeSinceStartup + 1f;
            try
            {
                var p = Place.Value.Split(':'); float d = 800f; if (p.Length > 1) float.TryParse(p[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
                var ships = BattleManager.Instance.CurrentBattle.Ships; var mine = new List<Ship>(); var ai = new List<Ship>();
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (s == null || s.isSinking || s.isDead) continue; if (s.player != null && s.player.isMain) mine.Add(s); else ai.Add(s); }
                if (mine.Count == 0 || ai.Count == 0) return;
                var lead = mine[0]; var lt = lead.transform; var fwd = lt.forward; fwd.y = 0f; fwd.Normalize(); var right = Vector3.Cross(Vector3.up, fwd);
                try { lead.division.MoveTo(lt.position + fwd * 3000f); } catch { }
                for (int i = 0; i < ai.Count; i++)
                {
                    var s = ai[i]; var st = s.transform; var slot = lt.position + right * d - fwd * (i * 600f); slot.y = st.position.y;
                    try { s.division.MoveTo(slot + fwd * 3000f); } catch { }
                    var off = st.position - slot; off.y = 0f;
                    var sf = st.forward; sf.y = 0f;
                    if (off.magnitude > 150f || Vector3.Angle(sf, fwd) > 15f)
                    {
                        var np = st.position - off * 0.5f; var rot = Vector3.Angle(sf, fwd) > 15f ? lt.rotation : st.rotation;
                        st.SetPositionAndRotation(np, rot);
                        try { var rb = s.GetComponent<Rigidbody>(); if (rb != null) { rb.position = np; rb.rotation = rot; } } catch { }
                        holdMoves++;
                    }
                }
            }
            catch (Exception e) { if (holdErr++ < 3) Log("hold: " + e.Message); }
        }
        static string Geometry()
        {
            try
            {
                var ships = BattleManager.Instance.CurrentBattle.Ships; Ship a = null, b = null;
                for (int i = 0; i < ships.Count; i++) { var s = ships[i]; if (s == null || s.isSinking || s.isDead) continue; if (s.player != null && s.player.isMain) { if (a == null) a = s; } else if (b == null) b = s; }
                if (a == null || b == null) return "";
                var pa = a.transform.position; var pb = b.transform.position; pa.y = pb.y = 0f;
                var fa = a.transform.forward; fa.y = 0f; var fb = b.transform.forward; fb.y = 0f;
                float ang = Vector3.Angle(fa, fb);
                return $"range {(pa - pb).magnitude:0} m, courses {ang:0} deg apart, hold corrections {holdMoves}; ";
            }
            catch { return ""; }
        }
        internal static MelonPreferences_Entry<float> LoadTimeout; // 1.11.176

        // 1.11.157: the player's divisions get division orders (test of the autonomy) - light ships in turn, capital ships stay manual
        static void GiveOrders()
        {
            if (Orders == null || string.IsNullOrEmpty(Orders.Value)) return;
            try
            {
                var list = new List<BattleAi.DivOrder>();
                foreach (var x in Orders.Value.Split(',')) { BattleAi.DivOrder o; if (Enum.TryParse(x.Trim(), true, out o) && o != BattleAi.DivOrder.None) list.Add(o); }
                if (list.Count == 0) return;
                var b = BattleManager.Instance.CurrentBattle; var ships = b.Ships; int k = 0;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null || s.player == null || !s.player.isMain || !s.isDivLeader || s.division == null) continue;
                    if (BattleAi.IsCapital(s)) continue;
                    BattleAi.SetOrder(s.division, list[k % list.Count], "auto test"); k++;
                }
                Log($"division orders given to {k} light division(s): {Orders.Value}");
            }
            catch (Exception e) { Log("orders: " + e.Message); }
        }

        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AutoTest] " + m); }

        internal static void Tick()
        {
            // F11 (screenshot mode only): full-resolution screenshot of the game frame, including the mod's windows
            try { if (Shots != null && Shots.Value && Input.GetKeyDown(KeyCode.F11)) Shot("manual"); } catch { }
            if (started || Spec == null || string.IsNullOrEmpty(Spec.Value)) return;
            bool menu = false; try { menu = GameManager.IsMainMenu && !GameManager.IsLoadingAny; } catch { }
            if (!menu || Time.realtimeSinceStartup < 20f) return;
            started = true;
            MelonCoroutines.Start(Run(Spec.Value));
        }

        static void Fill(Ui.SkirmishSetup.Player p, string side, int year)
        {
            var kv = side.Split(':');
            p.country = G.GameData.players[kv[0].Trim()];
            p.year = year;
            var am = p.shipAmounts;
            var keys = new List<ShipType>(); foreach (var x in am) keys.Add(x.Key);
            foreach (var k in keys) am[k] = 0;
            if (kv.Length > 1)
                foreach (var e in kv[1].Split(','))
                {
                    var q = e.Split('='); if (q.Length != 2) continue;
                    ShipType st; if (!G.GameData.shipTypes.TryGetValue(q[0].Trim(), out st)) { Log("unknown ship type " + q[0]); continue; }
                    am[st] = int.Parse(q[1].Trim());
                }
        }

        // TweaksAndFixes replaces the custom battle setup: its UiM.skirmishSetupMod holds one entry per ship class (Guid -> number of ships)
        static void FillTaf(string side, int year, bool first)
        {
            Type uim = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType("TweaksAndFixes.UiM"); if (t != null) { uim = t; break; } }
            if (uim == null) { Log("TAF UiM not found"); return; }
            var mod = uim.GetField("skirmishSetupMod", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).GetValue(null);
            var pl = mod.GetType().GetField(first ? "player1" : "player2").GetValue(mod);
            var amounts = (Dictionary<ShipType, Dictionary<Guid, int>>)pl.GetType().GetField("shipAmounts").GetValue(pl);
            var avail = (Dictionary<ShipType, bool>)pl.GetType().GetField("shipTypeAvailible").GetValue(pl);
            pl.GetType().GetField("year").SetValue(pl, year);
            var kv = side.Split(':');
            try { pl.GetType().GetField("player").SetValue(pl, G.GameData.players[kv[0].Trim()]); } catch { }
            foreach (var k in new List<ShipType>(amounts.Keys)) amounts[k].Clear();
            var sb = new System.Text.StringBuilder($"TAF {(first ? "player" : "enemy")}: ");
            if (kv.Length > 1)
                foreach (var e in kv[1].Split(','))
                {
                    var q = e.Split('='); ShipType st; if (q.Length != 2 || !G.GameData.shipTypes.TryGetValue(q[0].Trim(), out st)) continue;
                    var d = new Dictionary<Guid, int>(); d[Guid.NewGuid()] = int.Parse(q[1].Trim());
                    amounts[st] = d; avail[st] = true;
                    sb.Append($"{st.name}={q[1]} ");
                }
            Log(sb.ToString());
        }

        static IEnumerator Run(string spec)
        {
            Log("starting: " + spec);
            var parts = spec.Split(';');
            int year = int.Parse(parts[0].Trim());
            try { GameManager.Instance.ToCustomBattleSetup(); } catch (Exception e) { Log("ToCustomBattleSetup: " + e.Message); yield break; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < t0 + 60f) { bool ok = false; try { ok = GameManager.IsCustomBattleSetup && !GameManager.IsLoadingAny && G.ui.skirmishSetup != null; } catch { } if (ok) break; yield return null; }
            for (float w = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < w;) yield return null;
            Ui.SkirmishSetup setup = null;
            try
            {
                setup = G.ui.skirmishSetup;
                Fill(setup.player1, parts[1], year);
                Fill(setup.player2, parts[2], year);
                setup.Dirty();
                try { FillTaf(parts[1], year, true); FillTaf(parts[2], year, false); } catch (Exception e) { Log("TAF setup: " + e); }
                try { BattleManager.Instance.PreInitCustomBattle(setup); } catch (Exception e) { Log("PreInitCustomBattle: " + e.Message); }
                var sb0 = new System.Text.StringBuilder("amounts: "); foreach (var x in setup.player1.shipAmounts) sb0.Append($"P {(x.Key != null ? x.Key.name : "?")}={x.Value} "); foreach (var x in setup.player2.shipAmounts) sb0.Append($"AI {(x.Key != null ? x.Key.name : "?")}={x.Value} "); if (setup.player1.isHullAvailable != null) foreach (var x in setup.player1.isHullAvailable) sb0.Append($"hullP {(x.Key != null ? x.Key.name : "?")}={x.Value} "); Log(sb0.ToString());
                string why1, why2;
                bool can = BattleManager.Instance.CanStartCustomBattle(setup, false, out why1, out why2);
                Log($"setup filled: {parts[1]} vs {parts[2]}, year {year}; can start = {can} {why1} {why2}");
                BattleManager.Instance.StartCustomBattle(setup, false);
            }
            catch (Exception e) { Log("setup/start: " + e); Application.Quit(); yield break; }
            t0 = Time.realtimeSinceStartup;
            bool loaded = false; float loadMax = LoadTimeout != null ? Math.Max(60f, LoadTimeout.Value) : 2400f; while (Time.realtimeSinceStartup < t0 + loadMax) { try { loaded = GameManager.IsBattle && !GameManager.IsLoadingAny && BattleManager.Instance.CurrentBattle != null && BattleManager.Instance.CurrentBattle.Ships != null && BattleManager.Instance.CurrentBattle.Ships.Count > 0; } catch { } if (loaded) break; yield return null; }
            if (!loaded) { Log($"battle did not load within {loadMax / 60f:0} min - quitting"); Application.Quit(); yield break; }
            Log("battle loaded after " + (Time.realtimeSinceStartup - t0).ToString("0") + " s");
            // the loading screen waits for "click to continue" and the first battle may open the help window: dismiss both
            for (float w = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < w;) yield return null;
            try { var b = G.ui.loadingUiButton; if (b != null) { b.onClick.Invoke(); Log("loading screen: continue pressed"); } } catch (Exception e) { Log("continue: " + e.Message); }
            try { G.ui.HideLoadingScreen(false, null); } catch (Exception e) { Log("hide loading: " + e.Message); }
            try { var h = G.ui.popupHelp; if (h != null) { h.SetActive(false); Log("help window closed"); } } catch (Exception e) { Log("help: " + e.Message); }
            bool shots = Shots != null && Shots.Value;
            if (shots)
            {
                // screenshot run: the battle stays paused behind the pre-battle panel until "Start Battle" is pressed (up to 10 min)
                PlaceShips(); float wt = Time.realtimeSinceStartup + (StartWait != null ? StartWait.Value : 600f); bool p = true;
                while (Time.realtimeSinceStartup < wt) { try { p = TimeControl.IsPaused(); } catch { p = false; } if (!p) break; yield return null; }
                Log("screenshot run: battle " + (p ? "still paused - unpausing" : "started by the player"));
                try { if (TimeControl.IsPaused()) TimeControl.Pause(false); } catch { }
                try { var c = Cam.Instance; c.distanceMax *= 3f; c.distanceSelectedCamYmax *= 3f; c.distanceDesired = c.distanceDesired * 2.5f; Log($"camera distance max {c.distanceMax:0}, selected max {c.distanceSelectedCamYmax:0}"); } catch (Exception e) { Log("camera: " + e.Message); }
            }
            else
                try { PlaceShips(); Log("paused = " + TimeControl.IsPaused()); if (TimeControl.IsPaused()) TimeControl.Pause(false); } catch (Exception e) { Log("pause: " + e.Message); }
            GiveOrders(); // 1.11.157
            GunLab.Run(); // 1.11.173
            float end = Time.realtimeSinceStartup + Seconds.Value; float next = 0f;
            running = true;
            float firstShot = Time.realtimeSinceStartup + 3f, nextOverview = firstShot + 5f; bool f10Done = false;
            nextBubbleShot = firstShot;
            while (Time.realtimeSinceStartup < end)
            {
                try { if (Time.timeScale < Speed.Value) Time.timeScale = Speed.Value; } catch { }
                Hold();
                if (Time.realtimeSinceStartup > next) { next = Time.realtimeSinceStartup + (Place != null && !string.IsNullOrEmpty(Place.Value) ? 10f : 60f); Summary("progress"); }
                if (shots && !f10Done && Time.realtimeSinceStartup > firstShot)
                {
                    f10Done = true;
                    try { SettingsUi.SetOpen(true); } catch (Exception e) { Log("F10: " + e.Message); }
                    MelonCoroutines.Start(Later(1.5f, () => Shot("f10_settings")));
                    MelonCoroutines.Start(Later(3f, () => SettingsUi.SetOpen(false)));
                }
                if (shots && Time.realtimeSinceStartup > nextOverview && shotN <= 60) { nextOverview = Time.realtimeSinceStartup + (ShotEvery != null ? Math.Max(5f, ShotEvery.Value) : 120f); Shot("battle_view"); }
                bool inBattle = false; try { inBattle = GameManager.IsBattle; } catch { }
                if (!inBattle) { Log("battle ended before the time limit"); break; }
                yield return null;
            }
            Summary("final");
            running = false;
            if (shots) { Shot("battle_final"); for (float w = Time.realtimeSinceStartup + 2f; Time.realtimeSinceStartup < w;) yield return null; }
            try { ArmorDiag.Summary(); } catch { } // 1.11.250
            Log("test finished - quitting");
            Application.Quit();
        }

        static void Summary(string tag)
        {
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null) return;
                var ships = b.Ships; if (ships == null) return;
                var sb = new System.Text.StringBuilder($"{tag} t={Time.time:0}s timeScale={Time.timeScale:0.0} night={BattleAi.IsNight()}: {Geometry()}");
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    string st = s.isSinking || s.isDead ? "sunk" : s.IsSurrendered ? "surrendered" : (s.division != null && (s.division.isRetreating || s.division.retreat)) ? "retreating" : "active";
                    float dmg = 0f; try { dmg = s.DamageEstimation(); } catch { }
                    sb.Append($"[{(s.player != null && s.player.isMain ? "P" : "AI")} {s.shipType.name} {s.name} {st} dmg {dmg:0.00}] ");
                }
                Log(sb.ToString());
            }
            catch (Exception e) { Log("summary: " + e.Message); }
        }
    }
}
