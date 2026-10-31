// 1.11.507 (#121, author 31.10: "make ship design in particular happen while the player is thinking"): at the end of turn of
// 09.1926 the game's fleet management of the AI took 35 s of 53 s, most of it generating new ship designs. While the player is on
// the world map during his own turn, the AI great powers now run the game's own design generation in the background, one nation
// at a time and one step per frame, so less is left for the end of turn. Each nation at most once per game month.
// The pass stops at once (the unfinished step is dropped) when the player leaves the map, opens the designer, a battle starts or
// the end of turn begins. Any error switches it off for the session. Log tag [DesignAhead].
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2Cpp;
using MelonLoader;

namespace DipGgFixes
{
    internal static class DesignAhead
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static bool Driving;                 // true only while our MoveNext runs
        static Il2CppSystem.Collections.IEnumerator it;
        static readonly List<Il2CppSystem.Collections.IEnumerator> stack = new List<Il2CppSystem.Collections.IEnumerator>();
        static Player cur; static string curName;
        static double curMs; static int curSteps, curBefore;
        static float mapSince = -1f, lastInput = -1f; static bool broken;
        static int month = int.MinValue;
        static readonly HashSet<string> doneThisMonth = new HashSet<string>();
        static UnityEngine.Vector3 lastMouse;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DesignAhead", true, null, "Campaign AI: while you are on the world map in your turn, the AI great powers prepare their new ship designs in the background (one nation at a time, each at most once a month), so the end of turn is shorter");
        }

        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][DesignAhead] " + m); }

        internal static void Abort(string why)
        {
            if (it == null) return;
            Log($"{curName}: stopped ({why}) after {curSteps} step(s), {curMs / 1000.0:0.0} s - the rest is left to the end of turn");
            if (curName != null) doneThisMonth.Add(curName);
            it = null; stack.Clear(); cur = null; curName = null;
        }

        static int Designs(Player p)
        {
            int n = 0;
            try { var e = p.designs.GetEnumerator(); var u = e.Cast<Il2CppSystem.Collections.IEnumerator>(); while (u.MoveNext()) n++; } catch { return -1; }
            return n;
        }

        static bool MapFree()
        {
            try
            {
                if (!GameManager.IsCampaign || !GameManager.IsWorldMap) return false;
                if (GameManager.IsBattle || GameManager.IsConstructor || GameManager.IsLoadingAny || GameManager.IsLoadingScreenActive) return false;
            }
            catch { return false; }
            // a step of the end of turn was seen in the last 10 s
            if (DesignAheadTiming.LastTurnStep > 0 && UnityEngine.Time.realtimeSinceStartup - DesignAheadTiming.LastTurnStep < 10f) return false;
            return true;
        }

        internal static void Tick()
        {
            if (broken || On == null || !On.Value) { if (it != null) Abort("switched off"); return; }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!MapFree()) { mapSince = -1f; if (it != null) Abort("map left"); return; }
            if (mapSince < 0) mapSince = now;
            // the player is "thinking" when the mouse rests and no key is held; a running pass goes on while he only looks around
            try
            {
                var m = UnityEngine.Input.mousePosition;
                if (UnityEngine.Input.anyKey || (m - lastMouse).sqrMagnitude > 4f) lastInput = now;
                lastMouse = m;
            }
            catch { }
            if (now - mapSince < 10f) return;
            try
            {
                if (it == null)
                {
                    // 1.11.517 (author 31.10, 17:35: "let the designs ahead run also while the player clicks and gives orders on the
                    // strategic map, and in the fleet, finances and diplomacy windows"): a pass no longer waits for the mouse to rest
                    // (those windows are part of the world map; only the designer, a battle, loading and the end of turn stop it)
                    Next();
                    return;
                }
                var sw = Stopwatch.StartNew(); bool more;
                // a step that yields another coroutine is driven like Unity would: the inner one first, to its end
                Driving = true;
                try
                {
                    var top = stack.Count > 0 ? stack[stack.Count - 1] : it;
                    more = top.MoveNext();
                    if (more)
                    {
                        Il2CppSystem.Collections.IEnumerator inner = null;
                        try { var c = top.Current; if (c != null) inner = c.TryCast<Il2CppSystem.Collections.IEnumerator>(); } catch { }
                        if (inner != null) stack.Add(inner);
                    }
                    else if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); more = true; }
                }
                finally { Driving = false; }
                sw.Stop(); curMs += sw.Elapsed.TotalMilliseconds; curSteps++;
                if (!more)
                {
                    int after = Designs(cur);
                    Log($"{curName}: design pass done on the map in {curSteps} step(s), {curMs / 1000.0:0.0} s; designs {curBefore} -> {after}");
                    doneThisMonth.Add(curName);
                    it = null; stack.Clear(); cur = null; curName = null;
                }
            }
            catch (Exception e)
            {
                broken = true; Driving = false; it = null; stack.Clear(); cur = null;
                MelonLogger.Warning("[DIP GG Fixes][DesignAhead] switched off for this session: " + (e.InnerException ?? e).Message);
            }
        }

        static void Next()
        {
            var cc = CampaignController.Instance; if (cc == null || cc.CampaignData == null) return;
            int mo = int.MinValue; try { mo = Fog.Month(); } catch { }
            if (mo != month) { month = mo; doneThisMonth.Clear(); }
            var ps = cc.CampaignData.Players;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i]; if (p == null) continue;
                bool main = false, major = false, disabled = true;
                try { main = p.isMain; major = p.isMajor; disabled = p.isDisabled; } catch { continue; }
                if (main || !major || disabled) continue;
                string name = "?"; try { name = p.data.name; } catch { }
                if (doneThisMonth.Contains(name)) continue;
                var e = cc.GenerateRandomDesigns(p, false);
                if (e == null) { doneThisMonth.Add(name); continue; }
                it = e; cur = p; curName = name; curMs = 0; curSteps = 0; curBefore = Designs(p);
                Log($"{name}: design pass started on the map ({curBefore} designs)");
                return;
            }
        }
    }
}
