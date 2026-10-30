// 1.11.470 (author 30.10 20:55, screenshot of the Finances window: the naval aviation slider belongs there, next to the game's own
// budget sliders, not under the F8 treasury panel): in the game's Finances window the Tech Budget row is copied once into a fourth
// slider, "Naval Aviation", 0-200% of the historical upkeep in steps of 10, with its monthly cost and the aircraft strength; the
// expense list gets a "Naval Aviation:" row (the cost is part of the game's Total Expenses through the expenses hook).
using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class AirBudgetUi
    {
        static CampaignFinancesWindow forWin; static GameObject row, costRow; static Slider slider; static TMP_Text pctText, costText, label, costValue;
        static bool busy, tech; static int errors, logs; static CampaignFinancesWindow failed;
        static void Log(string m) { if (logs++ < 20) MelonLogger.Msg("[DIP GG Fixes][AirBudget] " + m); }

        // the child of 'root' on the path to 't', and the path below it
        static string PathFrom(Transform root, Transform t) { string s = ""; for (var x = t; x != null && x != root; x = x.parent) s = x.name + (s.Length > 0 ? "/" + s : ""); return s; }
        static Transform Common(Transform a, Transform b)
        {
            for (var x = a; x != null; x = x.parent) for (var y = b; y != null; y = y.parent) if (x == y) return x;
            return null;
        }
        static string M(float v) { try { return Ui.MoneyDelta(v); } catch { return (v / 1e6f).ToString("0") + "M"; } }

        static bool Build(CampaignFinancesWindow w)
        {
            if (w.TechBudgetSlider == null || w.TechBudgetValue == null || w.TechBudgetTotalValue == null) { Log("Finances window: Tech Budget row not found - no aviation slider"); return false; }
            var common = Common(w.TechBudgetSlider.transform, Common(w.TechBudgetValue.transform, w.TechBudgetTotalValue.transform));
            if (common == null || common.parent == null) { Log("Finances window: no common parent of the Tech Budget row"); return false; }
            // the row: the topmost ancestor below the layout that holds the Tech Budget controls and nothing of the other sliders
            // the cell: the highest ancestor of the Tech Budget controls that does not also hold the Crew Training slider
            var rowSrc = common;
            while (rowSrc.parent != null && rowSrc.parent.GetComponent<CampaignFinancesWindow>() == null && (w.CrewTrainigSlider == null || !w.CrewTrainigSlider.transform.IsChildOf(rowSrc.parent)) && (w.TransportCapacitySlider == null || !w.TransportCapacitySlider.transform.IsChildOf(rowSrc.parent))) rowSrc = rowSrc.parent;
            bool flat = (w.CrewTrainigSlider != null && w.CrewTrainigSlider.transform.IsChildOf(rowSrc)) || (w.TransportCapacitySlider != null && w.TransportCapacitySlider.transform.IsChildOf(rowSrc));
            if (flat && (w.CrewTrainigSlider == null || w.CrewTrainigSlider.transform.parent != w.TechBudgetSlider.transform.parent)) { Log("Finances window: the sliders are laid out in an unknown way - no aviation slider"); return false; }
            string pSlider = PathFrom(rowSrc, w.TechBudgetSlider.transform), pPct = PathFrom(rowSrc, w.TechBudgetValue.transform), pCost = PathFrom(rowSrc, w.TechBudgetTotalValue.transform);
            row = UnityEngine.Object.Instantiate(rowSrc.gameObject, rowSrc.parent);
            row.name = "IA_NavalAviationRow";
            Strip(row);
            row.transform.SetSiblingIndex(rowSrc.GetSiblingIndex() + 1);
            if (flat)
            {
                // keep the Tech Budget children (the slider, its two values and its label - the label is the text nearest above the slider)
                var techRt = w.TechBudgetSlider.GetComponent<RectTransform>(); var crewRt = w.CrewTrainigSlider.GetComponent<RectTransform>();
                float dy = crewRt != null && techRt != null ? crewRt.anchoredPosition.y - techRt.anchoredPosition.y : 0f;
                float ty = techRt != null ? techRt.anchoredPosition.y : 0f, tx = techRt != null ? techRt.anchoredPosition.x : 0f;
                var keep = new System.Collections.Generic.HashSet<string> { w.TechBudgetSlider.name, w.TechBudgetValue.name, w.TechBudgetTotalValue.name };
                // the label: among the original's children, the text above the slider in the same column
                Transform lab = null; float best = float.MaxValue;
                for (int i = 0; i < rowSrc.childCount; i++)
                {
                    var c = rowSrc.GetChild(i); var rt = c.GetComponent<RectTransform>(); if (rt == null || c.GetComponent<TMP_Text>() == null || keep.Contains(c.name)) continue;
                    float d = rt.anchoredPosition.y - ty; if (d <= 0f || Mathf.Abs(rt.anchoredPosition.x - tx) > 200f) continue;
                    if (d < best) { best = d; lab = c; }
                }
                if (lab != null) keep.Add(lab.name);
                int removed = 0;
                for (int i = row.transform.childCount - 1; i >= 0; i--)
                {
                    var c = row.transform.GetChild(i);
                    if (!keep.Contains(c.name)) { UnityEngine.Object.DestroyImmediate(c.gameObject); removed++; continue; }
                    var rt = c.GetComponent<RectTransform>(); if (rt != null) rt.anchoredPosition += new Vector2(0f, dy);
                }
                var rr = row.GetComponent<RectTransform>(); if (rr != null && dy > 0f) rr.sizeDelta -= new Vector2(0f, dy);
                var le = row.GetComponent<LayoutElement>(); if (le != null && dy > 0f) { if (le.preferredHeight > 0f) le.preferredHeight -= dy; if (le.minHeight > 0f) le.minHeight -= dy; }
                Log($"Finances window: slider block copied, {removed} other slider part(s) removed, Tech Budget part(s) moved up {dy:0} (label '{(lab != null ? lab.name : "-")}')");
            }
            slider = pSlider.Length > 0 ? row.transform.Find(pSlider)?.GetComponent<Slider>() : row.GetComponent<Slider>();
            pctText = row.transform.Find(pPct)?.GetComponent<TMP_Text>();
            costText = row.transform.Find(pCost)?.GetComponent<TMP_Text>();
            // the label: a text of the row that is neither of the two values
foreach (var t in row.GetComponentsInChildren<TMP_Text>(true)) { if (t == null || t == pctText || t == costText) continue; string n0 = ""; try { n0 = t.gameObject.name; } catch { } if (flat && !(n0.Length > 0)) continue; label = t; break; }
            if (slider == null) { Log($"Finances window: the copied row has no slider (path '{pSlider}')"); UnityEngine.Object.Destroy(row); row = null; return false; }
            slider.onValueChanged.RemoveAllListeners();
            slider.minValue = 0f; slider.maxValue = 20f; slider.wholeNumbers = true;   // 0-20 = 0-200% in steps of 10
            slider.onValueChanged.AddListener((Action<float>)(v => OnSlide(v)));
            if (label != null) label.text = "Naval Aviation:";
            Log($"Finances window: naval aviation slider added (row '{rowSrc.name}' under '{rowSrc.parent.name}', slider '{pSlider}', percent '{pPct}', cost '{pCost}', label {(label != null ? "found" : "missing")})");
            // the expense row, copied from Fuel
            if (w.Fuel != null && w.Fuel.transform.parent != null)
            {
                var fr = w.Fuel.transform.parent; bool single = fr.GetComponent<CampaignFinancesWindow>() != null || fr.GetComponentsInChildren<TMP_Text>(true).Length > 3;
                if (!single)
                {
                    string pv = PathFrom(fr, w.Fuel.transform);
                    costRow = UnityEngine.Object.Instantiate(fr.gameObject, fr.parent); costRow.name = "IA_NavalAviationCost"; Strip(costRow);
                    costRow.transform.SetSiblingIndex(fr.GetSiblingIndex() + 1);
                    costValue = costRow.transform.Find(pv)?.GetComponent<TMP_Text>();
                    foreach (var t in costRow.GetComponentsInChildren<TMP_Text>(true)) if (t != costValue) { t.text = "Naval Aviation:"; break; }
                }
                else Log("Finances window: the Fuel row is not a row of its own - no Naval Aviation expense line");
            }
            return true;
        }

        // keep only Unity's UI components on a copy (the game's own scripts - tooltips, events - point at the original window)
        static void Strip(GameObject go)
        {
            int n = 0;
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string ns = ""; try { ns = c.GetIl2CppType().Namespace ?? ""; } catch { }
                if (ns.StartsWith("UnityEngine") || ns.StartsWith("TMPro")) continue;
                try { UnityEngine.Object.Destroy(c); n++; } catch { }
            }
            if (n > 0) Log($"Finances window: {n} game script(s) removed from the copied row");
        }
        static void OnSlide(float v)
        {
            if (busy) return;
            try
            {
                var p = Player(); if (p == null) return;
                AirBudget.SetPct(p, v * 10f);
                Show(p);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][AirBudget] slider: " + e.Message); }
        }
        static Player Player()
        {
            Player p = null; try { p = G.ui != null ? G.ui.GetPlayer() : null; } catch { }
            if (p == null) foreach (var q in Economy.MajorsPublic()) if (q.isMain) { p = q; break; }
            return p;
        }
        static void Show(Player p)
        {
            if (!tech)
            {
                busy = true; try { if (slider != null) slider.SetValueWithoutNotify(Mathf.Round(AirBudget.Pct(p) / 10f)); } finally { busy = false; }
                if (pctText != null) pctText.text = "needs seaplane stations or a seaplane carrier (research)";
                if (costText != null) costText.text = M(0f);
                if (costValue != null) costValue.text = M(0f);
                return;
            }
            float pct = AirBudget.Pct(p), cost = AirStrike.Upkeep(p);
            busy = true; try { if (slider != null) slider.SetValueWithoutNotify(Mathf.Round(pct / 10f)); } finally { busy = false; }
            if (pctText != null) pctText.text = $"{pct:0}% ({AirBudget.Stock(p) * 100f:0}% strength)";
            if (costText != null) costText.text = M(-cost);
            if (costValue != null) costValue.text = M(-cost);
        }

        // from the Finances window patch (Refresh and every 0.5 s while it is open)
        internal static void Apply(CampaignFinancesWindow w, Player p)
        {
            try
            {
                bool on = AirBudget.Enabled && p != null;
                int cl = 0, rl = 0; if (on) { try { cl = NavalAviation.CarrierLevel(p); rl = NavalAviation.ReconLevel(p); } catch { } }
                tech = cl > 0 || rl > 0;   // 1.11.474: the row is shown without the technology too, locked
                if (w != forWin || (on && row == null))
                {
                    if (w != forWin) { row = null; costRow = null; slider = null; pctText = costText = label = costValue = null; forWin = w; }
                    if (!on || failed == w) return;
                    if (!Build(w)) { failed = w; return; }
                }
                if (row != null && row.activeSelf != on) row.SetActive(on);
                if (costRow != null && costRow.activeSelf != on) costRow.SetActive(on);
                if (on) { if (slider != null && slider.interactable != tech) slider.interactable = tech; Show(p); }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][AirBudget] Finances window: " + e); }
        }
    }
}
