// 1.11.251 (beta): torpedo settings chosen in battle.
// Torpedoes of the period had more than one setting of the same weapon: a short, fast run and a long, slow one (from about 1916
// some navies added an extreme-range setting for firing into a battle line). The setting was chosen for the attack - at Jutland
// destroyers fired on the long-range setting by day and on both settings at night (BJMH, "Torpedoes at Jutland").
//   * Settings available to a ship follow its navy and the torpedo propulsion technology it carries (torpedoSettings.csv, built
//     from the navies' own torpedoes in NavWeaps; anchored on the technology year, never the calendar). One setting where the
//     navy's torpedoes of that generation had one (USA until the Mark 11 of 1926), two (high speed + long range) or three
//     (+ extreme range).
//   * The ratios of each setting to the long-range setting are those of the navy's torpedoes of that generation.
//   * Torpedo modifiers are multiplied instead of added: the game adds the diameter bonus and the propulsion bonus
//     (1 + size + propulsion); the mod makes it (1 + size) x (1 + propulsion), so the share of a setting no longer drifts with
//     the diameter. The old "fast propulsion" component no longer changes the torpedo - the settings replace it.
//   * Changing the setting resets the tubes: a tube is ready again TorpedoSettingChangeSeconds after the change (or when its
//     reload ends, whichever is later).
//   * AI ships and the player's ships on "Auto" pick the fastest setting whose run to the intercept point stays inside its range
//     less TorpedoSettingMargin (25 %, the German safety allowance at Jutland); the extreme-range setting only against a battle
//     line beyond the long range. A ship with loaded tubes and a possible shot fires first and resets the tubes afterwards.
//   * The player can set a division (panel button) or a single ship (Shift + button); "Auto" gives the choice back.
// Switch: TorpedoSettings (F10, Battle AI).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoSettings
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> ChangeSeconds, Margin;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TorpedoSettings", true, null, "Battle: torpedo settings (high speed / long range / extreme range) chosen in battle as the navy's torpedoes of that generation allowed; the AI and the player's ships on Auto pick the setting by the distance to the target; torpedo modifiers multiply instead of add");
            ChangeSeconds = cat.CreateEntry("TorpedoSettingChangeSeconds", 60f, null, "Battle: seconds the torpedo tubes need after a change of the torpedo setting");
            Margin = cat.CreateEntry("TorpedoSettingMargin", 0.25f, null, "Battle: share of the torpedo range kept as a safety allowance when the setting is chosen (0.25 = the run to the intercept point may use 75 % of the range)");
        }
        static bool Enabled { get { return On != null && On.Value; } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][TorpSet] " + m); }

        internal const int HS = 0, LR = 1, ER = 2;
        static readonly string[] Names = { "High Speed", "Long Range", "Extreme Range" };
        internal static string Name(int s) { return s >= 0 && s < 3 ? Names[s] : "Auto"; }

        // ------------------------------------------------------------------ data
        class Prof { public int N = 1; public float[] R = { 1f, 1f, 1f }; public float[] V = { 1f, 1f, 1f }; public string Src = ""; }
        static readonly Dictionary<string, SortedDictionary<int, Prof>> profiles = new Dictionary<string, SortedDictionary<int, Prof>>();
        static readonly Dictionary<string, float[]> compMods = new Dictionary<string, float[]>();  // component -> {range mod, speed mod} (fractions)
        static readonly Dictionary<string, string> compType = new Dictionary<string, string>();
        static bool loaded;

        static List<string> SplitCsv(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (char c in line)
            {
                if (c == '"') { q = !q; continue; }
                if (c == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(c);
            }
            r.Add(sb.ToString()); return r;
        }
        static float F(string s, float d) { float v; return float.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
        static float Eff(string effects, string name)
        {
            int i = effects.IndexOf(name + "(", StringComparison.Ordinal); if (i < 0) return 0f;
            int a = i + name.Length + 1, b = effects.IndexOf(')', a); if (b < 0) return 0f;
            return F(effects.Substring(a, b - a), 0f) / 100f;
        }
        static void Load()
        {
            if (loaded) return; loaded = true;
            string dir = Path.Combine(MelonUtils.GameDirectory, "Mods");
            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(dir, "torpedoSettings.csv")))
                {
                    if (line.Length == 0 || line[0] == '#' || line[0] == '@') continue;
                    var c = SplitCsv(line); if (c.Count < 7) continue;
                    string nat = c[0].Trim().ToLowerInvariant(); int k; if (!int.TryParse(c[1].Replace("torpedo_prop_", "").Trim(), out k)) continue;
                    var p = new Prof { N = Math.Max(1, Math.Min(3, (int)F(c[2], 1f))), Src = c.Count > 7 ? c[7] : "" };
                    p.R[HS] = F(c[3], 1f); p.V[HS] = F(c[4], 1f); p.R[ER] = F(c[5], 1f); p.V[ER] = F(c[6], 1f);
                    if (!profiles.TryGetValue(nat, out var d)) { d = new SortedDictionary<int, Prof>(); profiles[nat] = d; }
                    d[k] = p;
                }
            }
            catch (Exception e) { Log("torpedoSettings.csv not read: " + e.Message); }
            try
            {
                foreach (var line in File.ReadAllLines(Path.Combine(dir, "technologies.csv")))
                {
                    if (!line.StartsWith("torpedo_", StringComparison.Ordinal)) continue;
                    var c = SplitCsv(line); if (c.Count < 9) continue;
                    string comp = c[8].Trim(); if (comp.Length == 0) continue;
                    string eff = c[7];
                    compMods[comp] = new[] { Eff(eff, "torpedo_range_mod"), Eff(eff, "torpedo_speed_mod") };
                    compType[comp] = c[1].Trim();
                }
            }
            catch (Exception e) { Log("technologies.csv not read: " + e.Message); }
            Log($"loaded: {profiles.Count} navy tables ({string.Join(", ", profiles.Keys)}), {compMods.Count} torpedo components");
        }

        // ------------------------------------------------------------------ per ship
        class St
        {
            public bool Init; public Prof P; public int Gen; public string Nation;
            public float CorrR = 1f, CorrV = 1f;
            public int Cur = LR, Pending = -1; public string PendingWhy; public float ReadyAt, LastChange = -1000f, NextThink, WantSince; public int Want = -1;
            public float BaseKn;   // long-range speed as the game reports it (knots), measured at the first decision
            public bool NeedRecalc, FiredSince;
        }
        static readonly Dictionary<IntPtr, St> states = new Dictionary<IntPtr, St>();
        internal static readonly Dictionary<IntPtr, int> shipManual = new Dictionary<IntPtr, int>();
        internal static readonly Dictionary<IntPtr, int> divManual = new Dictionary<IntPtr, int>();
        static readonly Dictionary<IntPtr, float> divSaid = new Dictionary<IntPtr, float>();
        static IntPtr battlePtr; static int logs, errors, calLogs, measLogs;

        static void ResetIfNewBattle()
        {
            IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
            if (bp != battlePtr) { battlePtr = bp; states.Clear(); shipManual.Clear(); divManual.Clear(); divSaid.Clear(); tracks.Clear(); logs = 0; calLogs = 0; measLogs = 0; }
        }
        static bool InBattle { get { try { return GameManager.IsBattle; } catch { return false; } } }

        static St Get(Ship s)
        {
            ResetIfNewBattle();
            if (!states.TryGetValue(s.Pointer, out var st)) { st = new St(); states[s.Pointer] = st; }
            if (!st.Init) { st.Init = true; Setup(s, st); }
            return st;
        }
        static void Setup(Ship s, St st)
        {
            Load();
            if (logs < 3) Log($"setup {s.name} (battle {InBattle})");
            // propulsion generation: the newest torpedo_prop_5..15 technology of the ship's design, else of its navy
            int gen = 0;
            try { foreach (var t in s.techs) { if (t == null) continue; gen = Math.Max(gen, GenOf(t.name)); } } catch { }
            if (gen == 0)
            {
                try { var tl = s.player != null ? s.player.technologies : null; if (tl != null) for (int i = 0; i < tl.Count; i++) { var t = tl[i]; if (t == null || t.data == null) continue; bool ok = false; try { ok = t.isResearched; } catch { } if (ok) gen = Math.Max(gen, GenOf(t.data.name)); } } catch { }
            }
            if (gen == 0) gen = 5;
            st.Gen = gen;
            string nat = ""; try { nat = s.player.data.name.ToLowerInvariant(); } catch { }
            st.Nation = nat;
            st.P = Pick(nat, gen, out string used);
            string comps = Corr(s, out st.CorrR, out st.CorrV);
            if (logs++ < 40) Log($"{s.name}: navy {nat} ({used}), torpedo_prop_{gen}, {st.P.N} setting(s){(st.P.N > 1 ? $" - HS x{st.P.R[HS]:0.00} range x{st.P.V[HS]:0.00} speed" : "")}{(st.P.N > 2 ? $", ER x{st.P.R[ER]:0.00} range x{st.P.V[ER]:0.00} speed" : "")}; components {comps}; multiplied modifiers: range x{st.CorrR:0.000}, speed x{st.CorrV:0.000}");
            // no RecalculateWeaponRanges here: Setup can run inside the game's own recalculation (first WeaponRange call) and a nested
            // recalculation crashed the game while the ships were spawned; the game fills the cache with the factors applied
            st.NeedRecalc = false;
        }
        // multiplied modifiers: game = 1 + S + P, mod = (1 + S) x (1 + P'), P' = 0 for the old fast propulsion
        static string Corr(Ship s, out float cr, out float cv)
        {
            Load();
            float sr = 0f, sv = 0f, pr = 0f, pv = 0f, pr2 = 0f, pv2 = 0f; string comps = "";
            try
            {
                var cl = s.components;
                if (cl != null) foreach (var kv in cl)
                {
                    var cd = kv.Value; if (cd == null) continue; string n = cd.name; if (n == null || !compMods.TryGetValue(n, out var m)) continue;
                    string ty = compType.TryGetValue(n, out var t2) ? t2 : "";
                    comps += (comps.Length > 0 ? ", " : "") + n;
                    if (ty == "torpedo_size") { sr += m[0]; sv += m[1]; }
                    else if (ty == "torpedo_prop") { pr += m[0]; pv += m[1]; if (n != "torpedo_prop_fast") { pr2 += m[0]; pv2 += m[1]; } }
                }
            }
            catch { }
            float addR = 1f + sr + pr, addV = 1f + sv + pv;
            cr = addR > 0.05f ? (1f + sr) * (1f + pr2) / addR : 1f;
            cv = addV > 0.05f ? (1f + sv) * (1f + pv2) / addV : 1f;
            return comps;
        }
        static int GenOf(string n)
        {
            if (n == null || !n.StartsWith("torpedo_prop_", StringComparison.Ordinal)) return 0;
            int k; return int.TryParse(n.Substring(13), out k) && k >= 5 && k <= 15 ? k : 0;
        }
        static Prof Pick(string nat, int gen, out string used)
        {
            used = nat;
            if (!profiles.TryGetValue(nat, out var d)) { used = "generic table"; if (!profiles.TryGetValue("generic", out d)) return new Prof(); }
            Prof best = null; foreach (var kv in d) if (kv.Key <= gen) best = kv.Value;
            return best ?? new Prof();
        }

        // factors applied by the patches
        internal static float RangeFactor(Ship s)
        {
            if (!Enabled || s == null) return 1f;
            try
            {
                if (!InBattle) { Corr(s, out float cr, out float cv); return cr; }   // designer: the components may change at any moment
                var st = Get(s); float f = st.CorrR; if (st.P.N > 1) f *= st.P.R[st.Cur]; return f;
            }
            catch { return 1f; }
        }
        internal static float SpeedFactor(Ship s)
        {
            if (!Enabled || s == null) return 1f;
            try
            {
                if (!InBattle) { Corr(s, out float cr, out float cv); return cv; }
                var st = Get(s); float f = st.CorrV; if (st.P.N > 1) f *= st.P.V[st.Cur]; return f;
            }
            catch { return 1f; }
        }
        internal static int Available(Ship s) { try { return Enabled ? Get(s).P.N : 1; } catch { return 1; } }
        internal static int Current(Ship s) { try { var st = Get(s); return st.Pending >= 0 ? st.Pending : st.Cur; } catch { return LR; } }
        static bool Has(St st, int set) { return set == LR || (set == HS && st.P.N >= 2) || (set == ER && st.P.N >= 3); }

        // after the game's RecalculateWeaponRanges: torpedo entries of the cache x the ship's factor (each recalculation starts from
        // the game's own value, so the factor is applied once)
        static int scaleLogs;
        internal static void ScaleCache(Ship s)
        {
            if (!Enabled || s == null) return;
            try
            {
                var cache = s.weaponRangesCache; if (cache == null) return;
                var keys = new List<PartData>();
                foreach (var kv in cache) { var d = kv.Key; if (d != null && d.isTorpedo) keys.Add(d); }
                if (keys.Count == 0) return;
                float f = RangeFactor(s);
                if (Mathf.Abs(f - 1f) < 1e-4f) return;
                foreach (var d in keys) { float v = cache[d]; cache[d] = v * f; if (scaleLogs < 20) { scaleLogs++; Log($"{s.name}: torpedo range {v:0} m x {f:0.000} = {v * f:0} m{(InBattle ? "" : " (designer)")}"); } }
            }
            catch (Exception e) { if (errors++ < 5) Log("range cache: " + e.Message); }
        }

        // ------------------------------------------------------------------ change
        static void Apply(Ship s, St st, int set, string why, bool manual)
        {
            if (set == st.Cur) { st.Pending = -1; return; }
            int old = st.Cur; st.Cur = set; st.Pending = -1; st.LastChange = Time.time;
            float secs = Mathf.Max(0f, ChangeSeconds != null ? ChangeSeconds.Value : 60f);
            int tubes = 0; string ammoNote = "";
            try
            {
                var parts = s.torpedoesAll;
                if (parts != null) for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i]; if (p == null) continue; tubes++;
                    int before = -1; try { before = s.GetTotalAmmo(p.data); } catch { }
                    var r = p.reloading;
                    if (r != null) { float left = 0f; try { left = r.isDone ? 0f : r.leftTime; } catch { } if (left < secs) r.Run(secs); }
                    if (i == 0 && before >= 0 && calLogs < 60) ammoNote = $", torpedoes on board {before}";
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tube reset: " + e.Message); }
            st.ReadyAt = Time.time + secs;
            st.NeedRecalc = true;   // the weapon range cache is refreshed by the tick, outside any game loop
            float rng = 0f; try { rng = BattleAi.TorpedoRange(s) / Mathf.Max(0.01f, st.P.R[old]) * st.P.R[set]; } catch { }
            float kn = 0f; try { kn = s.TorpedoSpeed(false) / 0.5144f; } catch { }   // Ship.TorpedoSpeed is in m/s
            if (logs++ < 200) Log($"{s.name}: torpedo setting {Name(old)} -> {Name(set)} ({(manual ? "ordered" : "chosen")}: {why}); range {rng:0} m, speed {kn:0.0}, {tubes} tube(s) ready in {secs:0} s{ammoNote}");
            Say(s, set, rng, kn, why, manual);
        }

        static void Say(Ship s, int set, float rng, float kn, string why, bool manual)
        {
            try
            {
                var div = s.division; IntPtr dp = div != null ? div.Pointer : s.Pointer;
                float now = Time.time;
                if (divSaid.TryGetValue(dp, out var last) && now - last < 60f) return;
                divSaid[dp] = now;
                // only the ships of the division already on this setting are named with her (each ship changes on its own)
                int others = 0; try { if (div != null && div.ships != null) for (int i = 0; i < div.ships.Count; i++) { var x = div.ships[i]; if (x == null || x.Pointer == s.Pointer) continue; if (states.TryGetValue(x.Pointer, out var o) && o.Cur == set) others++; } } catch { }
                Dispatch.TorpedoSetting(s.name, set, rng, kn, others, manual);
            }
            catch { }
        }

        // ------------------------------------------------------------------ choice (AI and the player's ships on Auto)
        internal static void Tick()
        {
            if (!Enabled || !InBattle) return;
            try
            {
                ResetIfNewBattle();
                MeasureTracks();
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null) return;
                var ships = cb.Ships; if (ships == null) return;
                float now = Time.time;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    bool ok = false; try { ok = s.haveTorpedoes && !s.isDead && !s.isSinking; } catch { }
                    if (!ok) continue;
                    var st = Get(s);
                    if (st.NeedRecalc) { st.NeedRecalc = false; try { s.RecalculateWeaponRanges(); } catch { } }
                    if (st.FiredSince) { st.FiredSince = false; if (st.Pending >= 0 && st.Pending != st.Cur) { int w = st.Pending; Apply(s, st, w, (st.PendingWhy ?? "") + " - after the salvo", false); continue; } }
                    if (now < st.NextThink) continue;
                    st.NextThink = now + 1f + 0.05f * (i % 10);
                    Think(s, st, now);
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        static int Manual(Ship s)
        {
            if (shipManual.TryGetValue(s.Pointer, out var m)) return m;
            try { var d = s.division; if (d != null && divManual.TryGetValue(d.Pointer, out var dm)) return dm; } catch { }
            return -1;
        }
        static bool IsPlayer(Ship s) { try { return s.player != null && s.player.isMain && !s.isAiControlled; } catch { return false; } }

        static void Think(Ship s, St st, float now)
        {
            if (st.P.N <= 1) return;
            int man = IsPlayer(s) ? Manual(s) : -1;
            if (man >= 0)
            {
                if (!Has(st, man)) man = LR;
                if (man != st.Cur) Apply(s, st, man, "the captain's order", true);
                return;
            }
            Ship t = null; try { t = ShipTick.TorpedoTarget(s); } catch { }
            if (t == null) t = Nearest(s);
            if (t == null) return;
            float lrRange = BattleAi.TorpedoRange(s) / Mathf.Max(0.01f, st.P.R[st.Cur]);
            // Ship.TorpedoSpeed is the torpedo speed in m/s; the battle runs torpedoes and ships in world units, so the run to the
            // intercept point uses the game's own conversion (Ship.ModifySpeedTorpedo, the speed the game hands to Torpedo.Create)
            float ms = 0f; try { ms = s.TorpedoSpeed(false) / Mathf.Max(0.01f, st.P.V[st.Cur]); } catch { }
            if (ms <= 0.5f) return;
            if (st.BaseKn <= 0f) st.BaseKn = ms / 0.5144f;
            float m = Mathf.Clamp(Margin != null ? Margin.Value : 0.25f, 0f, 0.6f);
            // fastest setting whose intercept run fits its range less the margin
            int want = -1; string why = null; float d = BattleAi.Dist(s, t);
            int[] order = { HS, LR, ER };
            bool line = false; try { line = BattleAi.IsCapital(t); } catch { }
            foreach (var set in order)
            {
                if (!Has(st, set)) continue;
                if (set == ER && !line) continue;   // extreme range only into a battle line (test 10:16, 1916: Extreme Range against a destroyer)
                float rng = lrRange * st.P.R[set];
                float run = InterceptRun(s, t, World(ms * st.P.V[set]));
                // hysteresis: the shorter setting than the present one must fit with a further 10 % to spare
                float lim = rng * (1f - m) * (set < st.Cur ? 0.9f : 1f);
                if (run > 0f && run <= lim) { want = set; why = $"{Dispatch.Ship(t.name)} at {d:0} m, run to the intercept {run:0} m within {lim:0} m of the {Name(set)} setting"; break; }
            }
            if (want < 0)
            {
                want = Has(st, ER) && line ? ER : LR;
                why = $"{Dispatch.Ship(t.name)} at {d:0} m beyond the shorter settings{(want == ER ? " - a battle line, extreme range" : "")}";
            }
            // hysteresis the other way (test 10:16, 1905: Long Range <-> Extreme Range every 90 s around the limit): a slower setting
            // is taken only when the present one no longer reaches the intercept point with half the allowance to spare
            if (want > st.Cur && Has(st, st.Cur))
            {
                float curRun = InterceptRun(s, t, World(ms * st.P.V[st.Cur]));
                if (curRun > 0f && curRun <= lrRange * st.P.R[st.Cur] * (1f - m * 0.5f)) want = st.Cur;
            }
            if (want == st.Cur) { st.Want = -1; st.Pending = -1; return; }
            // the choice must stand for 5 s, and settings are not changed more often than every 90 s
            if (want != st.Want) { st.Want = want; st.WantSince = now; return; }
            if (now - st.WantSince < 5f || now - st.LastChange < 90f) return;
            // a ship with loaded tubes and a possible shot on the present setting fires first
            if (LoadedShare(s) > 0f && ShotPossible(s, st, t, lrRange, ms, m))
            {
                if (st.Pending != want) { st.Pending = want; st.PendingWhy = why; if (logs++ < 200) Log($"{s.name}: {Name(want)} wanted ({why}) - fires the loaded tubes on {Name(st.Cur)} first"); }
                return;
            }
            Apply(s, st, want, why, false);
        }
        static float World(float ms) { try { float w = Ship.ModifySpeedTorpedo(ms); if (w > 0f) return w; } catch { } return ms * 3.2f; }
        static bool ShotPossible(Ship s, St st, Ship t, float lrRange, float ms, float m)
        {
            float run = InterceptRun(s, t, World(ms * st.P.V[st.Cur]));
            return run > 0f && run <= lrRange * st.P.R[st.Cur];
        }
        static float LoadedShare(Ship s)
        {
            int n = 0, l = 0;
            try { var parts = s.torpedoesAll; if (parts != null) for (int i = 0; i < parts.Count; i++) { var p = parts[i]; if (p == null) continue; n++; var r = p.reloading; if (r == null || r.isDone) l++; } } catch { return 0f; }
            return n > 0 ? (float)l / n : 0f;
        }
        static Ship Nearest(Ship s)
        {
            Ship best = null; float bd = float.MaxValue;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || s.player == null) return null;
                var en = b.ShipsOf(b.OppositePlayer(s.player), true); if (en == null) return null;
                for (int i = 0; i < en.Count; i++) { var e = en[i]; if (e == null || e.isDead || e.isSinking) continue; float d = BattleAi.Dist(s, e); if (d < bd) { bd = d; best = e; } }
            }
            catch { }
            return best;
        }
        // length of the torpedo run (m) to the point where it meets the target moving straight on; -1 when it cannot catch it
        static float InterceptRun(Ship s, Ship t, float vt)
        {
            var P = t.transform.position - s.transform.position; P.y = 0f;
            Vector3 V = Vector3.zero; try { V = t.velocityCurrent; V.y = 0f; } catch { }
            float vmax = BattleAi.Speed(t) * 1.15f; if (vmax > 0f && V.magnitude > vmax) V = V.normalized * vmax;
            float a = V.sqrMagnitude - vt * vt, b = 2f * Vector3.Dot(P, V), c = P.sqrMagnitude, T = -1f;
            if (Mathf.Abs(a) < 1e-3f) { if (b < 0f) T = -c / b; }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f) { float sq = Mathf.Sqrt(disc), t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a); T = t1 > 0f && (t1 < t2 || t2 <= 0f) ? t1 : t2; }
            }
            return T > 0f ? vt * T : -1f;
        }

        // ------------------------------------------------------------------ calibration log: what the game launches
        class Track { public Torpedo T; public Vector3 P; public float At; public string Ship; public float Speed, Dur, Range; public int Set; }
        static readonly List<Track> tracks = new List<Track>();
        internal static void Launched(Part from, float duration, float speed, Torpedo t)
        {
            if (!Enabled) return;
            try
            {
                var s = from != null ? from.ship : null; if (s == null) return;
                var st = Get(s); float rng = BattleAi.TorpedoRange(s);
                if (calLogs < 80) { calLogs++; { float tsp = s.TorpedoSpeed(false); Log($"launch {s.name}: setting {Name(st.Cur)}, game speed argument {speed:0.00}, duration argument {duration:0.0} s, speed x duration {speed * duration:0}, ship torpedo range {rng:0} m, TorpedoSpeed {tsp:0.0} m/s = {tsp / 0.5144f:0.0} kn, ModifySpeedTorpedo {World(tsp):0.00}"); } }
                if (t != null && measLogs < 40 && tracks.Count < 20) { var p = t.transform.position; p.y = 0f; tracks.Add(new Track { T = t, P = p, At = Time.time, Ship = s.name, Speed = speed, Dur = duration, Range = rng, Set = st.Cur }); }
                st.FiredSince = true;   // a wanted change follows at the next tick
            }
            catch (Exception e) { if (errors++ < 5) Log("launch: " + e.Message); }
        }
        static void MeasureTracks()
        {
            if (tracks.Count == 0) return;
            float now = Time.time;
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                var k = tracks[i]; if (now - k.At < 4f) continue;
                try
                {
                    var p = k.T.transform.position; p.y = 0f; float ms = (p - k.P).magnitude / (now - k.At);
                    if (measLogs++ < 40) Log($"track {k.Ship} ({Name(k.Set)}): measured {ms:0.00} world units/s; game speed argument {k.Speed:0.00}; run at that speed for the duration {ms * k.Dur:0} m vs ship torpedo range {k.Range:0} m");
                }
                catch { }
                tracks.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ player panel
        static GUIStyle sT, sB; static bool folded; static int gErr;
        // small button style shared by the compact battle strips (torpedo setting, torpedo targets)
        // (the Il2Cpp GUIStyle has no copy constructor: the skin's button is used with a smaller font and padding while a strip is drawn)
        static int smallDepth, oldFont; static int[] oldPad = new int[4];
        internal static GUIStyle Small() { return GUI.skin.button; }
        internal static void BeginSmall()
        {
            if (smallDepth++ > 0) return;
            var b = GUI.skin.button; oldFont = b.fontSize; b.fontSize = 11;
            var p = b.padding; oldPad[0] = p.left; oldPad[1] = p.right; oldPad[2] = p.top; oldPad[3] = p.bottom; p.left = 2; p.right = 2; p.top = 1; p.bottom = 1;
        }
        internal static void EndSmall()
        {
            if (smallDepth == 0 || --smallDepth > 0) return;
            var b = GUI.skin.button; b.fontSize = oldFont;
            var p = b.padding; p.left = oldPad[0]; p.right = oldPad[1]; p.top = oldPad[2]; p.bottom = oldPad[3];
        }
        internal static void OnGUI()
        {
            if (!Enabled || !InBattle || BattleScreen.Hidden) return;
            try
            {
                TorpedoSettings.BeginSmall();
                Ship s = BattleSel.Ship(); // 1.11.261: kept after a click on a strip
                if (s == null || !IsPlayer(s) || s.isDead || s.isSinking || !s.haveTorpedoes) return;
                var st = Get(s);
                if (sT == null) { sT = new GUIStyle(); sT.fontSize = 12; sT.richText = true; sT.normal.textColor = new Color(0.9f, 0.9f, 0.9f); }
                // 1.11.255 (author 25.10, 12:36): one compact row - smaller buttons packed closer, the state in a short text after them
                float w = 600f, h = 24f, x = (UiScale.W - w) / 2f, y = 54f + 62f;
                int cur = st.Pending >= 0 ? st.Pending : st.Cur;
                if (folded) { if (GUI.Button(new Rect(x + w - 150f, y, 150f, 18f), $"Torpedoes: {Name(cur)} ▾", Small())) folded = false; return; }
                GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
                float rng = 0f, kn = 0f; try { rng = BattleAi.TorpedoRange(s); kn = s.TorpedoSpeed(false) / 0.5144f; } catch { }
                int man = Manual(s);
                string mode = man < 0 ? "auto" : (shipManual.ContainsKey(s.Pointer) ? "ship" : "division");
                string ready = st.ReadyAt > Time.time ? $", reset {st.ReadyAt - Time.time:0} s" : "";
                string avail = st.P.N == 1 ? ", one setting" : "";
                GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), "<b>Torp. setting</b>", sT);
                string[] lab = { "Auto", "Fast", "Long", "Extreme" }; // 1.11.265: short labels
                const float bw = 56f; float bx = x + 98f;
                for (int i = 0; i < 4; i++)
                {
                    int set = i - 1;
                    bool can = set < 0 || Has(st, set);
                    var r = new Rect(bx + i * (bw + 2f), y + 3f, bw, 18f);
                    bool active = set == man;
                    var oldBg = GUI.backgroundColor; var oldEn = GUI.enabled;
                    if (active) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                    GUI.enabled = can;
                    if (GUI.Button(r, lab[i], Small())) Order(s, set, Event.current != null && Event.current.shift);
                    GUI.enabled = oldEn; GUI.backgroundColor = oldBg;
                }
                float tx = bx + 4f * (bw + 2f) + 4f;
                GUI.Label(new Rect(tx, y + 4f, x + w - 24f - tx, 18f), $"{rng / 1000f:0.0} km {kn:0} kn <color=#C0C0C0>{mode}{ready}{avail}</color>", sT);
                if (GUI.Button(new Rect(x + w - 21f, y + 3f, 18f, 18f), "▴", Small())) folded = true;
            }
            catch (Exception e) { if (gErr++ < 5) MelonLogger.Warning("[DIP GG Fixes][TorpSet] panel: " + e.Message); }
            finally { TorpedoSettings.EndSmall(); }
        }
        static void Order(Ship s, int set, bool shipOnly)
        {
            var div = s.division;
            if (shipOnly || div == null) { if (set < 0) shipManual.Remove(s.Pointer); else shipManual[s.Pointer] = set; }
            else
            {
                if (set < 0) divManual.Remove(div.Pointer); else divManual[div.Pointer] = set;
                try { if (div.ships != null) for (int i = 0; i < div.ships.Count; i++) { var x = div.ships[i]; if (x != null) shipManual.Remove(x.Pointer); } } catch { }
            }
            Log($"{s.name}: torpedo setting order {Name(set)} for {(shipOnly || div == null ? "the ship" : "the division")}");
            // the order applies at the next decision (within a second)
            try { var st = Get(s); st.NextThink = 0f; if (div != null && div.ships != null) for (int i = 0; i < div.ships.Count; i++) { var x = div.ships[i]; if (x != null && states.TryGetValue(x.Pointer, out var o)) o.NextThink = 0f; } } catch { }
        }
    }

    // ---- patches
    [HarmonyPatch]
    internal static class Patch_TorpSet_Speed
    {
        static MethodBase TargetMethod() { try { return AccessTools.GetDeclaredMethods(typeof(Ship)).FirstOrDefault(m => m.Name == "TorpedoSpeed" && !m.IsStatic && m.GetParameters().Length == 1); } catch { return null; } }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Ship __instance, ref float __result) { try { if (__result > 0f) __result *= TorpedoSettings.SpeedFactor(__instance); } catch { } }
    }
    // the torpedo entries of the ship's weapon range cache are scaled after the game fills it (a patch on the static
    // Part.WeaponRange with its nullable shell-type argument crashed the game while the ships were spawned)
    [HarmonyPatch]
    internal static class Patch_TorpSet_Range
    {
        static MethodBase TargetMethod() { try { return AccessTools.GetDeclaredMethods(typeof(Ship)).FirstOrDefault(m => m.Name == "RecalculateWeaponRanges" && m.GetParameters().Length == 0); } catch { return null; } }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Ship __instance) { TorpedoSettings.ScaleCache(__instance); }
    }
    [HarmonyPatch]
    internal static class Patch_TorpSet_Launch
    {
        static MethodBase TargetMethod() { try { return AccessTools.GetDeclaredMethods(typeof(Torpedo)).FirstOrDefault(m => m.Name == "Create" && m.GetParameters().Length == 5); } catch { return null; } }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Part __0, float __3, float __4, Torpedo __result) { TorpedoSettings.Launched(__0, __3, __4, __result); }
    }
}
