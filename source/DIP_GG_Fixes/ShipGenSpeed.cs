// 1.11.196 (beta, author's decision 24.10): the AI's ship generator (TweaksAndFixes shipgen) made faster and its results kept.
// 24.10 log: Japan, hull bb_1_japan (Dreadnought I, 1921): 4 attempts, 189 s, failure - every attempt ended the same way,
// 2 main turrets of the 4 the hull requires, 4 barrels of 6; two destroyer designs took 20 s each.
// 1. Early stop: when an attempt fails because the hull cannot take the required main battery (both the turret and the barrel
//    minimum of the hull missed), the remaining attempts are dropped - they end the same way (the failure memory, 1.11.99,
//    already remembers the nation/hull pair for later turns).
// 2. Newest hulls: while the AI designs a ship, only hulls whose unlocking technology is among the newest the nation has for
//    that ship type (within ShipGenHullWindow years of technology data) count as unlocked - no 1905 dreadnought hull for a
//    nation with 1920 hull technology. Technology years come from technologies.csv (unlock(...) lists), never the calendar.
// 3. Kept designs: a design the generator finished successfully is saved as a shared design of its nation (file tag [gen]),
//    at most ShipGenShareMax per nation and ship type - on later turns the AI finds a fitting shared design and skips the
//    generator altogether (TAF: shared designs first, then the generator).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ShipGenSpeed
    {
        internal static MelonPreferences_Entry<bool> EarlyStop, NewHulls, ShareAi;
        internal static MelonPreferences_Entry<int> HullWindow, ShareMax;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ShipGen] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            EarlyStop = cat.CreateEntry("ShipGenEarlyStop", true, null, "Campaign: the AI ship generator drops its remaining attempts when an attempt fails because the hull cannot take the required main battery (shorter end of turn)");
            NewHulls = cat.CreateEntry("ShipGenNewestHulls", true, null, "Campaign: the AI designs new ships only on hulls whose unlocking technology is among the newest it has for that ship type (no obsolete hulls, fewer failed attempts)");
            HullWindow = cat.CreateEntry("ShipGenHullWindow", 10, null, "Newest hulls: a hull counts as current when its unlocking technology is at most this many years (technology data) older than the newest hull technology the nation has for that ship type");
            ShareAi = cat.CreateEntry("ShipGenShareDesigns", true, null, "Campaign: a design the AI generator finished successfully is saved as a shared design of its nation ([gen] files) - on later turns the AI reuses it instead of running the generator");
            ShareMax = cat.CreateEntry("ShipGenShareMax", 8, null, "Kept generator designs: at most this many per nation and ship type");
            RelaxTurrets = cat.CreateEntry("ShipGenRelaxTurrets", true, null, "Campaign: when the AI generator cannot fit the main battery a hull requires, the next attempt may carry one main turret fewer instead of giving the hull up (modern hulls stay in use)"); // 1.11.223
            TolOn = cat.CreateEntry("ShipGenAsymmetryTolerance", true, null, "Ship generator: a small side or fore-aft weight asymmetry (ballast and trim took care of it on real ships) does not throw an attempt away; only while the generator runs - the designer keeps the game's strictness"); // 1.11.244
            TolValue = cat.CreateEntry("GeneratorAsymmetryTolerance", 0.002f, null, "Weight asymmetry tolerated by the ship generator as a share of displacement (0.002 = 0.2 %, about 24 t on a 12 000 t cruiser)");
            InstMax = cat.CreateEntry("GeneratorInstabilityMax", 2f, null, "Ship generator: an attempt whose side instability stat (instability_x) is at most this value is accepted - 100 is the full handling/accuracy penalty, so 2 means under 1 %; 0 = the game's rule (any value above zero fails the attempt)"); // 1.11.245
            TrimOn = cat.CreateEntry("ShipGenTrimOverweight", true, null, "Ship generator: an attempt that comes out overweight has guns below the main calibre removed, smallest first and in pairs, until the ship makes its tonnage - instead of the whole attempt being thrown away (the 25.10 tests: 26 of 27 failed attempts were overweight)"); // 1.11.246
            SecTowerOn = cat.CreateEntry("ShipGenSecondaryTower", false, null, "Ship generator (off until #91 is solved - author 30.10 09:10): when an attempt comes out with a main tower and no secondary tower, the later attempts of that design get an available secondary tower on a free mount before the check (1938 battleships of Italy and Germany failed all 20 attempts without one)");
            TrimMax = cat.CreateEntry("GeneratorOverweightMax", 0.12f, null, "Trimming: attempts overweight by more than this share of the tonnage are left to the game (0.12 = 12 %)");
            TrimState = cat.CreateEntry("ShipGenTrimState", 20, null, "Author's tool: coroutine state of the generator at which trimming runs - 20 is the state that checks the attempt, after the game's own weight reductions (state log of 25.10: 8 parts, 12 armour, 13 and 15 weight reduction, 20 check)");
            TurnBudget = cat.CreateEntry("ShipGenTurnBudget", 20f, null, "Campaign: seconds of AI ship generator work per turn - once used up, a design that fails an attempt gets at most one more instead of all its attempts (author 30.10: a turn of 2.5 min, 2 of them one Scandinavian battlecruiser failing 4 attempts of 30 s); 0 = no limit"); // 1.11.478
            HardStop = cat.CreateEntry("ShipGenTurnHardStop", 45f, null, "Campaign: seconds after the AI's first ship design of a turn after which the AI nations that have not ordered ships yet skip shipbuilding for that month (they build next month) - keeps the end of a turn near a minute; 0 = no limit"); // 1.11.481
            Diag = cat.CreateEntry("ShipGenDiagnosis", false, null, "Author's tool: outside the campaign, every failed generator attempt is logged with its guns and tonnage, and when no main tower, secondary tower or funnel was placed, each tower/funnel part is checked with the game's own availability and placement tests"); // 1.11.245
        }

        // ---- 0. weight-asymmetry tolerance for the generator (1.11.244, author's test 25.10) ----
        // Custom-battle test, Scandinavia 1930, heavy cruiser on ca_5_generic_early: 9 of 20 attempts thrown away for "instability_x
        // 0.1-0.7 > 0" - a side weight offset of a few tonnes on a 12 000 t ship (stability_x_threshold 0.000012 of the displacement).
        // Real ships carried ballast and asymmetric loads of that order without notice. While the generator coroutine runs the
        // thresholds are raised to GeneratorAsymmetryTolerance (share of displacement) and put back at once, so the player's designer
        // keeps the game's strictness. Switch: ShipGenAsymmetryTolerance.
        internal static MelonPreferences_Entry<bool> TolOn; internal static MelonPreferences_Entry<float> TolValue;
        static float tolOx = -1f, tolOz = -1f; static bool tolSet; static int tolLogged;
        internal static void TolBegin()
        {
            try
            {
                if (TolOn == null || !TolOn.Value || tolSet) return;
                bool ctor = false; try { ctor = GameManager.IsConstructor; } catch { }
                if (ctor) return;
                var raw = G.GameData.paramsRaw; if (raw == null) return;
                float tol = Mathf.Clamp(TolValue != null ? TolValue.Value : 0.002f, 0f, 0.05f);
                ParamData px = raw.ContainsKey("stability_x_threshold") ? raw["stability_x_threshold"] : null, pz = raw.ContainsKey("stability_z_threshold") ? raw["stability_z_threshold"] : null;
                if (px == null || pz == null) return;
                tolOx = px.value; tolOz = pz.value;
                if (tol > tolOx) px.value = tol; if (tol > tolOz) pz.value = tol;
                tolSet = true;
                if (tolLogged++ < 1) Log($"generator weight-asymmetry tolerance: stability thresholds {tolOx:0.######} / {tolOz:0.######} -> {px.value:0.####} / {pz.value:0.####} of the displacement while the generator runs");
            }
            catch (Exception e) { if (tolLogged++ < 3) Log("tolerance: " + e.Message); }
        }
        internal static void TolEnd()
        {
            if (!tolSet) return; tolSet = false;
            try { var raw = G.GameData.paramsRaw; if (raw != null) { if (tolOx >= 0f && raw.ContainsKey("stability_x_threshold")) raw["stability_x_threshold"].value = tolOx; if (tolOz >= 0f && raw.ContainsKey("stability_z_threshold")) raw["stability_z_threshold"].value = tolOz; } } catch { }
        }

        // ---- 0b. generator stability tolerance (1.11.245, author's test 25.10) ----
        // Test Spain vs China 1922 with beta.54: the raised stability thresholds changed nothing - the generator's attempts still
        // ended with "instability_x: 0.3-1.8 > 0" (10 of 36 failed attempts). The game's rule is "any instability_x above zero is
        // invalid"; the stat itself scales the accuracy/handling penalties (100 = the full penalty of -40 % accuracy), so an attempt
        // at 2 carries under 1 % of penalty. While the generator runs, Ship.IsValidWeightOffset accepts a side instability of at
        // most GeneratorInstabilityMax (fore-aft keeps the game's 100 limit). The designer is untouched.
        internal static MelonPreferences_Entry<float> InstMax;
        internal static bool GenRunning;
        static int instLogged;
        internal static bool AcceptOffset(Ship ship)
        {
            try
            {
                if (!GenRunning || ship == null) return false;
                float max = InstMax != null ? InstMax.Value : 2f; if (max <= 0f) return false;
                var stats = G.GameData.stats; if (stats == null) return false;
                float ix = 0f, iz = 0f;
                Ship.StatValue sv;
                if (ship.stats_.TryGetValue(stats["instability_x"], out sv) && sv != null) ix = sv.total;
                if (ship.stats_.TryGetValue(stats["instability_z"], out sv) && sv != null) iz = sv.total;
                if (ix <= max && iz <= 100f)
                {
                    if (instLogged++ < 20) Log($"attempt accepted with side instability {ix:0.##} (limit {max:0.##}; fore-aft {iz:0.##})");
                    return true;
                }
            }
            catch (Exception e) { if (instLogged++ < 20) Log("instability: " + e.Message); }
            return false;
        }

        // ---- 0c. diagnosis of failed attempts outside the campaign (1.11.245) ----
        // Custom-battle tests: the campaign-only early-stop log is replaced by a summary per failed attempt, and when the attempt
        // has no main tower / secondary tower / funnel the tower and funnel parts are checked one by one with the game's own
        // tests (Ship.IsPartAvailable - nation, technology, hull tags; Part.CanPlaceGeneric - the hull takes the part), so the log
        // says why nothing was placed. Switch: ShipGenDiagnosis (author's tool, off by default).
        internal static MelonPreferences_Entry<bool> Diag;
        static readonly Dictionary<IntPtr, int> diagTry = new Dictionary<IntPtr, int>();
        internal static void Diagnose(Ship._GenerateRandomShip_d__573 it, Ship ship, bool running)
        {
            if (Diag == null || !Diag.Value || it == null) return;
            var p = it.Pointer;
            if (!running) { diagTry.Remove(p); return; }
            bool campaign = false; try { campaign = GameManager.IsCampaign; } catch { }
            if (campaign) return;                                   // 1.11.247: the campaign has the early-stop log already
            if (ship == null) return;
            int tryN = it._tryN_5__5, total = it._triesTotal_5__4;
            int prev; if (!diagTry.TryGetValue(p, out prev)) prev = 0;
            if (tryN <= prev) return;
            diagTry[p] = tryN;
            try
            {
                int tm = 0, ts = 0, fn = 0;
                var parts = ship.parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    var d = parts[i] != null ? parts[i].data : null; if (d == null) continue;
                    string t = d.type ?? "";
                    if (t == "tower_main") tm++; else if (t == "tower_sec") ts++; else if (d.isFunnel || t == "funnel") fn++;
                }
                string key = ""; try { key = ship.player.data.name + ": hull " + ship.hull.data.name; } catch { }
                Log($"{key} - attempt {tryN} of {total} failed: {Summary(ship)}; placed: main towers {tm}, secondary towers {ts}, funnels {fn}");
                if (tm == 0) Log(Avail(ship, "tower_main"));
                if (ts == 0) Log(Avail(ship, "tower_sec"));
                if (fn == 0) Log(Avail(ship, "funnel"));
            }
            catch (Exception e) { Log("diagnosis: " + e.Message); }
        }
        static string Avail(Ship ship, string type)
        {
            int all = 0, avail = 0, placeable = 0;
            var reasons = new Dictionary<string, int>(); var names = new List<string>();
            foreach (var kv in G.GameData.parts)
            {
                var pd = kv.Value; if (pd == null) continue;
                if ((pd.type ?? "") != type) continue;
                all++;
                bool av = false; try { av = ship.IsPartAvailable(pd); } catch { }
                if (!av) continue;
                avail++;
                string why = null; bool ok = false;
                try { ok = Part.CanPlaceGeneric(pd, ship, false, out why); } catch (Exception e) { why = "exception: " + e.Message; }
                if (ok) { placeable++; if (names.Count < 6) names.Add(pd.name); }
                else { why = why ?? "(no reason)"; int c; reasons.TryGetValue(why, out c); reasons[why] = c + 1; }
            }
            var sb = new System.Text.StringBuilder();
            sb.Append($"  {type}: {all} parts in the game, {avail} available to this ship (nation, technology, hull tags), {placeable} the hull takes");
            if (names.Count > 0) sb.Append(" [" + string.Join(", ", names) + "]");
            foreach (var r in reasons.OrderByDescending(x => x.Value).Take(5)) sb.Append($"; refused {r.Value}x: {r.Key}");
            return sb.ToString();
        }

        // ---- 0d. overweight attempts trimmed instead of thrown away (1.11.246, author's tests 25.10) ----
        // Tests Spain vs China 1922 (beta.55, with and without historical weights): 16 of 16 attempts on bb_5_china and 10 of 11 on
        // bb_3 ended "Ship Overweight" by 1-13 % - the generator placed 16 casemates, 42 twin 3" mounts and the like, and the
        // weight reduction could not make it up. A real design office cuts the light battery to make weight. Before the game checks
        // the attempt (coroutine state 20 - the state log of 25.10: state 8 places the parts, 12 the armour, 13 and 15 reduce weight,
        // 20 checks; ShipGenTrimState), guns that are not of the main calibre are removed,
        // smallest calibre first, pairs together (mirror mounts), until the ship makes its tonnage; attempts over
        // GeneratorOverweightMax (share of tonnage) are left to the game. Switch: ShipGenTrimOverweight.
        internal static MelonPreferences_Entry<bool> TrimOn; internal static MelonPreferences_Entry<float> TrimMax; internal static MelonPreferences_Entry<int> TrimState;
        static readonly Dictionary<IntPtr, int> lastState = new Dictionary<IntPtr, int>();
        static int trimLogged, stateLogged;
        internal static void StateStep(Ship._GenerateRandomShip_d__573 it)
        {
            if (it == null) return;
            int st = -99; try { st = it.__1__state; } catch { return; }
            var p = it.Pointer;
            int prev; if (!lastState.TryGetValue(p, out prev)) prev = -100;
            if (st == prev) return;
            lastState[p] = st;
            Ship ship = null; try { ship = it.__4__this; } catch { }
            if (ship == null) return;
            bool inCampaign = false; try { inCampaign = GameManager.IsCampaign; } catch { }
            if (Diag != null && Diag.Value && !inCampaign && stateLogged < 120) // 1.11.294 (#27): the state log too only outside the campaign
            {
                float w = 0f, t = 0f; int n = 0; try { w = ship.Weight(false, false); t = ship.Tonnage(); n = ship.parts.Count; } catch { }
                stateLogged++; Log($"state {prev} -> {st}: attempt {it._tryN_5__5}, {n} parts, {w:0} t of {t:0} t");
            }
            if (st == 8) { try { DynamicCaps.Begin(ship, p); } catch { } }   // 1.11.248: limits for the displacement chosen
            if (st == 13) { try { TorpedoTubesMin.Apply(ship); } catch (Exception e) { if (trimLogged++ < 10) Log("torpedo tubes: " + e.Message); } } // 1.11.309 (#47)
            int want = TrimState != null ? TrimState.Value : 20;
            if (st == 13 || st == want) { try { SecTower(p, ship, st == 13); } catch (Exception e) { if (secLogged++ < 10) Log("secondary tower: " + e.Message); } } // 1.11.412, 1.11.413
            if (TrimOn != null && TrimOn.Value)
            {
                // 1.11.247: first pass as the armour has just been generated and before the game cuts it (state 13) - only the light
                //           battery beyond what the period carried (tertiary mounts over 8, secondary mounts over 12) goes, so the
                //           weight reduction starts from a sane battery and keeps more armour; the last pass at the check (state 20)
                if (st == 13) { try { Trim(ship, true); } catch (Exception e) { if (trimLogged++ < 10) Log("trim: " + e.Message); } }
                if (st == want) { try { Trim(ship, false); } catch (Exception e) { if (trimLogged++ < 10) Log("trim: " + e.Message); } }
            }
        }
        internal static void StateEnd(IntPtr p, Ship ship) { lastState.Remove(p); secMissing.Remove(p); try { DynamicCaps.End(ship, p); } catch { } }

        // ---- 0e. secondary tower put in when the generator leaves it out (1.11.412) ----
        // Tests 30.10 (custom battles 1938): every attempt on bb_6_littorio / bb_7_littorio (Italy) and bb_5_bisko_compact (Germany)
        // ended "Unmet Requirements: tower_sec : 0 (1 ~ 1)" - 12 secondary towers were available and the hull took all of them
        // (ShipGenDiagnosis), yet the random placement never put one in, and after 20 attempts per design the battle had not
        // started in 25 minutes. When an attempt of a generation ends with a main tower and no secondary tower, every later attempt
        // of that generation gets one before the check: a free mount that takes an available secondary tower, aft of the main
        // tower first, the room checked with Part.CanPlace. Switch: ShipGenSecondaryTower.
        internal static MelonPreferences_Entry<bool> SecTowerOn;
        static readonly HashSet<IntPtr> secMissing = new HashSet<IntPtr>(); static int secLogged;
        // 1.11.413 (test 30.10 04:12-04:29, Germany bb_5_bisko_compact): at the check the free mounts were all taken ("no free mount
        // takes one" 13 of 16 attempts) and a tower put on a side mount made the attempt fail on instability_x 100. The tower now
        // goes in as the parts are placed (state 13, before the game's weight reduction pays for it), only on centre-line mounts,
        // and when none is free a lighter gun (not of the main calibre) on a centre-line mount that takes the tower gives way.
        static void SecTower(IntPtr p, Ship ship, bool place)
        {
            if (SecTowerOn == null || !SecTowerOn.Value || ship == null) return;
            int tm = 0, ts = 0; float tmz = 0f; float tmy = 0f;
            var parts = ship.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var d = parts[i] != null ? parts[i].data : null; if (d == null) continue;
                string t = d.type ?? "";
                if (t == "tower_main") { tm++; try { tmz = parts[i].transform.localPosition.z; tmy = parts[i].transform.localPosition.y; } catch { } }
                else if (t == "tower_sec") ts++;
            }
            if (tm == 0 || ts > 0) return;
            if (!place) { secMissing.Add(p); return; }      // at the check: this design's attempts come out without one
            if (!secMissing.Contains(p)) return;             // the first attempt shows whether the hull needs one
            string key = ""; try { key = ship.player.data.name + ": hull " + ship.hull.data.name; } catch { }
            // available secondary towers the hull takes, real secondary towers first, lightest first
            var cand = new List<PartData>();
            foreach (var kv in G.GameData.parts)
            {
                var pd = kv.Value; if (pd == null || (pd.type ?? "") != "tower_sec") continue;
                bool av = false; try { av = ship.IsPartAvailable(pd); } catch { }
                if (!av) continue;
                string why; bool ok = false; try { ok = Part.CanPlaceGeneric(pd, ship, false, out why); } catch { }
                if (ok) cand.Add(pd);
            }
            if (cand.Count == 0) { if (secLogged++ < 20) Log($"{key} - no secondary tower: none available that the hull takes"); return; }
            cand.Sort((a, b) =>
            {
                int sa = (a.name ?? "").Contains("tower_sec") ? 0 : 1, sb = (b.name ?? "").Contains("tower_sec") ? 0 : 1;
                if (sa != sb) return sa - sb;
                return a.weight.CompareTo(b.weight);
            });
            // free centre-line mounts, aft of the main tower first (nearest first), then forward
            var mounts = new List<Mount>(); var taken = new List<(Mount m, Part gun)>();
            try
            {
                var ms = ship.mounts;
                for (int i = 0; ms != null && i < ms.Count; i++)
                {
                    var m = ms[i]; if (m == null) continue;
                    bool side = false; try { side = m.side || Math.Abs(m.transform.localPosition.x) > 0.3f; } catch { }
                    if (side) continue;                                       // centre line only (instability_x)
                    Part occ = null; try { occ = m.employedPart; } catch { }
                    if (occ != null)
                    {
                        bool light = false; try { light = occ.data != null && occ.data.isGun && !ship.IsMainCal(occ.data); } catch { }
                        if (light) taken.Add((m, occ));
                        continue;
                    }
                    bool used = false; try { used = ship.mountsUsed.ContainsKey(m); } catch { }
                    if (used) continue;
                    mounts.Add(m);
                }
            }
            catch { }
            // lighter guns give way only when no free mount takes a tower: lightest first
            taken.Sort((a, b) => a.gun.data.caliber.CompareTo(b.gun.data.caliber));
            mounts.Sort((a, b) =>
            {
                float za = 0f, zb = 0f; try { za = a.transform.localPosition.z; zb = b.transform.localPosition.z; } catch { }
                bool aa = za < tmz, ab = zb < tmz; if (aa != ab) return aa ? -1 : 1;
                return Math.Abs(za - tmz).CompareTo(Math.Abs(zb - tmz));
            });
            int tries = 0;
            foreach (var pd in cand)
            {
                foreach (var m in mounts)
                {
                    bool fits = false; try { fits = m.Fits(pd, null, null); } catch { }
                    if (!fits) continue;
                    if (tries++ > 60) break;
                    Part part = null;
                    try
                    {
                        part = Part.Create(pd, ship, ship.partsCont, new Il2CppSystem.Nullable<int>(), true);
                        if (part == null) continue;
                        ship.AddPart(part);
                        try { part.LoadModel(ship, true); } catch { }
                        try { part.UpdateCollidersSize(ship); } catch { }
                        part.Mount(m, true);
                        string why = null; bool ok = true; try { ok = part.CanPlace(out why); } catch { }
                        if (!ok && why != "available") { ship.RemovePart(part, true, false); continue; }
                        if (secLogged++ < 30) Log($"{key} - attempt without a secondary tower: {pd.name} put on a free centre-line mount {(m.transform.localPosition.z < tmz ? "aft" : "forward")} of the main tower");
                        return;
                    }
                    catch (Exception e) { if (secLogged++ < 10) Log("secondary tower place: " + e.Message); try { if (part != null) ship.RemovePart(part, true, false); } catch { } }
                }
            }
            // no free mount: a lighter gun on a centre-line mount that takes a tower makes room
            foreach (var (m, gun) in taken)
            {
                foreach (var pd in cand)
                {
                    bool fits = false; try { fits = m.Fits(pd, null, null); } catch { }
                    if (!fits) continue;
                    Part part = null; string gunName = gun.data.name;
                    try
                    {
                        gun.Unmount(false);
                        part = Part.Create(pd, ship, ship.partsCont, new Il2CppSystem.Nullable<int>(), true);
                        if (part == null) { gun.Mount(m, false); continue; }
                        ship.AddPart(part);
                        try { part.LoadModel(ship, true); } catch { }
                        try { part.UpdateCollidersSize(ship); } catch { }
                        part.Mount(m, true);
                        string why = null; bool ok = true; try { ok = part.CanPlace(out why); } catch { }
                        if (!ok && why != "available") { ship.RemovePart(part, true, false); try { gun.Mount(m, false); } catch { } continue; }
                        ship.RemovePart(gun, true, false);
                        if (secLogged++ < 30) Log($"{key} - attempt without a secondary tower: {pd.name} put on the centre-line mount of {gunName} (gun removed)");
                        return;
                    }
                    catch (Exception e) { if (secLogged++ < 10) Log("secondary tower swap: " + e.Message); try { if (part != null) ship.RemovePart(part, true, false); } catch { } try { gun.Mount(m, false); } catch { } }
                }
            }
            // 1.11.414 (test 30.10 04:39-04:51, Britain bb_4_britain_nelson: 266 free centre-line mounts, none takes a tower - towers on
            // that hull are placed freely on the deck, not on mounts): free placement on the centre line, aft of the main tower first,
            // in 2 m steps, on the deck found at that point (Ship.FindDeckAtPoint, Part.Place), the room checked with Part.CanPlace
            // 1.11.415 (test 30.10 05:12-05:19, Germany bb_5_bismarck: the deck search never succeeded): positions in world space
            // from the main tower along the ship's own axis (the tower's localPosition is relative to its parent, not to the ship);
            // the refusals are counted and logged
            Transform st = null; Vector3 basePos = Vector3.zero, fwd = Vector3.forward;
            try { st = ship.transform; fwd = st.forward; fwd.y = 0f; fwd.Normalize(); } catch { }
            for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d != null && (d.type ?? "") == "tower_main") { try { basePos = parts[i].transform.position; } catch { } break; } }
            var refused = new Dictionary<string, int>(); int noDeck = 0, removals = 0;
            if (st != null)
            {
                foreach (var pd in cand)
                {
                    Part part = null;
                    try
                    {
                        part = Part.Create(pd, ship, ship.partsCont, new Il2CppSystem.Nullable<int>(), true);
                        if (part == null) continue;
                        ship.AddPart(part);
                        try { part.LoadModel(ship, true); } catch { }
                        try { part.UpdateCollidersSize(ship); } catch { }
                        // 1.11.416 (test 30.10 05:41-05:43: forward of the main tower the game refuses every place - "Secondary Tower
                        // should not be placed in front of Main Tower" - and aft every place collided with a part): aft only, up to 120 m,
                        // and where only lighter guns (not of the main calibre) are in the way, they give way
                        for (int k = 1; k <= 60; k++)
                        {
                            float dz = -2f * k;
                            var wp = basePos + fwd * dz; wp.y = basePos.y + 30f;
                            BoxCollider deck = null; try { deck = ship.FindDeckAtPoint(wp); } catch { }
                            if (deck == null) { wp.y = basePos.y; try { deck = ship.FindDeckAtPoint(wp); } catch { } }
                            if (deck == null) { noDeck++; continue; }
                            wp.y = deck.bounds.max.y;
                            try { part.Place(wp, true); } catch (Exception e) { int c0; refused.TryGetValue("place: " + e.Message, out c0); refused["place: " + e.Message] = c0 + 1; continue; }
                            string why = null; bool ok = false; Il2CppSystem.Collections.Generic.List<Part> ovp = null; Il2CppSystem.Collections.Generic.List<Collider> ovb = null;
                            try { ok = part.CanPlace(out why, out ovp, out ovb); } catch { try { ok = part.CanPlace(out why); } catch { } }
                            int cleared = 0;
                            if (!ok && why == "part" && ovp != null && ovp.Count > 0 && (ovb == null || ovb.Count == 0))
                            {
                                // only lighter guns in the way: they go
                                bool onlyLight = true; var gone = new List<Part>();
                                for (int q = 0; q < ovp.Count; q++)
                                {
                                    var op = ovp[q]; if (op == null || op.Pointer == part.Pointer) continue;
                                    bool light = false; try { light = op.data != null && op.data.isGun && !ship.IsMainCal(op.data); } catch { }
                                    if (!light) { onlyLight = false; break; }
                                    gone.Add(op);
                                }
                                if (onlyLight && gone.Count > 0 && gone.Count <= 6 && removals < 2)
                                {
                                    removals++;   // at most two clearings per attempt, so a failed search cannot strip the ship
                                    foreach (var gp in gone) { try { ship.RemovePart(gp, true, false); cleared++; } catch { } }
                                    try { ok = part.CanPlace(out why); } catch { ok = false; }
                                }
                            }
                            if (!ok && why != "available") { string w2 = why ?? "(no reason)"; int c1; refused.TryGetValue(w2, out c1); refused[w2] = c1 + 1; }
                            if (ok || why == "available")
                            {
                                if (secLogged++ < 30) Log($"{key} - attempt without a secondary tower: {pd.name} placed on the deck {Math.Abs(dz):0} m aft of the main tower{(cleared > 0 ? $" ({cleared} lighter gun mount(s) removed to make room)" : "")}");
                                return;
                            }
                        }
                        ship.RemovePart(part, true, false);
                    }
                    catch (Exception e) { if (secLogged++ < 10) Log("secondary tower deck: " + e.Message); try { if (part != null) ship.RemovePart(part, true, false); } catch { } }
                }
            }
            if (secLogged++ < 20) Log($"{key} - attempt without a secondary tower: {cand.Count} tower(s) available, no centre-line mount or deck place takes one ({mounts.Count} free mounts, {taken.Count} with lighter guns; deck places: {noDeck} without deck{string.Concat(refused.OrderByDescending(x => x.Value).Take(3).Select(x => $", {x.Value}x {x.Key}"))})");
        }
        static void Trim(Ship ship, bool early)
        {
            float w = ship.Weight(true, true), t = ship.Tonnage();
            if (t <= 0f || w <= t) return;
            float max = TrimMax != null ? TrimMax.Value : 0.12f;
            if (!early && w > t * (1f + max)) { if (trimLogged++ < 20) Log($"overweight {w:0} t of {t:0} t ({(w / t - 1f) * 100f:0.0} %) - over the trimming limit {max * 100f:0} %, left to the game"); return; }
            // candidates: guns that are not of the main calibre; smallest calibre first, deck mounts before casemates
            var cand = new List<Part>();
            var parts = ship.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                var q = parts[i]; if (q == null || q.data == null || !q.data.isGun) continue;
                bool main = false; try { main = ship.IsMainCal(q.data); } catch { }
                if (main) continue;
                cand.Add(q);
            }
            cand.Sort((a, b) =>
            {
                int ca = HistoricalWeights.IsCasemate(a.data) ? 1 : 0, cb = HistoricalWeights.IsCasemate(b.data) ? 1 : 0;
                if (ca != cb) return ca - cb;
                return a.data.caliber.CompareTo(b.data.caliber);
            });
            // the secondary battery proper (4" and up, deck mounts) keeps at least 4 mounts - a battleship without secondaries is no design
            int secLeft = 0, terLeft = 0; foreach (var q0 in cand) { if (HistoricalWeights.IsCasemate(q0.data)) continue; if (q0.data.caliber >= 100f) secLeft++; else terLeft++; }
            int secFloor = early ? 12 : 4, terFloor = early ? 8 : 0;
            int removed = 0; var gone = new HashSet<IntPtr>(); var what = new SortedDictionary<string, int>();
            for (int i = 0; i < cand.Count && w > t; i++)
            {
                var q = cand[i]; if (gone.Contains(q.Pointer)) continue;
                bool casem = HistoricalWeights.IsCasemate(q.data);
                if (early && casem) continue;                       // casemates stay in the first pass
                bool sec = q.data.caliber >= 100f && !casem, ter = !sec && !casem;
                if (sec && secLeft <= secFloor) continue;
                if (ter && terLeft <= terFloor) continue;
                // the mirror mount of a side gun goes with it (same part, opposite side, same length position)
                Part mirror = null;
                try
                {
                    var lp = q.transform.localPosition;
                    if (Math.Abs(lp.x) > 0.5f)
                        for (int j = i + 1; j < cand.Count; j++)
                        {
                            var r = cand[j]; if (gone.Contains(r.Pointer) || r.data != q.data) continue;
                            var rp = r.transform.localPosition;
                            if (Math.Abs(rp.x + lp.x) < 0.3f && Math.Abs(rp.z - lp.z) < 0.3f) { mirror = r; break; }
                        }
                }
                catch { }
                string key = $"{q.data.caliber / 25.4f:0}\" x{Math.Max(1, q.data.barrels)}{(HistoricalWeights.IsCasemate(q.data) ? " casemate" : "")}";
                try { ship.RemovePart(q, true, true); gone.Add(q.Pointer); removed++; if (sec) secLeft--; if (ter) terLeft--; int c; what.TryGetValue(key, out c); what[key] = c + 1; } catch (Exception e) { if (trimLogged++ < 10) Log("remove: " + e.Message); break; }
                if (mirror != null) { try { ship.RemovePart(mirror, true, true); gone.Add(mirror.Pointer); removed++; if (sec) secLeft--; if (ter) terLeft--; what[key]++; } catch { } }
                try { w = ship.Weight(true, true); } catch { break; }
            }
            if (removed > 0 && trimLogged++ < 40)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in what) { if (sb.Length > 0) sb.Append(", "); sb.Append($"{kv.Value}x {kv.Key}"); }
                string key2 = ""; try { key2 = ship.player.data.name + ": hull " + ship.hull.data.name; } catch { }
                Log($"{key2} - attempt overweight{(early ? " before the armour is cut" : "")}, {removed} light gun mount(s) removed ({sb}): now {w:0} t of {t:0} t{(w > t ? (early ? " - the rest to the game's weight reduction" : " - still over") : "")}");
            }
        }

        // ---- 1. early stop ----
        static readonly Dictionary<IntPtr, int> lastTry = new Dictionary<IntPtr, int>();
        // 1.11.478 (author 30.10 21:47: "maybe a time limit for designs, so a turn takes no longer than 60 seconds"): the generator's
        // time is counted per turn (from its first attempt in that month); past ShipGenTurnBudget seconds a failed attempt is followed by
        // at most one more - the failure memory then keeps the nation/hull pair away on later turns
        internal static MelonPreferences_Entry<float> TurnBudget, HardStop;
        internal static bool BudgetUsed() { return TurnBudget != null && TurnBudget.Value > 0f && budgetMonth == Economy.Month() && Time.realtimeSinceStartup - budgetStart > TurnBudget.Value; }
        static readonly HashSet<string> stopLogged = new HashSet<string>();
        // a nation skipped one month is never skipped the next month (the AI order of a turn is fixed - the same nations would always lose)
        static readonly Dictionary<string, int> skippedAt = new Dictionary<string, int>();
        internal static bool HardStopNow(Player p)
        {
            try
            {
                if (p == null || p.isMain || HardStop == null || HardStop.Value <= 0f || budgetMonth != Economy.Month()) return false;
                float used = Time.realtimeSinceStartup - budgetStart; if (used <= HardStop.Value) return false;
                string n = p.data != null ? p.data.name : "?";
                if (skippedAt.TryGetValue(n, out int last) && budgetMonth - last <= 1) return false;
                skippedAt[n] = budgetMonth;
                if (stopLogged.Add(n + "|" + budgetMonth)) Log($"{n}: no shipbuilding this month - the AI's ship design work has taken {used:0} s of this turn (limit {HardStop.Value:0} s, ShipGenTurnHardStop); it builds next month");
                return true;
            }
            catch { return false; }
        }
        static int budgetMonth = -1; static float budgetStart; static int budgetCuts;
        internal static void Attempt(Ship._GenerateRandomShip_d__573 it, Ship ship, string key, bool running)
        {
            var p = it.Pointer;
            if (!running) { lastTry.Remove(p); RestoreHull(p); return; }
            int m0 = Economy.Month(); if (m0 != budgetMonth) { budgetMonth = m0; budgetStart = Time.realtimeSinceStartup; }
            if (EarlyStop == null || !EarlyStop.Value) return;
            int tryN = it._tryN_5__5, total = it._triesTotal_5__4;
            int prev; if (!lastTry.TryGetValue(p, out prev)) prev = 0;
            if (tryN <= prev) return;
            lastTry[p] = tryN;
            float used = Time.realtimeSinceStartup - budgetStart;
            if (TurnBudget != null && TurnBudget.Value > 0f && used > TurnBudget.Value && tryN + 1 < total)
            {
                it._triesTotal_5__4 = tryN + 1; ShipGenMemory.saved += total - tryN - 1; budgetCuts++;
                try { Log($"{key.Replace("|", ": hull ")} - attempt {tryN} of {total} failed and the generator has used {used:0} s of its {TurnBudget.Value:0} s this turn - one more attempt instead of {total - tryN}"); } catch { }
                return;
            }
            try { Log($"{key.Replace("|", ": hull ")} - attempt {tryN} of {total} failed: {Summary(ship)}"); } catch { } // 1.11.237: what the failed attempt carried
            if (tryN + 1 >= total) return; // one attempt left anyway
            string why; if (!Structural(ship, out why)) return;
            // 1.11.223 (author's decision 24.10): a modern hull that cannot take its full main battery is not given up - the next
            //           attempt may carry one main turret (two barrels) fewer; the generator then fits what the hull takes (24.10:
            //           France failed on Dreadnought IV with 3 of 4 turrets, and the AI fell back to 1890 hulls). The hull's minimum
            //           is lowered only for this generation and put back when it ends.
            if (Relax(p, ship, why)) { Log($"{key.Replace("|", ": hull ")} - attempt {tryN} of {total} failed on the hull itself ({why}) - next attempt with one main turret fewer allowed"); return; }
            // 1.11.197: the coroutine has already decided to go on with the next attempt when this runs (24.10 log: "Attempt 2 / 1"
            //           after the total was cut to the failed attempt), so the total is cut to one more attempt - the success rule
            //           of the report (attempts used up = failure) stays consistent
            it._triesTotal_5__4 = tryN + 1; ShipGenMemory.saved += total - tryN - 1;
            Log($"{key.Replace("|", ": hull ")} - attempt {tryN} of {total} failed on the hull itself ({why}) - one more attempt instead of {total - tryN}");
        }
        // 1.11.237: what a failed attempt carried - guns by calibre (turrets x barrels, casemates), tonnage against the hull's maximum
        static string Summary(Ship ship)
        {
            try
            {
                var guns = new SortedDictionary<float, int[]>(); int torp = 0;
                var parts = ship.parts;
                for (int i = 0; i < parts.Count; i++)
                {
                    var d = parts[i] != null ? parts[i].data : null; if (d == null) continue;
                    if (d.isTorpedo) { torp++; continue; }
                    if (!d.isGun) continue;
                    float cal = 0f; try { cal = (float)Math.Round(d.GetCaliberInch(ship), 1); } catch { }
                    bool casemate = d.name != null && d.name.IndexOf("casemate", StringComparison.OrdinalIgnoreCase) >= 0;
                    int[] c; if (!guns.TryGetValue(cal, out c)) { c = new int[3]; guns[cal] = c; }
                    if (casemate) c[2]++; else { c[0]++; c[1] += Math.Max(1, d.barrels); }
                }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in guns.Reverse())
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(kv.Key.ToString("0.#")).Append("\"");
                    if (kv.Value[0] > 0) sb.Append($" {kv.Value[0]}x{(kv.Value[0] > 0 ? kv.Value[1] / kv.Value[0] : 0)}");
                    if (kv.Value[2] > 0) sb.Append($" {kv.Value[2]} casemate(s)");
                }
                if (torp > 0) sb.Append($", {torp} torpedo mount(s)");
                float ton = 0f, max = 0f; try { ton = ship.Tonnage(); } catch { } try { max = ship.hull != null && ship.hull.data != null ? ship.hull.data.tonnageMax : 0f; } catch { }
                return $"{sb} - {ton:0} t of {max:0} t max";
            }
            catch (Exception e) { return "summary failed: " + e.Message; }
        }
        // 1.11.223: hull minimum relaxed by one turret / two barrels for the rest of this generation (floor: 2 turrets, 4 barrels)
        internal static MelonPreferences_Entry<bool> RelaxTurrets;
        class HullMin { public PartData Hull; public int T, B; }
        static readonly Dictionary<IntPtr, HullMin> relaxed = new Dictionary<IntPtr, HullMin>();
        static bool Relax(IntPtr gen, Ship ship, string why)
        {
            try
            {
                if (RelaxTurrets != null && !RelaxTurrets.Value) return false;
                if (relaxed.ContainsKey(gen)) return false;                        // once per generation
                var hd = ship.hull != null ? ship.hull.data : null; if (hd == null) return false;
                int t = hd.minMainTurrets, b = hd.minMainBarrels;
                if (t <= 2 && b <= 4) return false;
                relaxed[gen] = new HullMin { Hull = hd, T = t, B = b };
                if (t > 2) hd.minMainTurrets = t - 1;
                if (b > 4) hd.minMainBarrels = Math.Max(4, b - 2);
                return true;
            }
            catch { return false; }
        }
        static void RestoreHull(IntPtr gen)
        {
            HullMin h; if (!relaxed.TryGetValue(gen, out h)) return;
            relaxed.Remove(gen);
            try { h.Hull.minMainTurrets = h.T; h.Hull.minMainBarrels = h.B; } catch { }
        }
        // the hull cannot take the required main battery: fewer main turrets than the hull minimum AND fewer barrels than its minimum
        // (the game needs only one of the two) - some main guns must be present, so a ship cleared for the next attempt never counts
        static bool Structural(Ship ship, out string why)
        {
            why = null;
            try
            {
                var hull = ship.hull; if (hull == null || hull.data == null) return false;
                int minT = hull.data.minMainTurrets, minB = hull.data.minMainBarrels;
                if (minT <= 0 && minB <= 0) return false;
                var parts = ship.parts; float top = 0f; int turrets = 0, barrels = 0;
                for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d == null || !d.isGun) continue; if (d.caliber > top) top = d.caliber; }
                for (int i = 0; i < parts.Count; i++) { var d = parts[i] != null ? parts[i].data : null; if (d == null || !d.isGun) continue; if (Mathf.Abs(d.caliber - top) < 0.01f) { turrets++; barrels += Math.Max(1, d.barrels); } }
                if (turrets == 0) return false;
                bool tOk = minT <= 0 || turrets >= minT, bOk = minB <= 0 || barrels >= minB;
                if (tOk || bOk) return false;
                why = $"main turrets {turrets} of {minT}, barrels {barrels} of {minB}";
                return true;
            }
            catch { return false; }
        }

        // ---- 2. newest hulls ----
        internal static bool Active; internal static IntPtr ActivePlayer;
        static bool computing;
        static Dictionary<string, int> unlockYear;            // hull -> year of its unlocking technology
        static Dictionary<string, string> hullType;           // hull -> ship type (type(..) in parts.csv param)
        static readonly Dictionary<string, int> newest = new Dictionary<string, int>(); // player|type -> newest unlock year the player has (cached per month)
        static int newestMonth = -1; static int logged; static readonly HashSet<string> loggedKeys = new HashSet<string>();
        static void LoadTables()
        {
            if (unlockYear != null) return;
            unlockYear = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); hullType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string mods = Path.Combine(MelonUtils.GameDirectory, "Mods");
                foreach (var line in File.ReadAllLines(Path.Combine(mods, "technologies.csv")))
                {
                    if (line.StartsWith("@") || line.StartsWith("#") || line.Length < 5) continue;
                    var f = Split(line); if (f.Count < 8) continue;
                    int y; if (!int.TryParse(f[4].Trim(), out y)) continue;
                    foreach (var eff in f[7].Split(','))
                    {
                        int i = eff.IndexOf("unlock(", StringComparison.Ordinal); if (i < 0) continue;
                        int j = eff.IndexOf(')', i); if (j < 0) continue;
                        foreach (var h in eff.Substring(i + 7, j - i - 7).Split(';')) { var n = h.Trim(); if (n.Length == 0) continue; int old; if (!unlockYear.TryGetValue(n, out old) || y < old) unlockYear[n] = y; }
                    }
                }
                foreach (var line in File.ReadAllLines(Path.Combine(mods, "parts.csv")))
                {
                    if (line.StartsWith("@") || line.StartsWith("#") || line.Length < 5) continue;
                    var f = Split(line); if (f.Count < 15 || f[2].Trim() != "hull") continue;
                    var prm = f[14]; int i = prm.IndexOf("type(", StringComparison.Ordinal); if (i < 0) continue;
                    int j = prm.IndexOf(')', i); if (j < 0) continue;
                    hullType[f[0].Trim()] = prm.Substring(i + 5, j - i - 5).Trim();
                }
                Log($"newest hulls: {unlockYear.Count} hull unlock year(s) from technologies.csv, {hullType.Count} hull type(s) from parts.csv");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] hull tables: " + e.Message); }
        }
        static List<string> Split(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (char c in line) { if (c == '"') q = !q; else if (c == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
            r.Add(sb.ToString()); return r;
        }
        static int MonthIndex() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; } }

        // postfix of Player.IsHullUnlocked(PartData): during AI design generation an old hull does not count as unlocked
        internal static void Filter(Player player, PartData hull, ref bool result)
        {
            if (!result || computing || !Active || NewHulls == null || !NewHulls.Value || player == null || hull == null) return;
            if (player.Pointer != ActivePlayer) return;
            LoadTables();
            string name = hull.name; int y; string type;
            if (!hullType.TryGetValue(name, out type)) return; // unknown hulls stay
            // 1.11.222 (author, 24.10): start hulls (no unlocking technology - the 1890 hulls) count as 1890 and fall under the window
            //           like any other; before they always passed, and in 1922 the AI still laid down 3-Mast Armored Cruisers and
            //           Dreadnought-era battleships on the 1890 French large hull while modern hulls failed in the generator
            if (!unlockYear.TryGetValue(name, out y)) y = 1890;
            string pk = player.data != null ? player.data.name : player.Pointer.ToString();
            // 1.11.403 (log 29.10 19:11-19:17, custom battle 1920: Japan's generator took the 1927 hull bb_5_old - Modern Battleship I,
            // the Yamato-style hull - and failed attempt after attempt, a minute each, because the main tower and funnel that fit it
            // were not available in 1920 ("Unmet Requirements: tower_main 0, funnel 0", "Tonnage outside tech range")): in a custom
            // battle a hull whose unlocking technology is later than the battle year is not offered to the generator
            int cy = CustomBattle.LastYear;
            bool cb = false; try { cb = GameManager.IsCustomBattle || GameManager.IsCustomBattleSetup; } catch { }
            if (cb && cy > 0 && y > cy)
            {
                result = false;
                if (logged < 60 && loggedKeys.Add(pk + "|" + name + "|cb")) { logged++; Log($"{pk}: hull {name} ({y}) left out for the AI design - later than the custom battle year {cy}"); }
                return;
            }
            string key = pk + "|" + type;
            int m = MonthIndex(); if (m != newestMonth) { newest.Clear(); newestMonth = m; }
            int top;
            if (!newest.TryGetValue(key, out top))
            {
                top = 0; computing = true;
                try
                {
                    foreach (var kv in hullType)
                    {
                        if (kv.Value != type) continue;
                        int hy; if (!unlockYear.TryGetValue(kv.Key, out hy) || hy <= top) continue;
                        PartData pd = null; try { pd = G.GameData.parts[kv.Key]; } catch { }
                        if (pd == null) continue;
                        bool ok = false; try { ok = player.IsHullUnlocked(pd); } catch { }
                        if (ok) top = hy;
                    }
                }
                finally { computing = false; }
                newest[key] = top;
            }
            int window = HullWindow != null ? Math.Max(0, HullWindow.Value) : 10;
            if (top > 0 && y < top - window)
            {
                result = false;
                if (logged < 60 && loggedKeys.Add(pk + "|" + name)) { logged++; Log($"{pk}: hull {name} ({y}) left out for the AI design - newest {type} hull technology {top}, window {window} years"); }
            }
        }

        // 1.11.219: for the fleet age report
        internal static int HullYear(string hull) { LoadTables(); int y; return hull != null && unlockYear.TryGetValue(hull, out y) ? y : 0; }
        internal static int NewestUnlocked(Player player, string type, out string hullName)
        {
            LoadTables(); hullName = null; int top = 0;
            try
            {
                computing = true;
                foreach (var kv in hullType)
                {
                    if (kv.Value != type) continue;
                    int hy; if (!unlockYear.TryGetValue(kv.Key, out hy) || hy <= top) continue;
                    PartData pd = null; try { pd = G.GameData.parts[kv.Key]; } catch { }
                    if (pd == null) continue;
                    bool ok = false; try { ok = player.IsHullUnlocked(pd); } catch { }
                    if (ok) { top = hy; hullName = kv.Key; }
                }
            }
            catch { }
            finally { computing = false; }
            return top;
        }

        // ---- 3. kept designs ----
        internal static void Share(Ship ship, string key)
        {
            if (ShareAi == null || !ShareAi.Value || ship == null) return;
            try
            {
                var player = ship.player; if (player == null || player.data == null || !player.isAi) return;
                bool valid = false; try { valid = ship.IsValid(false); } catch { }
                if (!valid) { Log($"{key.Replace("|", ": hull ")} - design not kept: the game does not accept it as valid"); return; } // 1.11.197
            if (!ShipPartsCheck.AllMounted(ship)) { Log($"{key.Replace("|", ": hull ")} - design not kept: a part has no mount on this hull (it would load as a bare hull in battle)"); return; } // 1.11.217
                var st = ship.shipType; string type = st != null ? st.name : "ship";
                var store = ship.ToStore(false); if (store == null) return;
                string nation = player.data.name;
                string origId = store.id.ToString(); string shortId = origId.Length >= 8 ? origId.Substring(0, 8) : origId;
                string fileName; string path = Storage.GetSavedDesignPath($"{store.YearCreated} {nation} {type} {store.vesselName} [{shortId}][gen]", out fileName);
                if (string.IsNullOrEmpty(path)) return;
                string dir = Path.GetDirectoryName(path); int have = 0;
                try { foreach (var f in Directory.GetFiles(dir, $"* {nation} {type} *[gen].bindesign")) have++; } catch { }
                int max = ShareMax != null ? ShareMax.Value : 8;
                if (have >= max) { Log($"{key.Replace("|", ": hull ")} - design not kept: {have} [gen] design(s) of {nation} {type} already"); return; }
                store.playerName = nation; store.playerOriginal = nation; store.isSharedDesign = true; store.id = Il2CppSystem.Guid.NewGuid();
                Storage.SaveSharedDesignShipByte(path, Util.SerializeObjectByte<Ship.Store>(store));
                Log($"{key.Replace("|", ": hull ")} - design kept as shared design {fileName} ({have + 1} of {max} for {nation} {type}); it loads on the next game start");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] keep design: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Player), "IsHullUnlocked", new Type[] { typeof(PartData) })]
    internal static class Patch_IsHullUnlocked_ShipGen
    {
        private static void Postfix(Player __instance, PartData __0, ref bool __result)
        {
            try { ShipGenSpeed.Filter(__instance, __0, ref __result); }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][ShipGen] hull filter: " + e.Message); }
        }
    }

    // 1.11.245: Ship.IsValidWeightOffset accepts a small side instability while the generator runs (GeneratorInstabilityMax)
    [HarmonyPatch(typeof(Ship), nameof(Ship.IsValidWeightOffset))]
    internal static class Patch_WeightOffset_ShipGen
    {
        private static void Postfix(Ship __instance, ref bool __result)
        {
            if (__result) return;
            try { if (ShipGenSpeed.AcceptOffset(__instance)) __result = true; } catch { }
        }
    }
}
