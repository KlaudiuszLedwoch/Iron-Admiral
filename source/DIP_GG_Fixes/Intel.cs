// 1.11.84: naval intelligence report (F7).
// A window on the campaign map lists every other great power's navy as the player's intelligence sees it: ships in service,
// ships building and designs on the drawing board. How much is known depends on the observer's intelligence level for that
// power and on how long a ship or design has existed:
//   national level  = 0.35 + era x reconnaissance technology x intelligence budget (the same factors as the fog of war,
//                     Fog.Era/TechF/BudgetF), +0.20 for an ally, -0.10 in war (secrecy), clamped to 0..1;
//   ship in service = national level + 0.08 per year since completion (up to +0.30) + 0.10 for capital ships;
//   ship building   = national level - 0.15 + 0.30 x building progress;
//   design          = national level - 0.30 + 0.10 per year since it was drawn (up to +0.30).
// Peacetime navies knew foreign ships in service well (naval attachés, annual reference books from 1898 on), ships on the
// slips less well and new designs hardly at all; the scale follows that order. Levels: >= 0.60 full data (name, class, main
// battery, speed, displacement); 0.35-0.60 partial (type, class, approximate displacement, main calibre); below that the
// ship only enters the type totals, which then carry an error. All rolls are deterministic per month and ship (Fog.Roll),
// nothing is saved. Switch: IntelReports; key: IntelKey (F7).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Intel
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<string> Key;
        static KeyCode key = KeyCode.F7;
        static bool visible; static int sel, tab, page; static int errors;
        static GUIStyle sTxt, sHead, sBtn, sTitle, sDim;
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>();
        static readonly List<KeyValuePair<Rect, Action>> next = new List<KeyValuePair<Rect, Action>>();
        class Row { public bool Head; public string Text; }
        static List<Row> rows = new List<Row>(); static float rowsBuilt = -10f; static int builtFor = -1;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("IntelReports", true, null, "Naval intelligence report (key below): other great powers' ships in service, building and designed, as accurate as your intelligence on each power allows");
            Key = cat.CreateEntry("IntelKey", "F7", null, "Key that opens the naval intelligence report");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Intel] " + m); }

        // ------------------------------------------------------------------ data
        static List<Player> Majors()
        {
            var l = new List<Player>();
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && !p.isDisabled && p.isMajor && !p.isMain) l.Add(p); } } catch { }
            return l.OrderBy(N).ToList();
        }
        static Player Main() { try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.isMain) return p; } } catch { } return null; }
        static string N(Player p) { try { return p.data.nameUi; } catch { return "?"; } }
        static string PN(Player p) { try { return p.data.name; } catch { return "?"; } }
        static List<Ship> Fleet(Player p)
        {
            var r = new List<Ship>();
            try { var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var s = en.Current; if (s != null) r.Add(s); } } catch { }
            return r;
        }
        static List<Ship> Designs(Player p)
        {
            var r = new List<Ship>();
            try { var en = p.designs.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var s = en.Current; if (s != null) r.Add(s); } } catch { }
            return r;
        }
        static string T(Ship s) { try { return s.shipType != null ? s.shipType.name : "?"; } catch { return "?"; } }
        static bool Capital(string t) { return t == "bb" || t == "bc"; }
        static int YearOf(GameDate g) { try { return g.AsDate().Year; } catch { return 0; } }
        static bool AtWar(Player a, Player b)
        {
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) return r.isWar; } } catch { } return false;
        }
        internal static float National(Player obs, Player target)
        {
            float q = 0.35f + Fog.Era(Fog.Year()) * Fog.TechF(obs) * Fog.BudgetF(obs);
            try { if (Fog.Friendly(obs, target)) q += 0.20f; } catch { }
            if (AtWar(obs, target)) q -= 0.10f;
            return Mathf.Clamp01(q);
        }
        static string MainBattery(Ship s, bool exact)
        {
            try
            {
                float top = 0f; int barrels = 0; var parts = s.parts;
                for (int i = 0; parts != null && i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d != null && d.isGun && d.caliber > top) top = d.caliber; }
                if (top <= 0f) return "no guns known";
                for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d != null && d.isGun && Mathf.Abs(d.caliber - top) < 0.01f) barrels += Math.Max(1, d.barrels); }
                float inch = top > 30f ? top / 25.4f : top;
                return exact ? $"{barrels} x {inch:0.#} in" : $"{inch:0} in guns";
            }
            catch { return "?"; }
        }
        static float Tons(Ship s) { try { return s.Tonnage(); } catch { return 0f; } }
        static float Knots(Ship s) { try { return s.speedMax * 1.94384f; } catch { return 0f; } }
        static string ClassOf(Ship s) { try { if (s.design != null && !string.IsNullOrEmpty(s.design.name)) return s.design.name; } catch { } return s.name; }
        static string Ton(float t) { return t.ToString("#,0", CultureInfo.InvariantCulture) + " t"; }
        static string TypeUi(string t)
        {
            switch (t) { case "bb": return "battleship"; case "bc": return "battlecruiser"; case "ca": return "heavy cruiser"; case "cl": return "light cruiser"; case "dd": return "destroyer"; case "tb": return "torpedo boat"; case "ss": return "submarine"; case "tr": return "transport"; default: return t; }
        }

        static List<Row> Build(Player obs, Player target)
        {
            var l = new List<Row>(); int m = Fog.Month(), year = Fog.Year();
            float nq = National(obs, target);
            string grade = nq >= 0.75f ? "good" : nq >= 0.55f ? "fair" : nq >= 0.40f ? "limited" : "poor";
            l.Add(new Row { Head = true, Text = $"{N(target)} - intelligence {grade} ({nq * 100f:0}%)" });
            var fleet = Fleet(target);
            var service = fleet.Where(s => { try { return !s.isSunk && !s.isBuilding && s.status != VesselEntity.Status.Scrapped && s.status != VesselEntity.Status.Erased; } catch { return false; } }).ToList();
            var building = fleet.Where(s => { try { return s.isBuilding; } catch { return false; } }).ToList();
            if (tab == 0)
            {
                // type totals: exact with good intelligence, otherwise with an error that grows as the level falls
                var types = new[] { "bb", "bc", "ca", "cl", "dd", "tb", "ss", "tr" };
                var parts = new List<string>();
                foreach (var t in types)
                {
                    int n = service.Count(s => T(s) == t); if (n == 0 && nq >= 0.6f) continue;
                    string txt;
                    if (nq >= 0.6f) txt = n.ToString();
                    else
                    {
                        float err = (0.6f - nq) * 1.2f; float r = Fog.Roll($"intel|{m}|{PN(obs)}|{PN(target)}|{t}") * 2f - 1f;
                        int est = Math.Max(0, (int)Mathf.Round(n * (1f + err * r)));
                        int lo = Math.Max(0, (int)Mathf.Floor(est * (1f - err))), hi = (int)Mathf.Ceil(est * (1f + err));
                        if (hi == 0) continue;
                        txt = lo == hi ? $"about {est}" : $"{lo}-{hi}";
                    }
                    parts.Add($"{TypeUi(t)}s {txt}");
                }
                l.Add(new Row { Text = "In service: " + (parts.Count > 0 ? string.Join(", ", parts) : "nothing known") });
                foreach (var s in service.OrderBy(x => Array.IndexOf(types, T(x))).ThenByDescending(Tons))
                {
                    string t = T(s);
                    int done = YearOf(s.dateFinished); int age = done > 0 ? Math.Max(0, year - done) : 0;
                    float k = nq + Math.Min(0.30f, 0.08f * age) + (Capital(t) ? 0.10f : 0f) - 0.10f * Fog.Roll($"intel|{m}|{s.id}");
                    if (k >= 0.60f) l.Add(new Row { Text = $"{TypeUi(t)} <b>{s.name}</b> ({ClassOf(s)} class) - {MainBattery(s, true)}, {Knots(s):0} kn, {Ton(Tons(s))}{(done > 0 ? $", completed {done}" : "")}" });
                    else if (k >= 0.35f)
                    {
                        float tt = Tons(s) * (0.9f + 0.2f * Fog.Roll($"intelT|{m}|{s.id}"));
                        l.Add(new Row { Text = $"{TypeUi(t)} {ClassOf(s)} class - {MainBattery(s, false)}, about {Ton(Mathf.Round(tt / 500f) * 500f)}" });
                    }
                }
            }
            else if (tab == 1)
            {
                int hidden = 0;
                l.Add(new Row { Head = true, Text = "Building" });
                foreach (var s in building.OrderByDescending(Tons))
                {
                    string t = T(s); float prog = 0f; try { prog = s.buildingProgress; } catch { }
                    float p01 = prog > 1.5f ? prog / 100f : prog;
                    float k = nq - 0.15f + 0.30f * Mathf.Clamp01(p01) + (Capital(t) ? 0.05f : 0f) - 0.10f * Fog.Roll($"intelB|{m}|{s.id}");
                    if (k >= 0.55f) l.Add(new Row { Text = $"{TypeUi(t)} <b>{s.name}</b> ({ClassOf(s)} class) - {MainBattery(s, true)}, {Ton(Tons(s))}, about {Mathf.Clamp01(p01) * 100f:0}% complete" });
                    else if (k >= 0.30f) l.Add(new Row { Text = $"a {TypeUi(t)} on the slips, about {Ton(Mathf.Round(Tons(s) * (0.85f + 0.3f * Fog.Roll($"intelBT|{m}|{s.id}")) / 1000f) * 1000f)}" });
                    else hidden++;
                }
                if (building.Count == 0 || hidden == building.Count) l.Add(new Row { Text = nq >= 0.6f ? "No ships known to be building." : "Nothing known about ships building." });
                else if (hidden > 0) l.Add(new Row { Text = "Further work in the yards is reported but not identified." });
            }
            else
            {
                l.Add(new Row { Head = true, Text = "Designs" });
                var built = new HashSet<string>(); foreach (var s in fleet) { try { if (s.design != null) built.Add(s.design.id.ToString()); } catch { } }
                int shown = 0, total = 0;
                foreach (var d in Designs(target).OrderByDescending(x => YearOf(x.dateCreated)))
                {
                    total++;
                    int drawn = YearOf(d.dateCreated); int age = drawn > 0 ? Math.Max(0, year - drawn) : 0;
                    bool hasShips = built.Contains(d.id.ToString());
                    float k = nq - 0.30f + Math.Min(0.30f, 0.10f * age) + (hasShips ? 0.25f : 0f) - 0.10f * Fog.Roll($"intelD|{m}|{d.id}");
                    if (k >= 0.50f) { shown++; l.Add(new Row { Text = $"{TypeUi(T(d))} {d.name}{(drawn > 0 ? $" ({drawn})" : "")} - {MainBattery(d, k >= 0.65f)}, {(k >= 0.65f ? Ton(Tons(d)) : "displacement unknown")}{(hasShips ? "" : ", no ship known to be built")}" }); }
                }
                if (shown == 0) l.Add(new Row { Text = "No designs known." });
                else if (shown < total && nq >= 0.6f) l.Add(new Row { Text = "Other designs are believed to exist." });
            }
            return l;
        }

        // ------------------------------------------------------------------ window
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool map = false; try { map = GameManager.IsCampaign && !GameManager.IsLoadingAny; } catch { }
                if (!map) { visible = false; return; }
                if (Input.GetKeyDown(key)) { visible = !visible; rowsBuilt = -10f; if (visible) Log("report opened"); }
                if (visible && Input.GetMouseButtonDown(0))
                {
                    var mp = Input.mousePosition; var p = new Vector2(mp.x, Screen.height - mp.y);
                    foreach (var kv in clicks.ToList()) if (kv.Key.Contains(p)) { try { kv.Value(); } catch (Exception e) { Log("click: " + e.Message); } rowsBuilt = -10f; break; }
                }
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][Intel] " + e.Message); }
        }
        static void Styles()
        {
            if (sTxt != null) return;
            sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
            sHead = new GUIStyle(); sHead.fontSize = 14; sHead.richText = true; sHead.normal.textColor = new Color(1f, 0.85f, 0.35f);
            sTxt = new GUIStyle(); sTxt.fontSize = 13; sTxt.richText = true; sTxt.wordWrap = true; sTxt.normal.textColor = Color.white;
            sDim = new GUIStyle(); sDim.fontSize = 12; sDim.richText = true; sDim.wordWrap = true; sDim.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.richText = true; sBtn.normal.textColor = Color.white;
        }
        static void Button(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }

        internal static void OnGUI()
        {
            if (On == null || !On.Value || !visible) return;
            try
            {
                var main = Main(); if (main == null) return;
                var majors = Majors(); if (majors.Count == 0) return;
                sel = Mathf.Clamp(sel, 0, majors.Count - 1);
                Styles(); next.Clear();
                float w = Math.Min(860f, Screen.width - 40f), h = Math.Min(660f, Screen.height - 120f);
                var r = new Rect((Screen.width - w) / 2f, 80f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 120f, 24f), $"<b>Naval intelligence</b>   ({Key.Value} closes)", sTitle);
                Button(new Rect(r.xMax - 40f, r.y + 8f, 30f, 26f), "X", () => visible = false);
                // nation buttons (two rows if needed)
                float bx = r.x + 14f, by = r.y + 40f, bw = 118f;
                for (int i = 0; i < majors.Count; i++)
                {
                    if (bx + bw > r.xMax - 14f) { bx = r.x + 14f; by += 28f; }
                    int ii = i; string lab = N(majors[i]); if (lab.Length > 15) lab = lab.Substring(0, 14) + ".";
                    Button(new Rect(bx, by, bw - 4f, 24f), (sel == i ? "<color=#FFD95A><b>" : "") + lab + (sel == i ? "</b></color>" : ""), () => { sel = ii; page = 0; });
                    bx += bw;
                }
                by += 32f;
                string[] tabs = { "In service", "Building", "Designs" };
                for (int i = 0; i < 3; i++) { int ti = i; Button(new Rect(r.x + 14f + i * 150f, by, 140f, 24f), (tab == i ? "<color=#FFD95A><b>" : "") + tabs[i] + (tab == i ? "</b></color>" : ""), () => { tab = ti; page = 0; }); }
                by += 32f;
                int key2 = sel * 10 + tab;
                if (Time.realtimeSinceStartup - rowsBuilt > 2f || builtFor != key2) { rows = Build(main, majors[sel]); rowsBuilt = Time.realtimeSinceStartup; builtFor = key2; }
                float rowH = 22f; int per = Math.Max(1, (int)((r.yMax - 44f - by) / rowH));
                int pages = Math.Max(1, (rows.Count + per - 1) / per); page = Mathf.Clamp(page, 0, pages - 1);
                float y = by;
                foreach (var row in rows.Skip(page * per).Take(per)) { GUI.Label(new Rect(r.x + 14f, y, w - 28f, rowH), row.Text, row.Head ? sHead : sTxt); y += rowH; }
                float fy = r.yMax - 34f;
                GUI.Label(new Rect(r.x + 14f, fy + 4f, w - 260f, 22f), "Accuracy depends on reconnaissance technology, intelligence budget and the age of each ship or design.", sDim);
                if (pages > 1)
                {
                    Button(new Rect(r.xMax - 230f, fy, 40f, 24f), "<", () => { if (page > 0) page--; });
                    GUI.Label(new Rect(r.xMax - 184f, fy + 3f, 110f, 22f), $"page {page + 1}/{pages}", sTxt);
                    Button(new Rect(r.xMax - 70f, fy, 40f, 24f), ">", () => { page++; });
                }
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Intel] window: " + e); }
        }
    }
}
