// 1.11.76: historical design builder (author tool). Reads Mods\IronAdmiralDesigns\historical_specs.csv and, for each
// historical class, opens the game's shared-design constructor for the class's nation and year, sets the hull, lets the
// game's own generator place turrets and superstructure (limited to the historical main calibre, main-gun count checked,
// several tries), then sets displacement, speed and armour from the specification, thins the armour until the weight fits
// (armament and speed kept, as decided), and saves the result as a shared design under the historical name.
// Runs once when HistBuilderRun = true (main menu), then switches itself off. Report: UserData\IronAdmiral\hist_builder.csv,
// ids of built designs: UserData\IronAdmiral\historical_design_ids.txt. Log: "[HistBuilder]".
// 1.11.108: an invalid prepared template is repaired (parts back on the nearest mount, unplaceable parts removed, main guns and
// main tower kept) instead of rejected; weight is fitted by range, armour and up to 2 kn of speed; a class that still fails
// is retried on up to HistBuilderAltHulls other hulls of the same type available to the nation in that year.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class HistDesigner
    {
        internal static MelonPreferences_Entry<bool> Run, RetryFailed;
        internal static MelonPreferences_Entry<string> Filter, Types;
        internal static MelonPreferences_Entry<int> Limit, Tries, AltHulls;
        static bool started;
        // active limits for the generator while a class is built (mm); 0 = inactive
        internal static float CapMainMm, CapSecMm; internal static int MainPerTurret, WantMain;
        // era filter: parts whose needunlock technology dates from after the class's year are not offered to the generator
        // (the shared-design constructor does not apply needunlock, so without this an 1899 battleship could get a 1921 tower)
        internal static int EraYear; internal static readonly Dictionary<string, int> PartEra = new Dictionary<string, int>(StringComparer.Ordinal);
        // arguments of every fn(...) in a param/effect cell, e.g. FnArgs("need(a), needunlock(b)", "needunlock") -> [b]
        static List<string> FnArgs(string s, string fn)
        {
            var r = new List<string>(); if (string.IsNullOrEmpty(s)) return r;
            string key = fn + "("; int i = 0;
            while ((i = s.IndexOf(key, i, StringComparison.Ordinal)) >= 0)
            {
                int a = i + key.Length, b = s.IndexOf(')', a); if (b < 0) break;
                r.Add(s.Substring(a, b - a).Trim()); i = b;
            }
            return r;
        }
        static void LoadPartEra()
        {
            PartEra.Clear();
            try
            {
                var mods = Path.Combine(MelonUtils.GameDirectory, "Mods");
                var unlock = new Dictionary<string, int>(StringComparer.Ordinal);
                var tl = File.ReadAllLines(Path.Combine(mods, "technologies.csv"));
                int th = Array.FindIndex(tl, l => l.StartsWith("@name,"));
                if (th < 0) { Log("era filter: technologies.csv header not found"); return; }
                var thd = SplitCsv(tl[th]); int iy = thd.IndexOf("year"), ie = thd.IndexOf("effect");
                if (iy < 0 || ie < 0) { Log("era filter: year/effect columns not found"); return; }
                for (int i = th + 1; i < tl.Length; i++)
                {
                    var c = SplitCsv(tl[i]); if (c.Count <= Math.Max(iy, ie)) continue;
                    int y; if (!int.TryParse(c[iy], out y) || y <= 0) continue;
                    foreach (var k in FnArgs(c[ie], "unlockpart")) { int o; if (!unlock.TryGetValue(k, out o) || y < o) unlock[k] = y; }
                }
                var pl = File.ReadAllLines(Path.Combine(mods, "parts.csv"));
                int ph = Array.FindIndex(pl, l => l.StartsWith("@name,"));
                if (ph < 0) { Log("era filter: parts.csv header not found"); return; }
                var phd = SplitCsv(pl[ph]); int ip = phd.IndexOf("param");
                if (ip < 0) { Log("era filter: param column not found"); return; }
                for (int i = ph + 1; i < pl.Length; i++)
                {
                    var c = SplitCsv(pl[i]); if (c.Count <= ip || c[0].Length == 0 || c[0].StartsWith("#")) continue;
                    int best = 0;
                    foreach (var k in FnArgs(c[ip], "needunlock")) { int y; if (unlock.TryGetValue(k, out y) && y > best) best = y; }
                    if (best > 0) PartEra[c[0]] = best;
                }
                Log($"era filter: {PartEra.Count} parts carry a technology date ({unlock.Count} unlock groups)");
            }
            catch (Exception e) { Log("era filter: " + e.Message); }
        }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Run = cat.CreateEntry("HistBuilderRun", false, null, "Author tool: build the historical shared designs from Mods/IronAdmiralDesigns/historical_specs.csv at the next visit of the main menu (switches itself off when done)");
            Filter = cat.CreateEntry("HistBuilderFilter", "", null, "Author tool: comma-separated nations and/or class names to build (empty = all)");
            RetryFailed = cat.CreateEntry("HistBuilderRetryFailed", false, null, "Author tool: also retry classes that failed in an earlier run (hist_builder.csv)");
            Types = cat.CreateEntry("HistBuilderTypes", "", null, "Author tool: comma-separated ship types to build, e.g. bb,bc (empty = all)");
            Limit = cat.CreateEntry("HistBuilderLimit", 0, null, "Author tool: build at most this many designs (0 = no limit)");
            Tries = cat.CreateEntry("HistBuilderTries", 8, null, "Author tool: generator attempts per class to match the historical main battery");
            AltHulls = cat.CreateEntry("HistBuilderAltHulls", 2, null, "Author tool: when a class fails on its hull, try this many other hulls of the same type available to the nation in that year (0 = off)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][HistBuilder] " + m); }

        class Spec { public Dictionary<string, string> f = new Dictionary<string, string>(); public string this[string k] { get { string v; return f.TryGetValue(k, out v) ? v : ""; } } public float N(string k) { float v; return float.TryParse(this[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f; } }

        static Dictionary<string, string> LoadCandidates()
        {
            var map = new Dictionary<string, string>();
            var dir = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns", "HistCandidates");
            var idx = Path.Combine(dir, "hist_candidates.csv");
            try { if (File.Exists(idx)) foreach (var l in File.ReadAllLines(idx).Skip(1)) { var c = SplitCsv(l); if (c.Count >= 3 && (File.Exists(Path.Combine(dir, c[2])) || File.Exists(Path.ChangeExtension(Path.Combine(dir, c[2]), ".raw")))) map[c[0] + "|" + c[1]] = Path.Combine(dir, c[2]); } } catch { }
            return map;
        }
        // Loads a prepared candidate design onto the constructor slip (as the shared designer does for stored designs).
        static Ship LoadCandidate(string path, Ship slip)
        {
            var rawPath = Path.ChangeExtension(path, ".raw");   // plain MessagePack (the game's reader does not take the LZ4 wrapper)
            var bytes = File.ReadAllBytes(File.Exists(rawPath) ? rawPath : path);
            var store = Util.DeserializeObjectByte<Ship.Store>(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes));
            if (store == null) { Log("template: store could not be read"); return null; }
            var n = Ship.Create(null, null, false, false, false);
            if (n == null) { Log("template: Ship.Create failed"); return null; }
            bool ok = n.FromStore(store, new Il2CppSystem.Nullable<Il2CppSystem.Guid>(), null, null, true);   // as the shared designer loads stored designs
            if (!ok) { Log("template: FromStore(null player) refused, retrying with the slip's player"); ok = n.FromStore(store, new Il2CppSystem.Nullable<Il2CppSystem.Guid>(), null, slip.player, true); }
            if (!ok) { Log("template: FromStore refused the design"); try { VesselEntity.TryToEraseVessel(n); } catch { } return null; }
            PlayerController.Instance.Ship = n;
            try { n.EnterConstructor(); } catch { }
            try { G.ui.RefreshLevel(); } catch { }
            try { G.ui.Refresh(false); } catch { }
            return n;   // the caller erases the old slip when the template is accepted, or switches back to it
        }

        static HashSet<string> dupNames = new HashSet<string>();
        // classes sharing a name within a navy (Lion 1912 / Lion 1939) get the year in the design name
        static string DesignName(Spec s)
        {
            string n = dupNames.Contains(s["nation"] + "|" + s["name"]) ? $"{s["name"]} ({s["year"]})" : s["name"];
            foreach (var ch in Path.GetInvalidFileNameChars()) n = n.Replace(ch, '-');   // the name is also the design file name
            return n.Replace('/', '-').Replace('\\', '-').Trim();
        }

        static List<Spec> LoadSpecs()
        {
            var path = Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralDesigns", "historical_specs.csv");
            var list = new List<Spec>(); if (!File.Exists(path)) { Log("no spec file " + path); return list; }
            var lines = File.ReadAllLines(path); if (lines.Length < 2) return list;
            var hdr = SplitCsv(lines[0]);
            for (int i = 1; i < lines.Length; i++) { var c = SplitCsv(lines[i]); var s = new Spec(); for (int j = 0; j < hdr.Count && j < c.Count; j++) s.f[hdr[j]] = c[j]; list.Add(s); }
            return list;
        }
        static List<string> SplitCsv(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); }
                else if (ch == '"') q = true; else if (ch == ',') { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch);
            }
            r.Add(sb.ToString()); return r;
        }

        internal static void Tick()
        {
            if (started || Run == null || !Run.Value) return;
            bool menu = false; try { menu = GameManager.IsMainMenu; } catch { }
            if (!menu || Time.realtimeSinceStartup < 30f) return;
            started = true;
            MelonCoroutines.Start(Build());
        }

        static IEnumerator Wait(float sec) { float t = Time.realtimeSinceStartup + sec; while (Time.realtimeSinceStartup < t) yield return null; }

        static IEnumerator Build()
        {
            var specs = LoadSpecs(); var cands = LoadCandidates(); int fromTemplate = 0;
            LoadPartEra();
            dupNames = new HashSet<string>(specs.GroupBy(x => x["nation"] + "|" + x["name"]).Where(g => g.Count() > 1).Select(g => g.Key));
            var filt = (Filter.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToList();
            if (filt.Count > 0) specs = specs.Where(s => filt.Contains(s["nation"].ToLowerInvariant()) || filt.Contains(s["name"].ToLowerInvariant()) || filt.Contains(s["cls"].ToLowerInvariant())).ToList();
            var types = (Types.Value ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToList();
            if (types.Count > 0) specs = specs.Where(s => types.Contains(s["type"].ToLowerInvariant())).ToList();
            // resume: skip classes already saved in an earlier run
            {
                var doneFile = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "historical_design_ids.txt");
                var have = new HashSet<string>();
                try { if (File.Exists(doneFile)) foreach (var l in File.ReadAllLines(doneFile)) { var c = l.Split(','); if (c.Length >= 3) have.Add(c[1] + "|" + c[2]); } } catch { }
                int before = specs.Count; specs = specs.Where(s => !have.Contains(s["nation"] + "|" + DesignName(s))).ToList();
                if (before != specs.Count) Log($"skipping {before - specs.Count} classes already built");
                if (!RetryFailed.Value)
                {
                    var failedSet = new HashSet<string>();
                    try { var rp = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "hist_builder.csv"); if (File.Exists(rp)) foreach (var l in File.ReadAllLines(rp).Skip(1)) { var c = SplitCsv(l); if (c.Count > 5 && c[5] == "failed") failedSet.Add(c[1] + "|" + c[3] + "|" + c[2]); } } catch { }
                    int b2 = specs.Count; specs = specs.Where(s => !failedSet.Contains(s["nation"] + "|" + s["name"] + "|" + s["year"])).ToList();
                    if (b2 != specs.Count) Log($"skipping {b2 - specs.Count} classes that failed earlier (HistBuilderRetryFailed = false)");
                }
                else
                {
                    // retry mode: classes that failed earlier go to the end of the queue, so a restart does not spend its first hour on the same failures
                    var failedSet = new HashSet<string>();
                    try { var rp = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "hist_builder.csv"); if (File.Exists(rp)) foreach (var l in File.ReadAllLines(rp).Skip(1)) { var c = SplitCsv(l); if (c.Count > 5 && c[5] == "failed") failedSet.Add(c[1] + "|" + c[3] + "|" + c[2]); } } catch { }
                    int nf = specs.Count(s => failedSet.Contains(s["nation"] + "|" + s["name"] + "|" + s["year"]));
                    if (nf > 0) { specs = specs.OrderBy(s => failedSet.Contains(s["nation"] + "|" + s["name"] + "|" + s["year"]) ? 1 : 0).ToList(); Log($"{nf} classes that failed earlier moved to the end of the queue"); }
                }
            }
            if (Limit.Value > 0 && specs.Count > Limit.Value) specs = specs.Take(Limit.Value).ToList();
            Log($"starting: {specs.Count} classes to build, {cands.Count} prepared templates");
            int oldFps = Application.targetFrameRate, oldVs = QualitySettings.vSyncCount;
            try { QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; } catch { }
            var dir = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral"); Directory.CreateDirectory(dir);
            var rep = Path.Combine(dir, "hist_builder.csv"); var idsFile = Path.Combine(dir, "historical_design_ids.txt");
            if (!File.Exists(rep)) File.WriteAllText(rep, "time,nation,year,class,hull,result,main_wanted,main_built,tonnage,weight,speed_kn,belt_wanted,belt_built,armour_scale,notes\n");
            int done = 0, failed = 0;
            foreach (var s in specs)
            {
                string result = "failed", notes = "", mainBuilt = ""; float weight = 0f, tonnage = 0f, beltBuilt = 0f, scale = 1f;
                // 1. open the shared design constructor for nation/year
                Ship prevShip = null; try { prevShip = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
                PlayerData pd = null; try { G.GameData.players.TryGetValue(s["nation"], out pd); } catch { }
                if (pd == null) { notes = "nation not found"; goto report; }
                {
                    int yr = (int)s.N("year");
                    bool inCons0 = false; try { inCons0 = GameManager.IsConstructor; } catch { }
                    if (!inCons0)
                    {
                        // enter the shared designer the way that reliably creates a ship (Britain 1906), then switch nation/year in place
                        PlayerData gb = null; try { G.GameData.players.TryGetValue("britain", out gb); } catch { }
                        try { G.ui.sharedDesignPlayer = gb ?? pd; G.ui.sharedDesignYear = 1906; GameManager.Instance.ToSharedDesignsConstructor(1906, gb ?? pd, true); } catch (Exception e) { notes = "constructor: " + e.Message; goto report; }
                        float u0 = Time.realtimeSinceStartup + 60f;
                        while (Time.realtimeSinceStartup < u0) { bool ok0 = false; try { ok0 = GameManager.IsConstructor && !GameManager.IsLoadingAny && PlayerController.Instance != null && PlayerController.Instance.Ship != null; } catch { } if (ok0) break; yield return null; }
                        yield return Wait(1.5f);
                        try { prevShip = PlayerController.Instance.Ship; } catch { }
                    }
                    // switch nation and year in place, as the designer's own year field does
                    try { G.ui.ClearPlacingPart(); } catch { }
                    try { G.ui.sharedDesignPlayer = pd; G.ui.sharedDesignYear = yr; GameManager.Instance.RefreshSharedDesign(yr, pd, false); } catch (Exception e) { notes = "refresh: " + e.Message; goto report; }
                }
                {
                    float until = Time.realtimeSinceStartup + 40f; float t0 = Time.realtimeSinceStartup; Ship ship = null;
                    while (Time.realtimeSinceStartup < until)
                    {
                        bool ok = false; try { ok = GameManager.IsConstructor && !GameManager.IsLoadingAny && PlayerController.Instance != null && PlayerController.Instance.Ship != null && (prevShip == null || PlayerController.Instance.Ship.Pointer != prevShip.Pointer || Time.realtimeSinceStartup > t0 + 25f); } catch { }
                        if (ok) break; yield return null;
                    }
                    yield return Wait(1.5f);
                    try { ship = PlayerController.Instance.Ship; } catch { }
                    // the constructor sometimes opens without a ship (a stored design of that year fails to load): ask for a new one again
                    for (int r = 0; r < 2 && ship == null; r++)
                    {
                        bool inC = false; try { inC = GameManager.IsConstructor; } catch { }
                        if (!inC) break;
                        try { GameManager.Instance.RefreshSharedDesign((int)s.N("year"), pd, r == 1); } catch (Exception e) { Log("refresh: " + e.Message); }
                        float ru = Time.realtimeSinceStartup + 40f;
                        while (Time.realtimeSinceStartup < ru) { try { ship = PlayerController.Instance.Ship; } catch { } if (ship != null) break; yield return null; }
                        yield return Wait(1.5f);
                        if (ship != null) Log($"{s["name"]}: ship created on retry {r + 1}");
                    }
                    if (ship == null) { notes = "constructor did not open"; goto report; }
                    // the base may be an existing shared design: give the slip a new identity so saving never replaces it
                    try { ship.id = Il2CppSystem.Guid.NewGuid(); } catch (Exception e) { notes = "new id: " + e.Message; goto report; }
                    bool templ = false;
                    {
                        string cp; float wantIn0 = s.N("main_in"); int wantMain0 = (int)s.N("main_count");
                        if (cands.TryGetValue(s["nation"] + "|" + s["name"], out cp))
                        {
                            Ship n = null; try { n = LoadCandidate(cp, ship); } catch (Exception e) { notes += "template load: " + e.GetType().Name + " " + e.Message + "; "; Log("template load: " + e); }
                            if (n != null)
                            {
                                yield return Wait(1f);
                                var oldSlip = ship; ship = n; try { ship.id = Il2CppSystem.Guid.NewGuid(); } catch { }
                                float c0; int b0; MainBattery(ship, out c0, out b0);
                                bool v0 = false; try { v0 = ship.IsValid(false); } catch { }
                                Log($"{s["name"]}: template {Path.GetFileName(cp)} - main battery {b0} x {c0:0.#} in, valid {v0}{(v0 ? "" : " - " + Why(ship))}");
                                // 1.11.108: repair the template instead of rejecting it - parts off their mount go back to the nearest
                                // free mount, parts that cannot be placed are removed (main guns and the main tower are kept),
                                // duplicate funnels over the limit are removed; weight is fitted later (range, armour, speed)
                                if (!v0)
                                {
                                    var fixLog = new List<string>(); int fixes = 0;
                                    int secB = 0, secR = 0;
                                    try { fixes = RepairTemplate(ship, wantIn0, fixLog, out secB, out secR); } catch (Exception e) { fixLog.Add("repair: " + e.GetType().Name); }
                                    yield return Wait(0.5f);
                                    MainBattery(ship, out c0, out b0);
                                    try { v0 = ship.IsValid(false); } catch { }
                                    bool wOnly = !v0 && OnlyWeight(ship);
                                    Log($"{s["name"]}: template repair - {fixes} change(s){(fixLog.Count > 0 ? ": " + string.Join("; ", fixLog.Take(12)) : "")}; main battery {b0} x {c0:0.#} in, valid {v0}{(v0 ? "" : (wOnly ? " (weight only - fitted later)" : " - " + Why(ship)))}");
                                    if (fixes > 0) notes += $"template repaired ({fixes} change(s)); ";
                                    if (wOnly) v0 = true;
                                    // a repair that strips more than a quarter of the secondary guns no longer gives the historical ship
                                    if (v0 && secB > 0 && secR * 4 > secB) { v0 = false; notes += $"template repair removed {secR} of {secB} secondary barrels - rejected; "; Log($"{s["name"]}: template repair removed {secR} of {secB} secondary barrels - template rejected"); }
                                }
                                if (v0 && Mathf.Abs(c0 - wantIn0) <= 0.6f && Math.Abs(b0 - wantMain0) <= Math.Max(1, wantMain0 / 4)) { templ = true; mainBuilt = $"{b0} x {c0:0.#} in"; fromTemplate++; try { VesselEntity.TryToEraseVessel(oldSlip); } catch { } }
                                else
                                {
                                    notes += v0 ? $"template battery {b0} x {c0:0.#} in rejected; " : "template invalid, generator used; ";
                                    // back to the designer's own slip (it carries the nation's technologies for the generator)
                                    try { PlayerController.Instance.Ship = oldSlip; oldSlip.EnterConstructor(); G.ui.RefreshLevel(); G.ui.Refresh(false); } catch { }
                                    try { VesselEntity.TryToEraseVessel(n); } catch { }
                                    ship = oldSlip;
                                    yield return Wait(1f);
                                }
                            }
                        }
                    }
                    // 2. hull; 1.11.108: when the class fails on its own hull, up to HistBuilderAltHulls other hulls of the same ship
                    // type that the nation has in that year and that cover the historical displacement are tried with the generator
                    PartData hull0 = null; ShipType st = null;
                    try { G.GameData.parts.TryGetValue(s["hull"], out hull0); G.GameData.shipTypes.TryGetValue(s["type"], out st); } catch { }
                    if (hull0 == null || st == null) { notes = "hull or ship type not found"; goto report; }
                    float wantT = s.N("tonnage"), wantKn = s.N("speed_kn"), wantIn = s.N("main_in"); int wantMain = (int)s.N("main_count");
                    var hulls = new List<PartData> { hull0 }; bool altsAdded = false; string notes0 = notes, lastFail = "";
                    for (int hv = 0; hv < hulls.Count; hv++)
                    {
                        PartData hull = hulls[hv]; bool useTempl = templ && hv == 0;
                        if (hv > 0)
                        {
                            notes = notes0 + $"alternative hull {hull.name} ({hulls[0].name}: {lastFail}); ";
                            weight = 0f; tonnage = 0f; beltBuilt = 0f; scale = 1f; mainBuilt = ""; result = "failed";
                            Log($"{s["name"]}: alternative hull {hv}/{hulls.Count - 1}: {hull.name} ({hull.nameUi}, {hull.tonnageMin:0}-{hull.tonnageMax:0} t)");
                        }
                        if (!useTempl) { try { ship.ChangeHull(hull, null, st, true); } catch (Exception e) { notes += "ChangeHull: " + e.Message + "; "; lastFail = "hull change failed"; goto nextHull; } }
                        yield return Wait(0.5f);
                        // 3. generator tries: match main calibre and gun count
                        int bestDiff = useTempl ? 0 : 999; int tries = useTempl ? 0 : Math.Max(1, Tries.Value); bool bestValid = false;
                        if (useTempl) { try { ship.SetTonnage(wantT); } catch { } }
                        CapMainMm = wantIn * 25.4f + 6f; CapSecMm = s.N("sec_mm") > 0f ? s.N("sec_mm") + 6f : (s.N("sec_in") > 0f ? s.N("sec_in") * 25.4f + 6f : 0f); MainPerTurret = (int)s.N("main_per_turret"); if (MainPerTurret > 0 && wantMain % MainPerTurret != 0) MainPerTurret = 0; /* mixed layouts such as 2x4 + 1x2 */ WantMain = wantMain; EraYear = (int)s.N("year");
                        // cruisers with an unknown mount layout: the generator mirrors twin mounts (2 wanted -> 2 twins = 4 guns), so an odd
                        // number of main guns or an early cruiser (single mounts were the rule before about 1910) gets single mounts on every
                        // other try (the remaining tries stay free, in case the hull offers no single mount of that calibre)
                        int mptBase = MainPerTurret;
                        bool singles = mptBase == 0 && wantMain > 0 && (s["type"] == "ca" || s["type"] == "cl") && (wantMain % 2 == 1 || s.N("year") < 1910f);
                        for (int t = 0; t < tries; t++)
                        {
                            MainPerTurret = singles && t % 2 == 0 ? 1 : mptBase;
                            try { ship.SetTonnage(wantT); } catch { }
                            bool fin = false;
                            try
                            {
                                var cb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool, int, float>>(new Action<bool, int, float>((a, b, c) => { fin = true; }));
                                // from the second try on, the generator may widen and deepen the hull (more room for turrets and barbettes)
                                bool roomy = t >= 1;
                                var en = ship.GenerateRandomShip(cb, true, new Il2CppSystem.Nullable<bool>(), false, roomy, roomy, false, false,
                                    new Il2CppSystem.Nullable<float>(), new Il2CppSystem.Nullable<float>(wantKn), new Il2CppSystem.Nullable<VesselEntity.OpRange>(), new Il2CppSystem.Nullable<Ship.Survivability>(),
                                    new Il2CppSystem.Nullable<float>(), new Il2CppSystem.Nullable<float>(wantIn), new Il2CppSystem.Nullable<float>(), new Il2CppSystem.Nullable<float>(),
                                    true, false, (t % 2 == 0), false, new Il2CppSystem.Text.StringBuilder());
                                ship.StartCoroutine(en);
                            }
                            catch (Exception e) { notes += "generator: " + e.Message + "; "; break; }
                            float gUntil = Time.realtimeSinceStartup + 50f;
                            while (!fin && Time.realtimeSinceStartup < gUntil) yield return null;
                            yield return Wait(0.3f);
                            float cal; int barrels; MainBattery(ship, out cal, out barrels);
                            bool tv = false; try { tv = ship.IsValid(false); } catch { }
                            int diff = Math.Abs(barrels - wantMain) + (Mathf.Abs(cal - wantIn) > 0.6f ? 10 : 0) + (tv ? 0 : 3);
                            mainBuilt = $"{barrels} x {cal:0.#} in";
                            Log($"{s["name"]} ({s["nation"]} {s["year"]}{(hv > 0 ? ", " + hull.name : "")}): try {t + 1}: main battery {mainBuilt}, wanted {wantMain} x {wantIn} in, speedMax {ShipSpeed(ship):0.0}, valid {tv}{(tv ? "" : " - " + Why(ship))}");
                            if (diff < bestDiff) { bestDiff = diff; bestValid = tv; }
                            if (diff == 0) break;
                            // the slip keeps only the last try: stop at the first valid try that is close enough to the historical battery,
                            // otherwise a later, worse try would replace it (e.g. 11 of 13 guns valid, then 10 of 13 invalid)
                            if (tv && Mathf.Abs(cal - wantIn) <= 0.6f && Math.Abs(barrels - wantMain) <= Math.Max(1, wantMain / 4)) { Log($"{s["name"]}: try {t + 1} kept (valid, close to the historical battery)"); break; }
                            // 1.11.109: a valid try with the historical calibre and surplus main guns is kept too - the surplus mounts are removed below
                            if (tv && Mathf.Abs(cal - wantIn) <= 0.6f && barrels > wantMain && wantMain > 0) { Log($"{s["name"]}: try {t + 1} kept (valid, historical calibre, surplus mounts to remove)"); break; }
                        }
                        CapMainMm = 0f; CapSecMm = 0f; MainPerTurret = 0; WantMain = 0; EraYear = 0;
                        // 1.11.109: surplus main mounts of the historical calibre (e.g. 10 of 8 or 12 of 9 guns) are removed before the battery check
                        { var tl = new List<string>(); int tr = 0; try { tr = TrimMainBattery(ship, wantIn, wantMain, tl); } catch (Exception e) { Log("trim: " + e.Message); } if (tr > 0) { notes += $"{tr} surplus main mount(s) removed; "; Log($"{s["name"]}: main battery trimmed - {string.Join("; ", tl)}"); yield return Wait(0.3f); } }
                        // 1.11.112: the generator did not give the historical main battery - the builder places the main turrets itself on the
                        // hull's mounts (guns of the historical calibre and barrels per turret, centre line first, ends first), the rest of the
                        // generated ship (superstructure, secondaries) stays
                        {
                            float c1; int b1; MainBattery(ship, out c1, out b1);
                            if (Mathf.Abs(c1 - wantIn) > 0.6f || Math.Abs(b1 - wantMain) > Math.Max(1, wantMain / 4))
                            {
                                var ml = new List<string>(); int placed = 0;
                                try { placed = PlaceMainTurrets(ship, wantIn, wantMain, (int)s.N("main_per_turret"), (int)s.N("year"), ml); } catch (Exception e) { ml.Add("error " + e.GetType().Name + ": " + e.Message); }
                                yield return Wait(0.5f);
                                // 1.11.114: removed guns leave the scene only at the end of the frame - a second pass after a pause no longer meets them
                                if (placed == 0 && ml.Any(x => x.Contains(": part")))
                                {
                                    var ml2 = new List<string>();
                                    try { placed = PlaceMainTurrets(ship, wantIn, wantMain, (int)s.N("main_per_turret"), (int)s.N("year"), ml2); } catch (Exception e) { ml2.Add("error " + e.GetType().Name + ": " + e.Message); }
                                    yield return Wait(0.5f);
                                    ml.Clear(); ml.Add("second pass"); ml.AddRange(ml2);
                                }
                                MainBattery(ship, out c1, out b1);
                                bool v1 = false; try { v1 = ship.IsValid(false); } catch { }
                                Log($"{s["name"]}: builder placed {placed} main turret(s){(ml.Count > 0 ? " - " + string.Join("; ", ml.Take(14)) : "")}; main battery now {b1} x {c1:0.#} in, valid {v1}{(v1 ? "" : " - " + Why(ship))}");
                                if (placed > 0) { notes += $"main turrets placed by the builder ({placed}); "; mainBuilt = $"{b1} x {c1:0.#} in"; bestDiff = Math.Abs(b1 - wantMain); }
                            }
                        }
                        // the last try is what stays on the slip; if it is not the historical battery, do not save a wrong design
                        { float c2; int b2; MainBattery(ship, out c2, out b2); if (Mathf.Abs(c2 - wantIn) > 0.6f || Math.Abs(b2 - wantMain) > Math.Max(1, wantMain / 4)) { notes += $"main battery {b2} x {c2:0.#} in, not saved; "; result = "failed"; lastFail = $"main battery {b2} x {c2:0.#} in"; goto nextHull; } }
                        if (bestDiff != 0) notes += $"main battery {mainBuilt} instead of {wantMain} x {wantIn} in; ";
                        // 4. speed and armour from the specification
                        try { ship.SetSpeedMax(wantKn / 1.94384f); } catch (Exception e) { notes += "speed: " + e.Message + "; "; }
                        var arm = new Dictionary<int, float>();
                        void Want(int zone, string key) { float v = s.N(key); if (v > 0f) arm[zone] = v; }
                        Want(2, "belt"); Want(5, "deck"); Want(8, "ct"); Want(11, "turret"); Want(12, "barbette");
                        if (arm.ContainsKey(2)) { arm[3] = arm[2] * 0.5f; arm[4] = arm[2] * 0.5f; }
                        if (arm.ContainsKey(5)) { arm[6] = arm[5] * 0.7f; arm[7] = arm[5] * 0.7f; }
                        ApplyArmour(ship, arm, 1f);
                        // 5. fit weight (1.11.108): range down to "low" first, then armour down to half, then speed by at most 2 kn,
                        // then armour further (down to about 16%, as before); main armament is never touched
                        try { weight = ship.Weight(true, true); tonnage = ship.Tonnage(); } catch { }
                        if (weight > tonnage) { try { if ((int)ship.opRange > (int)VesselEntity.OpRange.Low) { ship.SetOpRange(VesselEntity.OpRange.Low, false); notes += "range reduced to low; "; } } catch { } yield return null; }
                        float knCut = 0f;
                        for (int k = 0; k < 40; k++)
                        {
                            try { weight = ship.Weight(true, true); tonnage = ship.Tonnage(); } catch { }
                            if (weight <= tonnage) break;
                            if (arm.Count > 0 && scale > 0.16f && (scale > 0.5f || knCut >= 2f)) { scale *= 0.94f; ApplyArmour(ship, arm, scale); }
                            else if (knCut < 2f && wantKn > 0f) { knCut += 0.5f; try { ship.SetSpeedMax((wantKn - knCut) / 1.94384f); } catch { } }
                            else break;
                            yield return null;
                        }
                        try { weight = ship.Weight(true, true); tonnage = ship.Tonnage(); beltBuilt = ArmourOf(ship, 2); } catch { }
                        bool valid = false; try { valid = ship.IsValid(false); } catch { }
                        if (scale < 0.999f) notes += $"armour thinned to {scale * 100f:0}%; ";
                        if (knCut > 0f) notes += $"speed reduced by {knCut:0.#} kn; ";
                        if (weight > tonnage) notes += "still overweight; ";
                        if (!valid) notes += "game reports design invalid: " + Why(ship) + "; ";
                        // parts from later years (e.g. from a prepared template) make the class unhistorical: do not save it
                        int late = 0; string lateList = "";
                        try
                        {
                            var lps = ship.parts; int dy = (int)s.N("year");
                            for (int i = 0; lps != null && i < lps.Count; i++)
                            {
                                var d = lps[i] != null ? lps[i].data : null; int py;
                                if (d != null && PartEra.TryGetValue(d.name, out py) && py > dy) { late++; if (lateList.Length < 160) lateList += d.name + " (" + py + ") "; }
                            }
                        }
                        catch { }
                        if (late > 0) notes += $"{late} part(s) from later years: {lateList.Trim()}; ";
                        // 6. save as shared design
                        string name = DesignName(s);
                        try { ship.vesselName = name; } catch { }
                        // only designs the game accepts are saved - an invalid design would be useless to the player and the AI
                        if (valid && weight <= tonnage && late == 0) { try { G.ui.SaveSharedDesign(name); result = "saved"; } catch (Exception e) { notes += "save: " + e.Message; } }
                        else { result = "failed"; notes += "not saved; "; lastFail = weight > tonnage ? "overweight" : (late > 0 ? "parts from later years" : "invalid design"); }
                        if (result == "saved") { try { File.AppendAllText(idsFile, ship.id.ToString() + "," + s["nation"] + "," + name + "\n"); } catch { } }
                        yield return Wait(0.5f);
                    nextHull:
                        if (result == "saved") break;
                        if (!altsAdded && AltHulls != null && AltHulls.Value > 0)
                        {
                            altsAdded = true;
                            List<PartData> alts = null; try { alts = AltHullsFor(ship, hull0, st, wantT, s["nation"], (int)s.N("year"), AltHulls.Value); } catch (Exception e) { Log("alternative hulls: " + e.Message); }
                            if (alts != null && alts.Count > 0) { hulls.AddRange(alts); Log($"{s["name"]}: {hull0.name} failed ({lastFail}) - alternative hulls: {string.Join(", ", alts.Select(a => a.name))}"); }
                        }
                    }
                    yield return null;
                }
            report:
                if (result.StartsWith("saved")) done++; else failed++;
                try { File.AppendAllText(rep, $"{DateTime.Now:HH:mm:ss},{s["nation"]},{s["year"]},\"{s["name"]}\",{s["hull"]},{result},{s["main_count"]} x {s["main_in"]} in,{mainBuilt},{tonnage:0},{weight:0},{s["speed_kn"]},{s["belt"]},{beltBuilt:0},{scale:0.00},\"{notes.Replace("\"", "'")}\"\n"); } catch { }
                Log($"[{done + failed}/{specs.Count}] {s["name"]} ({s["nation"]} {s["year"]}, {s["hull"]}): {result}{(notes.Length > 0 ? " - " + notes : "")}");
                yield return null;
            }
            try { if (GameManager.IsConstructor) GameManager.Instance.ToMainMenu(); } catch { }
            try { QualitySettings.vSyncCount = oldVs; Application.targetFrameRate = oldFps; } catch { }
            Log($"finished: {done} saved ({fromTemplate} from prepared templates), {failed} failed - report {rep}");
            try { Run.Value = false; MelonPreferences.Save(); } catch { }
        }

        // ------------------------------------------------------------------ 1.11.108: template repair and alternative hulls
        static bool IsMainGun(Part p, float wantIn)
        {
            try { var d = p.data; if (d == null || !d.isGun) return false; float c = d.caliber > 30f ? d.caliber / 25.4f : d.caliber; return Mathf.Abs(c - wantIn) <= 0.6f; } catch { return false; }
        }
        // 1.11.109: the generator often mounts more main guns of the historical calibre than the class had (Nagato 10 of 8,
        // Iowa 12 of 9, Cressy 4 of 2). Whole main mounts are removed one at a time until the count is historical: the mount whose
        // removal comes closest to the historical count, wing mounts first, then the one nearest amidships. Never below the
        // tolerance of the battery check and never the last main mount.
        static int TrimMainBattery(Ship ship, float wantIn, int wantMain, List<string> log)
        {
            int removed = 0;
            if (wantMain <= 0) return 0;
            int tol = Math.Max(1, wantMain / 4);
            for (int k = 0; k < 16; k++)
            {
                float cal; int barrels; MainBattery(ship, out cal, out barrels);
                if (Mathf.Abs(cal - wantIn) > 0.6f || barrels <= wantMain) break;
                float top = 0f; var arr = ship.parts.ToArray();
                foreach (var p in arr) { var d = p != null ? p.data : null; if (d != null && d.isGun && d.caliber > top) top = d.caliber; }
                Part best = null; float bestScore = float.MaxValue; int bestBar = 0; int mounts = 0;
                foreach (var p in arr) { var d = p != null ? p.data : null; if (d != null && d.isGun && Mathf.Abs(d.caliber - top) < 0.01f) mounts++; }
                if (mounts < 2) break;
                Vector3 sp = Vector3.zero; Quaternion sr = Quaternion.identity;
                try { sp = ship.transform.position; sr = ship.transform.rotation; } catch { }
                foreach (var p in arr)
                {
                    var d = p != null ? p.data : null;
                    if (d == null || !d.isGun || Mathf.Abs(d.caliber - top) >= 0.01f) continue;
                    int b = Math.Max(1, d.barrels); int after = barrels - b;
                    if (after <= 0 || after < wantMain - tol) continue;
                    Vector3 lp = Vector3.zero; try { lp = Quaternion.Inverse(sr) * (p.transform.position - sp); } catch { }
                    float score = Math.Abs(after - wantMain) * 100f - (Mathf.Abs(lp.x) > 1.5f ? 50f : 0f) + Mathf.Abs(lp.z) * 0.1f;
                    if (score < bestScore) { bestScore = score; best = p; bestBar = b; }
                }
                if (best == null) break;
                string nm = best.data.name;
                try { ship.RemovePart(best, true, false); } catch { break; }
                removed++; log.Add($"{nm} ({bestBar} gun(s)) removed, {barrels - bestBar} left");
            }
            return removed;
        }
        // nearest free mount that takes the part (as TweaksAndFixes' design validator does): within 2.5 units on each axis, then 6
        static Mount ClosestMount(Ship ship, Part part)
        {
            Mount best = null; float bd = float.MaxValue;
            try
            {
                var pp = part.transform.position;
                foreach (float lim in new[] { 2.5f, 6f })
                {
                    var ms = ship.mounts;
                    for (int i = 0; ms != null && i < ms.Count; i++)
                    {
                        var m = ms[i]; if (m == null) continue;
                        bool used = false; try { used = ship.mountsUsed.ContainsKey(m); } catch { }
                        if (used) continue;
                        var mp = m.transform.position;
                        if (Mathf.Abs(mp.x - pp.x) > lim || Mathf.Abs(mp.y - pp.y) > lim || Mathf.Abs(mp.z - pp.z) > lim) continue;
                        bool fits = false; try { fits = m.Fits(part, null, null); } catch { }
                        if (!fits) continue;
                        float d2 = (mp - pp).sqrMagnitude; if (d2 < bd) { bd = d2; best = m; }
                    }
                    if (best != null) break;
                }
            }
            catch { }
            return best;
        }
        // parts the game refuses: "floor"/"mount"/"border" (off or outside its mount) -> nearest free mount first; duplicate funnels with "amount" -> one removed per round;
        // other refusals -> part removed, except main guns and towers (the class would lose its identity or its validity);
        // the caller rejects the repair when more than a quarter of the secondary barrels had to go
        // ------------------------------------------------------------------ 1.11.112: main turrets placed by the builder
        static float InchOf(PartData d) { try { return d.caliber > 30f ? d.caliber / 25.4f : d.caliber; } catch { return 0f; } }
        static bool IsCasemate(PartData d) { try { return Ship.IsCasemateGun(d); } catch { return false; } }
        // removes guns of the historical calibre or larger, frees centre-line mounts taken by smaller turrets when needed, then
        // places n turrets of the historical calibre (barrels per turret from the data, else twins for an even count, singles otherwise)
        static int PlaceMainTurrets(Ship ship, float wantIn, int wantMain, int mpt, int year, List<string> log)
        {
            if (wantMain <= 0 || wantIn <= 0f) return 0;
            var options = new List<int>();
            if (mpt > 0 && wantMain % mpt == 0) options.Add(mpt);
            else
            {
                if (wantMain % 2 == 0 && wantMain >= 2) options.Add(2);
                if (wantMain % 3 == 0 && year >= 1910) options.Add(3);
                if (wantMain % 4 == 0 && year >= 1930) options.Add(4);
                options.Add(1);
            }
            // guns that the game offers this nation in this year
            var guns = new List<PartData>();
            foreach (var kv in G.GameData.parts)
            {
                var d = kv.Value; if (d == null) continue;
                try { if (!d.isGun || IsCasemate(d)) continue; } catch { continue; }
                if (Mathf.Abs(InchOf(d) - wantIn) > 0.6f) continue;
                bool av = false; try { av = ship.IsPartAvailable(d); } catch { }
                if (av) guns.Add(d);
            }
            if (guns.Count == 0) { log.Add($"no {wantIn:0.#} in gun available"); return 0; }
            // old main guns of the historical calibre or larger go
            int removed = 0;
            foreach (var p in ship.parts.ToArray())
            {
                var d = p != null ? p.data : null; if (d == null) continue;
                try { if (!d.isGun || IsCasemate(d)) continue; } catch { continue; }
                if (InchOf(d) >= wantIn - 0.6f) { try { ship.RemovePart(p, true, false); removed++; } catch { } }
            }
            if (removed > 0) log.Add($"{removed} old main mount(s) removed");
            foreach (int per in options)
            {
                int n = wantMain / per; if (n <= 0) continue;
                var gd = guns.Where(g => { try { return g.barrels == per; } catch { return false; } }).OrderBy(g => Mathf.Abs(InchOf(g) - wantIn)).FirstOrDefault();
                if (gd == null) { log.Add($"no {per}-gun {wantIn:0.#} in turret"); continue; }
                var free = FreeMounts(ship, gd, per == 1);   // 1.11.113: single mounts may also go on wing (side) positions, as on early cruisers
                if (free.Count < n)
                {
                    // free centre-line mounts that take the turret but hold a smaller gun turret
                    foreach (var m in ship.mounts.ToArray())
                    {
                        if (free.Count >= n) break;
                        if (m == null || free.Contains(m)) continue;
                        bool center = false; try { center = m.center; } catch { }
                        if (!center) continue;
                        Part occ = null; try { occ = m.employedPart; } catch { }
                        if (occ == null || occ.data == null) continue;
                        bool g2 = false; try { g2 = occ.data.isGun && !IsCasemate(occ.data) && InchOf(occ.data) < wantIn - 0.6f; } catch { }
                        if (!g2) continue;
                        bool fits = false; try { fits = m.Fits(gd, null, null); } catch { }
                        if (!fits) continue;
                        try { ship.RemovePart(occ, true, false); free.Add(m); log.Add($"{occ.data.name} moved off a centre-line mount"); } catch { }
                    }
                }
                if (free.Count < n) { log.Add($"{per}-gun turrets: {free.Count} free mount(s) for {n}"); continue; }
                // ends first: alternate the furthest forward and the furthest aft mount
                var byZ = free.OrderBy(m => { try { return m.transform.localPosition.z; } catch { return 0f; } }).ToList();
                var order = new List<Mount>(); int lo = 0, hi = byZ.Count - 1; bool fwd = true;
                while (lo <= hi) { order.Add(fwd ? byZ[hi--] : byZ[lo++]); fwd = !fwd; }
                int placed = 0;
                foreach (var m in order)
                {
                    if (placed >= n) break;
                    Part part = null;
                    try
                    {
                        part = Part.Create(gd, ship, ship.partsCont, new Il2CppSystem.Nullable<int>(), true);
                        if (part == null) continue;
                        ship.AddPart(part);
                        try { part.LoadModel(ship, true); } catch { }
                        // 1.11.113: colliders sized as the game's generator does - without it every new turret collided ("part")
                        try { part.UpdateCollidersSize(ship); } catch { }
                        part.Mount(m, true);
                        // 1.11.114: the refusal names the parts or borders it collides with
                        string why = null; bool ok = true; Il2CppSystem.Collections.Generic.List<Part> ovp = null; Il2CppSystem.Collections.Generic.List<Collider> ovb = null;
                        try { ok = part.CanPlace(out why, out ovp, out ovb); } catch { try { ok = part.CanPlace(out why); } catch { } }
                        if (!ok && why != "available")
                        {
                            string with = "";
                            try
                            {
                                if (ovp != null && ovp.Count > 0) { var nn = new List<string>(); for (int k = 0; k < ovp.Count && k < 3; k++) { var op = ovp[k]; nn.Add(op == null ? "?" : (op.Pointer == part.Pointer ? "self" : (op.data != null ? op.data.name : "?"))); } with = " with " + string.Join("/", nn); }
                                if (ovb != null && ovb.Count > 0) with += " border(" + ovb.Count + ")";
                            }
                            catch { }
                            ship.RemovePart(part, true, false); log.Add($"{gd.name}: {why}{with}"); continue;
                        }
                        placed++;
                    }
                    catch (Exception e) { log.Add($"{gd.name}: {e.GetType().Name}"); try { if (part != null) ship.RemovePart(part, true, false); } catch { } }
                }
                try { ship.CheckCaliberOnShip(ship); } catch { }
                try { ship.Init(); } catch { }
                try { ship.CalcInstability(true); } catch { }
                try { G.ui.Refresh(true); } catch { }
                log.Add($"{placed} x {gd.name}");
                if (placed >= n) return placed;
                // incomplete: take them off and try the next layout
                foreach (var p in ship.parts.ToArray()) { try { if (p != null && p.data != null && p.data.name == gd.name) ship.RemovePart(p, true, false); } catch { } }
            }
            return 0;
        }
        static List<Mount> FreeMounts(Ship ship, PartData gd, bool allowSide = false)
        {
            var l = new List<Mount>();
            try
            {
                var ms = ship.mounts;
                for (int i = 0; ms != null && i < ms.Count; i++)
                {
                    var m = ms[i]; if (m == null) continue;
                    Part occ = null; try { occ = m.employedPart; } catch { }
                    if (occ != null) continue;
                    bool used = false; try { used = ship.mountsUsed.ContainsKey(m); } catch { }
                    if (used) continue;
                    bool side = false, cas = false; try { side = m.side; cas = m.casemate; } catch { }
                    if (cas) continue;
                    bool fits = false; try { fits = m.Fits(gd, null, null); } catch { }
                    if (fits && (allowSide || !side)) l.Add(m);
                }
            }
            catch { }
            return l;
        }

        static int RepairTemplate(Ship ship, float wantIn, List<string> log, out int secBefore, out int secRemoved)
        {
            int fixes = 0; var tried = new HashSet<IntPtr>(); secBefore = 0; secRemoved = 0;
            try { foreach (var p0 in ship.parts.ToArray()) { var d0 = p0 != null ? p0.data : null; if (d0 != null && d0.isGun && !IsMainGun(p0, wantIn)) secBefore += Math.Max(1, d0.barrels); } } catch { }
            for (int round = 0; round < 8; round++)
            {
                bool changed = false;
                var arr = ship.parts.ToArray();
                var funnelCount = new Dictionary<string, int>();
                foreach (var p in arr) { try { if (p != null && p.data != null && p.data.isFunnel) { funnelCount.TryGetValue(p.data.name, out var n); funnelCount[p.data.name] = n + 1; } } catch { } }
                foreach (var p in arr)
                {
                    if (p == null) continue; PartData d = null; try { d = p.data; } catch { }
                    if (d == null) continue;
                    try { if (d.isHull) continue; } catch { }
                    string why = null; bool ok = true;
                    try { ok = p.CanPlace(out why); } catch { continue; }
                    if (ok || why == "available") continue;
                    string nm = d.name;
                    if ((why == "floor" || why == "mount" || why == "border") && !tried.Contains(p.Pointer))
                    {
                        tried.Add(p.Pointer);
                        var m = ClosestMount(ship, p);
                        if (m != null) { try { p.Mount(m, false); log.Add(nm + " remounted"); fixes++; changed = true; continue; } catch { } }
                    }
                    if (IsMainGun(p, wantIn)) { if (!tried.Contains(p.Pointer)) { tried.Add(p.Pointer); log.Add(nm + " " + why + " (main gun kept)"); } continue; }
                    bool tower = false; try { tower = d.isTowerAny; } catch { }
                    if (tower) { if (!tried.Contains(p.Pointer)) { tried.Add(p.Pointer); log.Add(nm + " " + why + " (tower kept)"); } continue; }
                    if (why == "amount")
                    {
                        bool funnel = false; try { funnel = d.isFunnel; } catch { }
                        int fc; if (!funnel || !funnelCount.TryGetValue(nm, out fc) || fc < 2) { if (!tried.Contains(p.Pointer)) { tried.Add(p.Pointer); log.Add(nm + " amount (kept)"); } continue; }
                        funnelCount[nm] = fc - 1;
                    }
                    bool gun = false; int bar = 1; try { gun = d.isGun; bar = Math.Max(1, d.barrels); } catch { }
                    try { ship.RemovePart(p, true, false); log.Add(nm + " removed (" + why + ")"); fixes++; changed = true; if (gun) secRemoved += bar; } catch { }
                    if (why == "amount") break;   // re-check after each funnel removal
                }
                if (!changed) break;
            }
            return fixes;
        }
        // the only fault left is weight - the weight step (range, armour, speed) can fix that
        static bool OnlyWeight(Ship ship)
        {
            try
            {
                if (!ship.IsValidWeightOffset()) return false;
                string r; Il2CppSystem.Collections.Generic.Dictionary<Part, string> d;
                if (!ship.IsValidCostReqParts(out r, out _, out d)) return false;
                string r2; if (ship.IsValidCostWeightBarbette(out r2, out _)) return false;
                return r2 != null && r2.IndexOf("weight", StringComparison.OrdinalIgnoreCase) >= 0 && r2.IndexOf("barbette", StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return false; }
        }
        // other hulls of the same ship type the nation can use in that year, covering the historical displacement,
        // preferring hulls where the class sits in the lower-middle of the hull's tonnage range (room for guns and armour)
        static List<PartData> AltHullsFor(Ship ship, PartData orig, ShipType st, float wantT, string nation, int year, int max)
        {
            var res = new List<KeyValuePair<PartData, float>>();
            PlayerData pd = null; try { G.GameData.players.TryGetValue(nation, out pd); } catch { }
            foreach (var kv in G.GameData.parts)
            {
                var p = kv.Value; if (p == null) continue;
                try { if (!p.isHull || p.name == orig.name) continue; if (p.shipType == null || p.shipType.name != st.name) continue; } catch { continue; }
                try { if (pd != null && p.countriesx != null && p.countriesx.Count > 0 && !p.countriesx.Contains(pd)) continue; } catch { }
                int era; if (PartEra.TryGetValue(p.name, out era) && era > year) continue;
                bool avail = false; try { avail = ship.IsPartAvailable(p); } catch { }
                if (!avail) continue;
                float mn = p.tonnageMin, mx = p.tonnageMax;
                if (wantT > 0f && (wantT < mn || wantT > mx)) continue;
                float score = mx > mn && wantT > 0f ? Mathf.Abs((wantT - mn) / (mx - mn) - 0.4f) : 1f;
                res.Add(new KeyValuePair<PartData, float>(p, score));
            }
            return res.OrderBy(x => x.Value).Take(max).Select(x => x.Key).ToList();
        }

        static string Why(Ship ship)
        {
            var w = new List<string>();
            try { if (!ship.IsValidWeightOffset()) w.Add("weight offset/stability"); } catch { }
            try
            {
                string r; Il2CppSystem.Collections.Generic.Dictionary<Part, string> d;
                if (!ship.IsValidCostReqParts(out r, out _, out d)) { w.Add("parts: " + (r ?? "")); if (d != null) foreach (var kv in d) { try { w.Add((kv.Key != null && kv.Key.data != null ? kv.Key.data.name : "?") + "=" + kv.Value); } catch { } } }
            }
            catch (Exception e) { w.Add("req check: " + e.GetType().Name); }
            try { string r; if (!ship.IsValidCostWeightBarbette(out r, out _)) w.Add("cost/weight/barbette: " + (r ?? "")); } catch { }
            return string.Join(" | ", w).Replace(",", " ").Replace("\n", " ");
        }
        static float ShipSpeed(Ship s) { try { return s.speedMax * 1.94384f; } catch { return 0f; } }
        static void MainBattery(Ship ship, out float cal, out int barrels)
        {
            cal = 0f; barrels = 0;
            try
            {
                var parts = ship.parts; float top = 0f;
                for (int i = 0; i < parts.Count; i++) { var p = parts[i]; var d = p != null ? p.data : null; if (d == null || !d.isGun) continue; float c = d.caliber; if (c > top) top = c; }
                for (int i = 0; i < parts.Count; i++) { var p = parts[i]; var d = p != null ? p.data : null; if (d == null || !d.isGun) continue; if (Mathf.Abs(d.caliber - top) < 0.01f) barrels += Math.Max(1, d.barrels); }
                cal = top > 30f ? top / 25.4f : top;   // PartData.caliber may be mm or inches - log shows which
            }
            catch { }
        }
        static float ArmourOf(Ship s, int zone) { try { float v; return s.armor.TryGetValue((Ship.A)zone, out v) ? v : 0f; } catch { return 0f; } }
        static void ApplyArmour(Ship ship, Dictionary<int, float> arm, float scale)
        {
            foreach (var kv in arm) { try { ship.SetArmor((Ship.A)kv.Key, kv.Value * scale, false); } catch { } }
            try
            {
                float side = arm.ContainsKey(11) ? arm[11] * scale : -1f, barb = arm.ContainsKey(12) ? arm[12] * scale : -1f;
                var ta = ship.shipTurretArmor;
                for (int i = 0; ta != null && i < ta.Count; i++)
                {
                    var t = ta[i]; if (t == null || t.isCasemateGun) continue;
                    if (side > 0f) t.sideTurretArmor = side; if (barb > 0f) t.barbetteArmor = barb;
                }
            }
            catch { }
            try { ship.SetArmor((Ship.A)2, arm.ContainsKey(2) ? arm[2] * scale : ArmourOf(ship, 2), true); } catch { }
        }
    }

    // While the historical builder runs, the generator may not pick guns above the historical main calibre
    // (and secondaries above the historical secondary calibre when the main calibre is already fitted).
    [HarmonyLib.HarmonyPatch(typeof(Ship.__c__DisplayClass590_0), nameof(Ship.__c__DisplayClass590_0._GetParts_b__0))]
    internal static class Patch_HistBuilderGuns
    {
        [HarmonyLib.HarmonyPostfix]
        static void Post(PartData a, ref bool __result)
        {
            if (!__result || a == null) return;
            if (HistDesigner.EraYear > 0)
            {
                try { int py; if (HistDesigner.PartEra.TryGetValue(a.name, out py) && py > HistDesigner.EraYear) { __result = false; return; } } catch { }
            }
            if (HistDesigner.CapMainMm <= 0f) return;
            try
            {
                if (!a.isGun) return;
                float cap = HistDesigner.CapMainMm;
                if (a.caliber > cap) { __result = false; return; }
                bool mainCal = a.caliber > cap - 20f;   // the historical main calibre (cap = main + 6 mm)
                if (mainCal && HistDesigner.MainPerTurret > 0 && Math.Max(1, a.barrels) != HistDesigner.MainPerTurret) { __result = false; return; }
                if (mainCal && HistDesigner.WantMain > 0)
                {
                    // no more main-calibre mounts once the historical number of main guns is on the slip
                    int have = 0; var sh = PlayerController.Instance != null ? PlayerController.Instance.Ship : null;
                    if (sh != null) { var ps = sh.parts; for (int i = 0; i < ps.Count; i++) { var d = ps[i] != null ? ps[i].data : null; if (d != null && d.isGun && d.caliber > cap - 20f) have += Math.Max(1, d.barrels); } }
                    if (have + Math.Max(1, a.barrels) > HistDesigner.WantMain) { __result = false; return; }
                }
                if (!mainCal && HistDesigner.CapSecMm > 0f && a.caliber > HistDesigner.CapSecMm) __result = false;
            }
            catch { }
        }
    }
}
