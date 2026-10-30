// Iron Admiral - in-game treasury information (DIP_GG_Fixes 1.11.25)
//   1. Treasury panel on the campaign map (IMGUI, toggled with F8): inflation and its causes this month, what prices,
//      interest, loan repayment and savings mean for the monthly budget, growth bonus, series production of the ships
//      under construction and a table of the great powers.
//   2. Finances window: the game's own "Annual inflation" and "Inflation losses" fields show the mod's values.
//   3. Monthly treasury report after each turn (can be switched off from the report itself) - warnings for big events
//      are part of it; with the report off, the warnings still come as separate messages.
using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using UnityEngine;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class EconomyUi
    {
        internal static MelonPreferences_Entry<bool> PanelOn, ReportOn, WarningsOn, FinancesWindow;
        internal static MelonPreferences_Entry<string> PanelKey;
        private static bool visible = true;
        private static KeyCode key = KeyCode.F8;
        private static string text = ""; private static float built = -100f; private static int lines = 1;
        private static GUIStyle style;
        private static string pendingTitle, pendingText; private static bool reportOpen; private static float pendingSince; private static string lastReportWait;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            PanelOn = cat.CreateEntry("TreasuryPanel", true, null, "Treasury panel on the campaign map (inflation, credit, interest, growth, series production, great powers)");
            PanelKey = cat.CreateEntry("TreasuryPanelKey", "F8", null, "Key that shows / hides the treasury panel (Unity KeyCode name)");
            ReportOn = cat.CreateEntry("TreasuryMonthlyReport", true, null, "Monthly treasury report after each turn");
            WarningsOn = cat.CreateEntry("TreasuryWarnings", true, null, "Messages for important treasury events (inflation jumps, loans, bankruptcy, repayment) - inside the monthly report, or on their own when the report is off");
            FinancesWindow = cat.CreateEntry("TreasuryInFinancesWindow", true, null, "Show the mod's inflation and inflation losses in the game's Finances window");
            KeyCode k; if (Enum.TryParse(PanelKey.Value, true, out k)) key = k;
        }

        static string M(float v)
        {
            float a = Math.Abs(v);
            string s = a >= 1e9f ? (a / 1e9f).ToString("0.00") + " bn" : (a / 1e6f).ToString("0") + "M";
            return (v < 0f ? "-" : "") + s;
        }

        static Player Main()
        {
            try { foreach (var p in Economy.MajorsPublic()) if (p.isMain) return p; } catch { }
            return null;
        }

        internal static string mapWhy;
        static bool OnMap()
        {
            try
            {
                bool world = false, wmap = false; try { world = GameManager.IsWorld; } catch { } try { wmap = GameManager.IsWorldMap; } catch { }
                if (!world && !wmap)
                {
                    string st = "?"; try { st = GameManager.Instance.CurrentState.ToString(); } catch { }
                    mapWhy = "not the world map (state " + st + ")"; return false;
                }
                bool loading = false; try { loading = GameManager.IsLoadingScreenActive; } catch { }
                if (loading) { mapWhy = "loading screen"; return false; }
                mapWhy = null; return true;
            }
            catch (Exception e) { mapWhy = "check failed: " + e.Message; return false; }
        }

        internal static void Tick()
        {
            try
            {
                if (PanelOn != null && PanelOn.Value && Input.GetKeyDown(key)) visible = !visible;
                ShowPendingReport();
                Patch_FinancesRefresh.Tick();
            }
            catch { }
        }

        private static bool guiLogged; private static int guiErrors; private static string lastSkip;
        internal static void OnGUI()
        {
            try
            {
                if (!guiLogged) { guiLogged = true; MelonLogger.Msg("[DIP GG Fixes][Economy] treasury panel: OnGUI active (F8 toggles)"); }
                string skip = PanelOn == null || !PanelOn.Value ? "panel off in settings" : !visible ? "hidden with the key" : !Economy.On ? "economy off" : !OnMap() ? "not on the campaign map (" + mapWhy + ")" : null;
                if (skip != lastSkip) { lastSkip = skip; MelonLogger.Msg("[DIP GG Fixes][Economy] treasury panel: " + (skip ?? "drawing")); }
                if (skip != null) return;
                float t = Time.realtimeSinceStartup;
                if (t - built > 1f) { Build(); built = t; }
                if (style == null)
                {
                    style = new GUIStyle();
                    style.fontSize = 13; style.wordWrap = false; style.richText = true;
                    style.normal.textColor = Color.white;
                }
                // 1.11.90: height from the real text height (the fixed 16.5 px per line left the lower half without a background)
                float w = 470f, h = Mathf.Max(18f + lines * 16.5f, style.CalcHeight(new GUIContent(text), w - 16f) + 14f);
                var r = new Rect(UiScale.W - w - 12f, 90f, w, h);
                GUI.Box(r, "");
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 8f, r.y + 6f, w - 16f, h - 8f), text, style);
            }
            catch (Exception e) { if (guiErrors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Economy] treasury panel draw failed: " + e); }
        }

        static void Build()
        {
            var sb = new StringBuilder();
            var p = Main();
            if (p == null) { text = ""; lines = 1; return; }
            var st = Economy.Get(p);
            if (st == null) { text = ""; lines = 1; return; }
            var b = Economy.PartsFor(p);
            float gdp = Economy.Gdp(p);
            sb.Append("<b>TREASURY</b>  (").Append(PanelKey.Value).Append(" hides)\n");
            float d = st.Inflation - st.MonthStartInflation;
            sb.Append($"Inflation: <b>{st.Inflation:0.0}%</b> a year ({d:+0.0;-0.0;0.0} this month)\n");
            if (st.Notes.Count > 0) sb.Append("  causes: ").Append(string.Join("; ", st.Notes.ToArray())).Append('\n');
            sb.Append($"Prices: all expenses {st.Inflation:+0.0;-0.0;0.0}% = {M(b.Surcharge)} a month\n");
            sb.Append($"Credit rating: {Economy.Rating(p, st)} - loans cost inflation + {Economy.TotalSpread(p, st):0.0} pts = {b.Rate:0.0}% a year\n");
            if (b.Debt > 0f) sb.Append($"Debt: {M(b.Debt)}, interest {M(b.DebtInterest)} a month{(b.Erosion > 0f ? $", inflation erodes {M(b.Erosion)} of it" : "")}\n");
            if (st.LoanMonths > 0) sb.Append($"War loan: {M(st.LoanBalance)} left, {st.LoanMonths} months, repayment {M(b.LoanPayment)} a month\n");
            if (b.Savings > 0f) sb.Append($"Savings: {M(p.cash)} earn {b.SavingsRate:0.0}% a year = +{M(b.Savings)} a month\n");
            if (b.Aviation > 0f) sb.Append($"Naval Aviation (carrier, aircraft, crews): {M(b.Aviation)} a month = {AirStrike.UpkeepPct(p):0}% of the fleet maintenance\n");
            int month = Economy.Month();
            if (st.BankruptUntil > month) sb.Append($"Bankruptcy on record until {st.BankruptUntil / 12} - credit costs more\n");
            if (st.CutsMonths > 0) sb.Append($"Fleet cuts: {st.CutsMonths} months left\n");
            if (st.ResearchPp + st.ExpandPp > 0.005f) sb.Append($"Growth bonus: +{st.ResearchPp + st.ExpandPp:0.00} pts a year (research {st.ResearchPp:0.00}, shipyards {st.ExpandPp:0.00})\n");
            sb.Append($"Net effect on the budget: {M(-b.Net)} a month\n");
            // series production
            if (Series.On)
            {
                var rows = new List<string>();
                try
                {
                    var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var s = en.Current; if (s == null) continue;
                        bool bld = false; try { bld = s.isBuilding; } catch { }
                        if (!bld) continue;
                        var inf = Series.Get(s);
                        string kind = inf.N == 1 ? "prototype" : (inf.Learn ? $"#{inf.N}" : $"#{inf.N}, waits for prototype");
                        string dn = ""; try { dn = s.design != null ? s.design.name : s.name; } catch { }
                        rows.Add($"  {dn} ({kind}{(inf.Dock >= 2 ? $", {inf.Dock} in dock" : "")}): cost x{inf.Cost:0.00}, time x{inf.Time:0.00}");
                    }
                }
                catch { }
                if (rows.Count > 0)
                {
                    sb.Append("<b>Under construction</b>\n");
                    var seen = new HashSet<string>(); int shown = 0;
                    foreach (var r in rows) { if (!seen.Add(r)) continue; if (shown++ >= 6) { sb.Append($"  ... {rows.Count} ships in all\n"); break; } sb.Append(r).Append('\n'); }
                }
            }
            // great powers
            sb.Append("<b>Great powers</b>  inflation / debt / cash / rating\n");
            try
            {
                foreach (var o in Economy.MajorsPublic())
                {
                    if (o == null || o.isDisabled) continue;
                    var os = Economy.Get(o); if (os == null) continue;
                    float debt = Math.Max(0f, -o.cash) + os.LoanBalance;
                    sb.Append($"  {Economy.Name(o)}: {os.Inflation:0.0}% / {(debt > 0f ? M(debt) : "-")} / {M(o.cash)} / {Economy.Rating(o, os)}\n");
                }
            }
            catch { }
            text = sb.ToString().TrimEnd('\n');
            lines = 1; foreach (char c in text) if (c == '\n') lines++;
        }

        // ------------------------------------------------------------------ monthly report
        internal static void Notice(string title, string text)
        {
            if (pendingText != null) { pendingText = "! " + text + "\n\n" + pendingText; pendingTitle = title; }
            else { pendingTitle = title; pendingText = text; pendingSince = 0f; }
        }

        internal static void QueueReport(Player p, EconState st)
        {
            try
            {
                bool report = ReportOn != null && ReportOn.Value, warn = WarningsOn != null && WarningsOn.Value;
                if (!report && !(warn && st.Events.Count > 0)) return;
                var b = Economy.PartsFor(p);
                var sb = new StringBuilder();
                if (st.Events.Count > 0) { foreach (var e in st.Events) sb.Append("! ").Append(e).Append('\n'); sb.Append('\n'); }
                if (report)
                {
                    sb.Append($"Inflation {st.MonthStartInflation:0.0}% -> {st.Inflation:0.0}% a year.\n");
                    if (st.Notes.Count > 0) sb.Append("Causes: ").Append(string.Join("; ", st.Notes.ToArray())).Append(".\n");
                    sb.Append($"Higher prices cost {M(b.Surcharge)} a month");
                    if (b.DebtInterest > 0f) sb.Append($", interest on {M(b.Debt)} of debt at {b.Rate:0.0}% (rating {Economy.Rating(p, st)}) {M(b.DebtInterest)}");
                    if (b.LoanPayment > 0f) sb.Append($", loan repayment {M(b.LoanPayment)} ({st.LoanMonths} months left)");
                    sb.Append(".\n");
                    if (b.Savings > 0f) sb.Append($"Unspent cash earned {M(b.Savings)} ({b.SavingsRate:0.0}% a year).\n");
                    if (st.ResearchPp + st.ExpandPp > 0.005f) sb.Append($"Economic growth bonus +{st.ResearchPp + st.ExpandPp:0.00} points (research, shipyards).\n");
                    sb.Append($"Cash {M(p.cash)}, monthly balance {M(p.Income())}.");
                }
                pendingTitle = st.Events.Count > 0 ? "Treasury - important" : "Treasury report";
                pendingText = sb.ToString().TrimEnd('\n');
                pendingSince = 0f;
                MelonLogger.Msg($"[DIP GG Fixes][Economy] monthly treasury report for {Economy.Name(p)} queued ({pendingTitle})");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Economy] report: " + e.Message); }
        }

        static void ShowPendingReport()
        {
            if (pendingText == null || reportOpen) return;
            string why = FinanceCrisis.Busy ? "treasury crisis dialog" : !OnMap() ? mapWhy : null;
            if (why != lastReportWait) { lastReportWait = why; MelonLogger.Msg("[DIP GG Fixes][Economy] monthly report: " + (why == null ? "showing" : "waiting - " + why)); }
            if (why != null) { pendingSince = 0f; return; }
            float t = Time.realtimeSinceStartup;
            if (pendingSince == 0f) { pendingSince = t; return; }
            if (t - pendingSince < 1.5f) return;
            var ui = G.ui; if (ui == null) return;
            string title = pendingTitle, body = pendingText; pendingText = null;
            reportOpen = true;
            try
            {
                ui.ReportMessage(title, body, "OK", "No more monthly reports",
                    UpdateCheck.Act(() => { reportOpen = false; }),
                    UpdateCheck.Act(() => { reportOpen = false; try { ReportOn.Value = false; MelonPreferences.Save(); } catch { } }));
                MelonLogger.Msg("[DIP GG Fixes][Economy] monthly report shown");
            }
            catch (Exception e) { reportOpen = false; MelonLogger.Warning("[DIP GG Fixes][Economy] report window: " + e.Message); }
        }
    }

    // the game's own inflation fields in the Finances window
    [HarmonyPatch(typeof(CampaignFinancesWindow), "Refresh")]
    internal static class Patch_FinancesRefresh
    {
        internal static CampaignFinancesWindow last;
        private static bool logged; private static int errors; private static float nextTick;

        private static void Postfix(CampaignFinancesWindow __instance)
        {
            last = __instance;
            Apply(__instance, true);
        }

        internal static void Tick()
        {
            if (last == null) return;
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (t < nextTick) return; nextTick = t + 0.5f;
            bool active = false; try { active = last.IsActive; } catch { last = null; return; }
            if (active) Apply(last, false);
        }

        static void Show(TMP_Text txt, string v)
        {
            if (txt == null) return;
            txt.text = v;
            // DIP hides the base game's inflation row - switch the label and its row back on
            var go = txt.gameObject; if (go != null && !go.activeSelf) go.SetActive(true);
            var tr = txt.transform.parent;
            for (int i = 0; i < 2 && tr != null; i++, tr = tr.parent)
            {
                if (tr.gameObject.activeSelf) continue;
                if (tr.GetComponent<CampaignFinancesWindow>() != null) break;
                tr.gameObject.SetActive(true);
            }
        }

        static void Apply(CampaignFinancesWindow w, bool fromRefresh)
        {
            try
            {
                if (EconomyUi.FinancesWindow == null || !EconomyUi.FinancesWindow.Value || !Economy.On || w == null) return;
                Player p = null; try { p = G.ui != null ? G.ui.GetPlayer() : null; } catch { }
                if (p == null) foreach (var q in Economy.MajorsPublic()) if (q.isMain) { p = q; break; }
                if (p == null || !p.isMajor) { if (!logged) { logged = true; MelonLogger.Msg("[DIP GG Fixes][Economy] Finances window: no player"); } return; }
                var st = Economy.Get(p); if (st == null) return;
                var b = Economy.PartsFor(p);
                string v; try { v = Ui.MoneyDelta(-b.Surcharge); } catch { v = (-b.Surcharge / 1e6f).ToString("0") + "M"; }
                Show(w.AnnualInflation, $"{st.Inflation:0.0}%");
                Show(w.InflationLoses, v);
                if (!logged)
                {
                    logged = true;
                    MelonLogger.Msg($"[DIP GG Fixes][Economy] Finances window: {Economy.Name(p)} inflation {st.Inflation:0.0}%, losses {v} (field inflation {(w.AnnualInflation != null ? "found, active " + w.AnnualInflation.gameObject.activeInHierarchy : "missing")}, losses {(w.InflationLoses != null ? "found, active " + w.InflationLoses.gameObject.activeInHierarchy : "missing")})");
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Economy] Finances window: " + e); }
        }
    }
}
