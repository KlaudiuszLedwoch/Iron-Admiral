// 1.11.69: battle report in the battle result window.
// While a battle is fought the add-on collects its key moments from its own battle AI decisions and from the game:
// first identification of an enemy ship, torpedo charges, massed torpedo attacks, ramming, withdrawals, escape scatter,
// crew panic, surrenders, sinkings and - after the battle - prizes and returned ships. When the game shows the battle result
// window (BattlePopup with the result block), the most important moments (at most 12, in order, with battle minutes) are
// appended to the result text - no extra window and no extra click. Switch: BattleReport.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Narrative
    {
        internal static MelonPreferences_Entry<bool> On;
        class Ev { public int order; public int prio; public float min; public string text; }
        static readonly List<Ev> evs = new List<Ev>();
        static readonly HashSet<string> keys = new HashSet<string>();
        static string battleId; static float start; static bool inBattle; static float nextScan;
        static readonly Dictionary<string, string> reports = new Dictionary<string, string>();
        static readonly HashSet<IntPtr> surrendered = new HashSet<IntPtr>();
        static BattlePopup popup; static int errors;
        const string Marker = "Battle report";

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BattleReport", true, null, "After a battle, the battle result window also shows a short report of what happened (first identification, torpedo charges, withdrawals, panic, surrenders, sinkings, prizes)");
        }
        static bool Active { get { return On != null && On.Value; } }
        static string Short(string n)
        {
            if (string.IsNullOrEmpty(n)) return n; int i = n.IndexOf(" ("); if (i > 0) n = n.Substring(0, i);
            i = n.IndexOf(" ["); if (i > 0) n = n.Substring(0, i); return n.Trim();
        }
        internal static void Record(string key, int prio, string text)
        {
            if (key != null && key.StartsWith("prize|")) { try { Dispatch.Prize(text); } catch { } }
            if (!Active || !inBattle && prio < 9) return;
            try
            {
                if (key != null && !keys.Add(key)) return;
                evs.Add(new Ev { order = evs.Count, prio = prio, min = Mathf.Max(0f, (Time.time - start) / 60f), text = text });
                if (!inBattle) Store(); // after-battle events (prizes) update the stored report
            }
            catch { }
        }

        // ------------------------------------------------------------------ events from the add-on's battle AI log
        // manual parsing (System.Text.RegularExpressions is not referenced by the mod)
        static string Before(string m, string sep) { int i = m.IndexOf(sep, StringComparison.Ordinal); return i > 0 ? m.Substring(0, i) : null; }
        static string After(string m, string sep) { int i = m.IndexOf(sep, StringComparison.Ordinal); return i >= 0 ? m.Substring(i + sep.Length) : null; }
        static string Subject(string m) { var x = Before(m, ") "); if (x == null) return null; int k = x.LastIndexOf(" ("); return k > 0 ? x.Substring(0, k) : x; }
        internal static void FromLog(string m)
        {
            if (!Active || !inBattle || string.IsNullOrEmpty(m)) return;
            try
            {
                if (m.StartsWith("[player scout] ")) m = m.Substring(15);
                if (m.StartsWith("massed torpedo attack ON for "))
                {
                    var nat = After(m, "massed torpedo attack ON for "); nat = Before(nat + ":", ":");
                    Record("massed|" + nat, 6, $"{Cap(nat)} threw its torpedo craft into a massed attack"); return;
                }
                if (m.StartsWith("PANIC: "))
                {
                    var rest = m.Substring(7); var ship = Before(rest, " ("); if (ship != null && rest.Contains("division retreats")) Record("panic|" + ship, 6, $"{Short(ship)}: the crew panicked and the division fell back"); return;
                }
                if (m.Contains(") charges ") && m.Contains(" m, torpedo range"))
                {
                    var subj = Before(m, ") charges "); int k = subj != null ? subj.LastIndexOf(" (") : -1; var ship = k > 0 ? subj.Substring(0, k) : subj;
                    var rest = After(m, ") charges "); var target = rest != null ? Before(rest, " (") : null;
                    var atTxt = rest != null ? Before(After(rest, ") at ") ?? "", " m") : null; float dist; float.TryParse(atTxt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dist);
                    if (ship != null && target != null) Record("charge|" + ship, 3, $"{Short(ship)} ran in at {Short(target)} to launch torpedoes{(dist > 0 ? $" ({dist / 1000f:0.0} km)" : "")}");
                    return;
                }
                if (m.Contains(") rams "))
                {
                    var subj = Before(m, ") rams "); int k = subj.LastIndexOf(" ("); var ship = k > 0 ? subj.Substring(0, k) : subj; var target = Before(After(m, ") rams ") ?? "", " (");
                    Record("ram|" + ship, 5, $"{Short(ship)}, out of torpedoes, turned to ram {Short(target)}"); return;
                }
                if (m.Contains("): escape mission - cannot outrun")) { var ship = Subject(m); Record("scatter|" + ship, 5, $"{Short(ship)}'s force could not outrun the pursuit and scattered"); return; }
                if (m.Contains("): enemy force too strong")) { var ship = Subject(m); Record("withdraw|" + ship, 4, $"{Short(ship)} withdrew - outmatched"); return; }
                if (m.Contains("): cannot hurt ") && m.Contains(" and can survive"))
                {
                    var ship = Subject(m); var target = Before(After(m, "): cannot hurt ") ?? "", " and can survive");
                    Record("withdraw|" + ship, 4, $"{Short(ship)} broke off - its guns could not penetrate {Short(target)}"); return;
                }
            }
            catch { }
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

        // ------------------------------------------------------------------ battle life cycle
        internal static void Tick()
        {
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (b && !inBattle)
            {
                inBattle = true; evs.Clear(); keys.Clear(); surrendered.Clear(); start = Time.time;
                battleId = null; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) battleId = cb.Id.ToString(); } catch { }
            }
            else if (!b && inBattle) { inBattle = false; Store(); }
            if (!inBattle || !Active || Time.time < nextScan) return;
            nextScan = Time.time + 2f;
            try
            {
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null) return;
                var ships = cb.Ships; if (ships == null) return;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    bool sur = false; try { sur = s.IsSurrendered; } catch { }
                    if (sur && surrendered.Add(s.Pointer)) { try { Dispatch.Surrendered(s.name); } catch { } } if (sur && surrendered.Contains(s.Pointer)) Record("surr|" + s.Pointer, 8, $"{Short(s.name)} struck her colours");
                }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Report] scan: " + e.Message); }
        }
        internal static void Sunk(Ship s) { try { if (s != null) Dispatch.Sunk(s.name); } catch { } if (s == null || !inBattle) return; try { Record("sunk|" + s.Pointer, 9, $"{Short(s.name)} was sunk"); } catch { } }
        static void Store()
        {
            if (battleId == null || evs.Count == 0) return;
            var pick = new List<Ev>(evs); pick.Sort((a, c) => c.prio != a.prio ? c.prio.CompareTo(a.prio) : a.order.CompareTo(c.order));
            if (pick.Count > 12) pick.RemoveRange(12, pick.Count - 12);
            pick.Sort((a, c) => a.order.CompareTo(c.order));
            var sb = new StringBuilder();
            sb.Append("<b>").Append(Marker).Append("</b>");
            foreach (var e in pick) sb.Append("\n").Append(e.min >= 1f ? $"{e.min:0} min: " : "").Append(e.text);
            if (evs.Count > pick.Count) sb.Append($"\n(+{evs.Count - pick.Count} smaller events in the log)");
            reports[battleId] = sb.ToString();
            MelonLogger.Msg("[DIP GG Fixes][Report] battle report ready (" + evs.Count + " events): " + sb.ToString().Replace("\n", " | ").Replace("<b>", "").Replace("</b>", ""));
        }

        // ------------------------------------------------------------------ show it in the result window
        internal static void SetPopup(BattlePopup p) { popup = p; }
        internal static void LateTick()
        {
            if (!Active || popup == null || reports.Count == 0) return;
            try
            {
                if (popup.BattleResultInfoObject == null || !popup.BattleResultInfoObject.activeInHierarchy || popup.BattleResultInfo == null) return;
                var cb = popup.CurrentBattle; if (cb == null) return;
                string r; if (!reports.TryGetValue(cb.Id.ToString(), out r)) return;
                var t = popup.BattleResultInfo.text ?? "";
                if (t.Contains(Marker)) return;
                popup.BattleResultInfo.text = t + (t.Length > 0 ? "\n\n" : "") + r;
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Report] window: " + e.Message); }
        }
        internal static void Apply(HarmonyLib.Harmony h)
        {
            try
            {
                var t = AccessTools.Method(typeof(Ui), "OnShipSinks", new[] { typeof(Ship) });
                if (t != null) h.Patch(t, null, new HarmonyMethod(AccessTools.Method(typeof(Narrative), nameof(Post_Sinks))));
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Report] hook OnShipSinks failed: " + e.Message); }
        }
        static void Post_Sinks(Ship ship) { Sunk(ship); }
    }
}
