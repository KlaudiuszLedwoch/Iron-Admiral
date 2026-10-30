// 1.11.58: fog of war.
// Historical basis (sources in the project notes): dead-reckoning positions of fleets differed by 10-12 nm after a day at sea
// (Jutland), contact reports put the enemy 10-20 nm (Tsushima) to ~100 nm (Bismarck) off, airship and aircraft reports
// misidentified ship types in about half the cases (19 Aug 1916: 4 of 7 Zeppelin reports wrong; Coral Sea: both sides struck
// misidentified targets), and in battle ships were detected long before they were identified (Falklands, River Plate,
// Denmark Strait, Samar), at night only at a few thousand yards (Jutland night action, Guadalcanal).
//
// Three layers, all switchable in the F10 panel:
//  1. Strategic map: every foreign (non-allied) task force gets an intelligence quality Q (0-1) for the player, recomputed
//     monthly: era (wireless, direction finding, aircraft, radar), the observer's reconnaissance technology relative to the
//     other great powers (average Recon of its ships), intelligence budget relative to the other great powers, distance from
//     the observer's nearest port, own or allied task forces nearby (stationary = patrol, larger radius; light ships = scouts),
//     size of the enemy force, a monthly regional weather roll. Q < hidden threshold: the force is not shown at all;
//     below the exact threshold: position shifted, route hidden, composition in the group window reported with errors.
//  2. Battle decision window: the enemy side is shown "as reported": counts per reported type, misidentified types and
//     unreported ships, depending on era, scouts (light ships) in the player's own force, technology and budget.
//  3. Battle: an enemy ship that is detected is not yet identified. Until an own ship closes to identification range
//     (a share of the game's own detection range, capped by ship size, daylight, weather and smoke) the player sees only
//     "Unidentified ship (large/medium/small)" or a probable - possibly wrong - type, with no name, class icon or details
//     (ship list, overhead label, info panel, division tooltip). The AI judges the enemy by the same picture (strength
//     estimate for massed attacks and withdrawals, torpedo targets).
//  4. Interceptions: a battle that needs the attacker to find a force at sea can fail when the attacker's intelligence on
//     that force is poor (MissionGenerator.CanParticipateInBattleByRecon) - for the player and the AI alike.
// No state is saved: all rolls are deterministic hashes of month, ids and battle.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DipGgFixes
{
    internal static class Fog
    {
        internal static MelonPreferences_Entry<bool> On, MapOn, ReportOn, TacOn, AiOn, InterceptOn;
        internal static MelonPreferences_Entry<float> Strength;
        internal static MelonPreferences_Entry<bool> StaleContacts, NightFactors, HideEnemyDetails;
        static readonly Dictionary<IntPtr, float> lastShot = new Dictionary<IntPtr, float>();
        const float HiddenQ = 0.35f, ExactQ = 0.70f;
        static int errors;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("FogOfWar", true, null, "Fog of war: enemy fleets, contact reports and ships in battle are known only as well as reconnaissance allows (master switch for the options below)");
            MapOn = cat.CreateEntry("FogStrategicMap", true, null, "Strategic map: foreign task forces are hidden or shown with a position error and unknown route, depending on era, distance from own ports, own fleets and scouts nearby, technology and intelligence budget");
            ReportOn = cat.CreateEntry("FogBattleReports", true, null, "Battle decision window and enemy group window show the enemy as reported: misidentified types and unreported ships are possible, fewer with scouts and better technology");
            HideEnemyDetails = cat.CreateEntry("FogHideEnemyDetails", false, null, "Battle: enemy ships never show their details - an identified enemy shows only its name and type, no armament, armour, crew or damage picture (1.11.119)");
            TacOn = cat.CreateEntry("FogTacticalIdentification", true, null, "Battle: detected enemy ships stay unidentified (no name, class or details) until an own ship closes to identification range - shorter at night, in bad weather and behind smoke");
            AiOn = cat.CreateEntry("FogAiPerception", true, null, "The AI judges the enemy in battle by the same uncertain picture (strength estimate for massed attacks and withdrawals, torpedo targets)");
            InterceptOn = cat.CreateEntry("FogInterceptions", true, null, "A battle at sea needs the attacker to find the enemy force: poor intelligence on that force can prevent the battle (player and AI alike)");
            StaleContacts = cat.CreateEntry("FogStaleContacts", true, null, "Strategic map: an inexactly known enemy force stays marked where it was last reported; the enemy group window tells how old the report is and the course seen");
            NightFactors = cat.CreateEntry("FogNightFactors", true, null, "Battle at night or in poor visibility: ships firing their guns and burning ships are identified from further away; Japanese lookouts identify at night from further away (night optics)");
            Strength = cat.CreateEntry("FogStrength", 1.0f, null, "Strength of the fog of war: 1 = historical calibration, 0 = no fog, values above 1 make reconnaissance harder");
        }
        static bool Act(MelonPreferences_Entry<bool> e) { return On != null && On.Value && e != null && e.Value && Strength.Value > 0.001f; }
        internal static bool MapActive { get { return Act(MapOn); } }
        internal static bool ReportActive { get { return Act(ReportOn); } }
        internal static bool TacActive { get { return Act(TacOn); } }
        internal static bool AiActive { get { return Act(AiOn) && Act(TacOn); } }
        internal static bool InterceptActive { get { return Act(InterceptOn); } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Fog] " + m); }
        static void Err(string where, Exception e) { if (errors++ < 12) MelonLogger.Warning("[DIP GG Fixes][Fog] " + where + ": " + e.Message); }

        // ---------------------------------------------------------------- helpers
        internal static uint Hash(string s) { uint h = 2166136261; for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; } h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; }
        internal static float Roll(string s) { return (Hash(s) & 0xFFFFFF) / 16777216f; }
        static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
        static float Fogged(float q) { return Clamp01(1f - (1f - q) * Strength.Value); }
        internal static int Month() { return Independence_Month(); }
        static int Independence_Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return 0; } }
        internal static int Year() { try { return CampaignController.Instance.CurrentDate.AsDate().Year; } catch { return 1910; } }
        static Player Main()
        {
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) if (ps[i] != null && ps[i].isMain) return ps[i]; } catch { }
            return null;
        }
        static string PName(Player p) { try { return p.data.name; } catch { return p.Pointer.ToString(); } }
        static string TypeName(Ship s) { try { var t = s.shipType; return t != null ? t.name : "?"; } catch { return "?"; } }
        static readonly string[] Scale = { "tb", "dd", "cl", "ca", "bc", "bb" };
        static int ScaleIdx(string t) { return Array.IndexOf(Scale, t); }
        static readonly Dictionary<string, float> Typical = new Dictionary<string, float> { { "tb", 1f }, { "dd", 1.6f }, { "cl", 4.5f }, { "ca", 9f }, { "bc", 20f }, { "bb", 22f } };
        internal static string FullName(string t)
        {
            switch (t) { case "bb": return "battleship"; case "bc": return "battlecruiser"; case "ca": return "heavy cruiser"; case "cl": return "light cruiser"; case "dd": return "destroyer"; case "tb": return "torpedo boat"; case "tr": return "transport"; case "ss": return "submarine"; default: return "warship"; }
        }
        static string Plural(string t) { var n = FullName(t); return n.EndsWith("s") ? n + "es" : n + "s"; }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }
        internal static string SizeClass(string t) { switch (t) { case "bb": case "bc": return "large"; case "ca": case "cl": return "medium"; default: return "small"; } }
        // a reported type: wrong with probability p (one step up or down the size scale, two steps when the picture is very poor)
        // a reported type: wrong with probability p (one step up or down the size scale, two steps when the picture is very poor).
        // 1.11.59: only into types the owner's navy actually has - a navy without battlecruisers is never reported with one.
        internal static string Misreport(string actual, float p, string key, Player owner)
        {
            int i = ScaleIdx(actual); if (i < 0 || Roll(key + "|m") >= p) return actual;
            var have = NavyTypes(owner);
            int step = Roll(key + "|2") < (p > 0.45f ? 0.3f : 0.1f) ? 2 : 1;
            int dir = Roll(key + "|d") < 0.55f ? 1 : -1; // reports erred on the side of overestimating slightly more often
            foreach (int d in new[] { dir, -dir })
            {
                // the step-th type the navy has in that direction, or the nearest one if it has fewer
                int found = -1, n = 0;
                for (int j = i + d; j >= 0 && j < Scale.Length; j += d)
                {
                    if (have != null && !have.Contains(Scale[j])) continue;
                    found = j; n++; if (n >= step) break;
                }
                if (found >= 0) return Scale[found];
            }
            return actual;
        }
        static int navyMonth = -1; static readonly Dictionary<IntPtr, HashSet<string>> navy = new Dictionary<IntPtr, HashSet<string>>();
        static HashSet<string> NavyTypes(Player owner)
        {
            if (owner == null) return null;
            int m = Month(); if (m != navyMonth) { navyMonth = m; navy.Clear(); }
            HashSet<string> set; if (navy.TryGetValue(owner.Pointer, out set)) return set;
            set = new HashSet<string>();
            try
            {
                var ships = CampaignController.Instance.CampaignData.GetShips;
                for (int k = 0; ships != null && k < ships.Count; k++)
                {
                    var s = ships[k]; if (s == null || s.player == null || s.player.Pointer != owner.Pointer) continue;
                    bool ok = false; try { ok = !s.isSunk && !s.isScrapped && !s.isBuilding; } catch { }
                    if (ok) set.Add(TypeName(s));
                }
            }
            catch { }
            // ships of that navy present in the current battle count as well (temporary battle ships, custom battles)
            try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) { var l = b.Ships; for (int k = 0; l != null && k < l.Count; k++) { var s = l[k]; if (s != null && s.player != null && s.player.Pointer == owner.Pointer) set.Add(TypeName(s)); } } } catch { }
            navy[owner.Pointer] = set; return set;
        }
        internal static float Era(int y)
        {
            int[] ys = { 1890, 1905, 1914, 1918, 1930, 1939, 1942, 1945, 1950 };
            float[] v = { 0.15f, 0.20f, 0.25f, 0.30f, 0.34f, 0.40f, 0.48f, 0.55f, 0.58f };
            if (y <= ys[0]) return v[0];
            for (int i = 1; i < ys.Length; i++) if (y <= ys[i]) return v[i - 1] + (v[i] - v[i - 1]) * (y - ys[i - 1]) / (float)(ys[i] - ys[i - 1]);
            return v[v.Length - 1];
        }

        // ---------------------------------------------------------------- relations (cached per month)
        static int relMonth = -1; static readonly Dictionary<long, bool> friendly = new Dictionary<long, bool>();
        internal static bool Friendly(Player a, Player b)
        {
            if (a == null || b == null) return false; if (a.Pointer == b.Pointer) return true;
            int m = Month(); if (m != relMonth) { relMonth = m; friendly.Clear(); }
            long k = (long)a.Pointer ^ ((long)b.Pointer << 1); bool f;
            if (friendly.TryGetValue(k, out f)) return f;
            f = false;
            try { foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.isAlliance && r.AnyOnePlayerIs(a) && r.AnyOnePlayerIs(b)) { f = true; break; } } } catch { }
            friendly[k] = f; return f;
        }

        // ---------------------------------------------------------------- observer factors (monthly)
        static int facMonth = -1; static readonly Dictionary<IntPtr, float> techF = new Dictionary<IntPtr, float>(), budgetF = new Dictionary<IntPtr, float>();
        static void Factors()
        {
            int m = Month(); if (m == facMonth) return; facMonth = m; techF.Clear(); budgetF.Clear();
            try
            {
                var cd = CampaignController.Instance.CampaignData; var majors = cd.PlayersMajor;
                var rec = new Dictionary<IntPtr, float>(); var bud = new Dictionary<IntPtr, float>();
                float rs = 0f, bs = 0f; int rn = 0, bn = 0;
                var ships = cd.GetShips;
                var sum = new Dictionary<IntPtr, float>(); var cnt = new Dictionary<IntPtr, int>();
                for (int i = 0; ships != null && i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null || s.player == null) continue;
                    bool ok = false; try { ok = !s.isSunk && !s.isScrapped && !s.isBuilding; } catch { }
                    if (!ok) continue;
                    float r = 0f; try { r = s.Recon(); } catch { }
                    if (r <= 0f || float.IsNaN(r)) continue;
                    var p = s.player.Pointer; float a; sum.TryGetValue(p, out a); sum[p] = a + r; int c; cnt.TryGetValue(p, out c); cnt[p] = c + 1;
                }
                for (int i = 0; i < majors.Count; i++)
                {
                    var p = majors[i]; if (p == null) continue;
                    float a; int c; if (sum.TryGetValue(p.Pointer, out a) && cnt.TryGetValue(p.Pointer, out c) && c > 0) { rec[p.Pointer] = a / c; rs += a / c; rn++; }
                    float b = 0f; try { b = p.intelBudget; } catch { }
                    bud[p.Pointer] = b; bs += b; bn++;
                }
                float rm = rn > 0 ? rs / rn : 0f, bm = bn > 0 ? bs / bn : 0f;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < majors.Count; i++)
                {
                    var p = majors[i]; if (p == null) continue;
                    float r; float tf = 1f; if (rm > 0f && rec.TryGetValue(p.Pointer, out r)) tf = Mathf.Clamp(1f + 0.3f * (r / rm - 1f), 0.8f, 1.25f);
                    float b; float bf = 1f; if (bm > 0f && bud.TryGetValue(p.Pointer, out b)) bf = Mathf.Clamp(1f + 0.2f * (b / bm - 1f), 0.85f, 1.2f);
                    techF[p.Pointer] = tf; budgetF[p.Pointer] = bf;
                    if (p.isMain || i < 14) { float rr; rec.TryGetValue(p.Pointer, out rr); float bb2; bud.TryGetValue(p.Pointer, out bb2); sb.Append($"{p.data.name}: recon {rr:0.##} (x{tf:0.00}), intel budget {bb2:0.###} (x{bf:0.00}); "); }
                }
                Log($"month {Year()}: era base {Era(Year()):0.00}; reconnaissance factors - " + sb.ToString());
            }
            catch (Exception e) { Err("factors", e); }
        }
        internal static float TechF(Player p) { Factors(); float f; return p != null && techF.TryGetValue(p.Pointer, out f) ? f : 1f; }
        internal static float BudgetF(Player p) { Factors(); float f; return p != null && budgetF.TryGetValue(p.Pointer, out f) ? f : 1f; }

        // ---------------------------------------------------------------- strategic intelligence quality
        static float KmPerUnit() { try { if (CampaignMap.UnityDistanceToWorldKmMultiplier > 0f) return CampaignMap.UnityDistanceToWorldKmMultiplier; } catch { } return 1f; }
        static int qMonth = -1; static readonly Dictionary<string, float> qCache = new Dictionary<string, float>(); static int qLogged;
        static int Light(CampaignController.TaskForce tf, out int total)
        {
            int n = 0; total = 0;
            try
            {
                var vs = tf.Vessels; for (int i = 0; vs != null && i < vs.Count; i++)
                {
                    var v = vs[i]; if (v == null) continue; total++;
                    var s = v.TryCast<Ship>(); if (s == null) continue; var t = TypeName(s);
                    if (t == "cl" || t == "dd" || t == "ca") n++; else if (t == "tb") n += 0;
                }
            }
            catch { }
            return n;
        }
        // 1.11.408 (#85): misidentification when the force was found by air patrols - their reports cannot be better than the
        // patrol type allows (airships ~45 %, seaplanes ~40 %, flying boats ~25 %, long-range patrol ~15 %, radar ~10 %), unless
        // other sources (ports, own forces) give a better picture anyway. Key as in Q; value: Q without the air term, air error rate.
        static readonly Dictionary<string, (float qNoAir, float mis)> airPic = new Dictionary<string, (float, float)>();
        static int airPLogged;
        internal static float AirP(Player obs, CampaignController.TaskForce tf, float p)
        {
            try
            {
                if (obs == null || tf == null) return p;
                if (!airPic.TryGetValue(PName(obs) + "|" + tf.Id.ToString(), out var a)) return p;
                float pNo = Mathf.Clamp(0.6f * (1f - a.qNoAir) / 0.65f, 0f, 0.6f);
                float r = Mathf.Max(p, Mathf.Min(pNo, a.mis));
                if (r > p && airPLogged++ < 6) Log($"air report: misidentification chance {p:0.00} -> {r:0.00} (air patrol error rate {a.mis:0.00})");
                return r;
            }
            catch { return p; }
        }
        internal static float Q(Player obs, CampaignController.TaskForce tf)
        {
            if (obs == null || tf == null) return 1f;
            int m = Month(); if (m != qMonth) { qMonth = m; qCache.Clear(); airPic.Clear(); qLogged = 0; }
            string key = PName(obs) + "|" + tf.Id.ToString(); float q;
            if (qCache.TryGetValue(key, out q)) return q;
            q = 1f; string why = "";
            try
            {
                var pos = tf.WorldPos; float kpu = KmPerUnit();
                float era = Era(Year()), tfac = TechF(obs), bfac = BudgetF(obs);
                float baseQ = era * tfac * bfac;
                // nearest own port
                float portKm = 99999f;
                try { float sqr = float.MaxValue; var pt = CampaignMap.PortsDb.NearestPort(pos, out sqr, obs, true); if (pt != null && sqr < float.MaxValue) portKm = Mathf.Sqrt(sqr) * kpu; } catch { }
                float port = 0.35f * Clamp01(1f - (portKm - 200f) / 1300f);
                // 1.11.407 (#85 step 1): air patrols of the Naval Aviation technologies from our ports
                string airWhy; int acx = (int)Mathf.Floor(pos.x * kpu / 800f), acz = (int)Mathf.Floor(pos.z * kpu / 800f);
                float air = NavalAviation.AirQ(obs, portKm, m, acx, acz, out airWhy);
                // own or allied forces nearby: stationary = patrol (larger radius), light ships = scouts
                float fleet = 0f; string fleetWhy = "";
                try
                {
                    var all = CampaignController.Instance.CampaignData.TaskForces;
                    for (int i = 0; all != null && i < all.Count; i++)
                    {
                        var o = all[i]; if (o == null || o.Controller == null || !Friendly(obs, o.Controller)) continue;
                        bool moving = false; try { moving = o.IsMoving(); } catch { }
                        float radius = moving ? 500f : 900f;
                        float dKm = Vector3.Distance(o.WorldPos, pos) * kpu; if (dKm > radius) continue;
                        int tot; int light = Light(o, out tot);
                        float scout = 0.35f + 0.65f * Mathf.Min(1f, light / 3f);
                        float c = 0.30f * (1f - dKm / radius) * scout;
                        if (c > fleet) { fleet = c; fleetWhy = $"{(moving ? "fleet" : "patrol")} {dKm:0} km away, {light} scouts"; }
                    }
                }
                catch { }
                int total; Light(tf, out total);
                float size = 0.04f * Mathf.Min(5f, total / 2f);
                int cx = (int)Mathf.Floor(pos.x * kpu / 800f), cz = (int)Mathf.Floor(pos.z * kpu / 800f);
                float weather = -0.15f + 0.20f * Roll($"wx|{m}|{cx}|{cz}");
                int mon = ((m - 1) % 12 + 12) % 12 + 1; if (mon == 11 || mon == 12 || mon == 1 || mon == 2) weather -= 0.05f;
                q = Fogged(Clamp01(baseQ + port + air + fleet + size + weather));
                // 1.11.408: air reports found the force but often misidentified it - remember the picture without them
                if (air > 0f) airPic[key] = (Fogged(Clamp01(baseQ + port + fleet + size + weather)), NavalAviation.MisRate(NavalAviation.ReconLevel(obs)));
                else airPic.Remove(key);
                why = $"era {era:0.00} x tech {tfac:0.00} x budget {bfac:0.00} + port {port:0.00} ({portKm:0} km){(airWhy != "" ? " + " + airWhy : "")} + forces {fleet:0.00}{(fleetWhy != "" ? " (" + fleetWhy + ")" : "")} + size {size:0.00} + weather {weather:0.00}";
            }
            catch (Exception e) { Err("Q", e); q = 1f; }
            qCache[key] = q;
            if (obs.isMain && qLogged < 12) { qLogged++; string own = "?"; try { own = tf.Controller.data.name; } catch { } int vc = 0; try { vc = tf.Vessels.Count; } catch { } Log($"intelligence on {own} force ({vc} ships): Q {q:0.00} -> {(q < HiddenQ ? "not seen" : q < ExactQ ? "inexact" : "located")} [{why}]"); }
            return q;
        }

        // 1.11.120: intelligence on a foreign port (ships lying there) - same factors as for a force at sea, without the force size:
        // era x technology x intelligence budget, own ports nearby, own or allied forces near the port, monthly weather.
        // Returns 1 when the fog is off. Thresholds: below HiddenQ nothing known, below ExactQ an estimate.
        internal static float PortQ(Player obs, Vector3 pos, string key)
        {
            if (obs == null || !MapActive) return 1f;
            int m = Month(); if (m != qMonth) { qMonth = m; qCache.Clear(); qLogged = 0; }
            string ck = "port|" + PName(obs) + "|" + key; float q;
            if (qCache.TryGetValue(ck, out q)) return q;
            q = 1f;
            try
            {
                float kpu = KmPerUnit();
                float baseQ = Era(Year()) * TechF(obs) * BudgetF(obs);
                float portKm = 99999f;
                try { float sqr = float.MaxValue; var pt = CampaignMap.PortsDb.NearestPort(pos, out sqr, obs, true); if (pt != null && sqr < float.MaxValue) portKm = Mathf.Sqrt(sqr) * kpu; } catch { }
                float port = 0.35f * Clamp01(1f - (portKm - 200f) / 1300f);
                float fleet = 0f;
                try
                {
                    var all = CampaignController.Instance.CampaignData.TaskForces;
                    for (int i = 0; all != null && i < all.Count; i++)
                    {
                        var o = all[i]; if (o == null || o.Controller == null || !Friendly(obs, o.Controller)) continue;
                        float dKm = Vector3.Distance(o.WorldPos, pos) * kpu; if (dKm > 700f) continue;
                        int tot; int light = Light(o, out tot);
                        float c = 0.30f * (1f - dKm / 700f) * (0.35f + 0.65f * Mathf.Min(1f, light / 3f));
                        if (c > fleet) fleet = c;
                    }
                }
                catch { }
                int cx = (int)Mathf.Floor(pos.x * kpu / 800f), cz = (int)Mathf.Floor(pos.z * kpu / 800f);
                float weather = -0.15f + 0.20f * Roll($"wx|{m}|{cx}|{cz}");
                q = Fogged(Clamp01(baseQ + port + fleet + weather));
            }
            catch (Exception e) { Err("PortQ", e); q = 1f; }
            qCache[ck] = q; return q;
        }
        internal static float HiddenLimit => HiddenQ;
        internal static float ExactLimit => ExactQ;

        // ---------------------------------------------------------------- strategic map (player view)
        enum Mode { Normal, Hidden, Blurred }
        class MapState { public Mode mode; public Vector3 offset; public CampaignController.TaskForce tf; public float q; public bool saved, elActive, routeActive; public bool hasSeen; public Vector3 seenPos; public int seenMonth = -1, checkedMonth = -1; public string seenHeading; }
        static readonly Dictionary<IntPtr, MapState> mapStates = new Dictionary<IntPtr, MapState>();
        static float nextMap; static int mapMonth = -1; static MapUI mapUi;
        static void MapRecompute(MapUI ui)
        {
            mapUi = ui; var main = Main(); if (main == null) return;
            int m = Month(); bool newMonth = m != mapMonth; mapMonth = m;
            int hidden = 0, blurred = 0, exact = 0;
            var seen = new HashSet<IntPtr>();
            try
            {
                foreach (var kv in ui.shipGroups)
                {
                    var tf = kv.Key; var el = kv.Value; if (tf == null || el == null) continue;
                    seen.Add(el.Pointer);
                    MapState st; if (!mapStates.TryGetValue(el.Pointer, out st)) { st = new MapState(); mapStates[el.Pointer] = st; }
                    var prev = st.mode; st.tf = tf;
                    if (!MapActive || tf.Controller == null || Friendly(main, tf.Controller)) { st.mode = Mode.Normal; Restore(el, st); continue; }
                    float q = Q(main, tf); st.q = q; Save(el, st);
                    if (q < HiddenQ) { st.mode = Mode.Hidden; hidden++; }
                    else if (q < ExactQ)
                    {
                        st.mode = Mode.Blurred; blurred++;
                        float r = 0f; try { r = tf.GetZoneRadius(false); } catch { }
                        if (r <= 0f) r = 300f / KmPerUnit();
                        float mag = (ExactQ - q) / (ExactQ - HiddenQ) * 180f / KmPerUnit() * Mathf.Sqrt(Roll($"off|{m}|{tf.Id}")); // 1.11.70: at most 180 km (the zone radius gave 500-2600 km)
                        // 1.11.64: the error runs along the force's own sea route (ahead of or behind it), so the marker never
                        // lands on land; a force with no route is shown where it is (still without its route line)
                        // 1.11.74: contact reports age (Task Force 1942 style) - the marker stays where the force was last reported;
                        // each month the force is re-sighted with a chance that grows with intelligence quality
                        var cur = tf.WorldPos;
                        if (!st.hasSeen || (st.checkedMonth != m && Roll($"sight|{m}|{tf.Id}") < Mathf.Clamp((q - HiddenQ) / (ExactQ - HiddenQ), 0.2f, 0.9f)))
                        { st.hasSeen = true; st.seenPos = cur; st.seenMonth = m; st.seenHeading = Heading(tf); }
                        st.checkedMonth = m;
                        if (!StaleContacts.Value) { st.seenPos = cur; st.seenMonth = m; }
                        bool stale = st.seenMonth != m && (st.seenPos - cur).sqrMagnitude > 1e-4f;
                        st.offset = (st.seenPos - cur) + SeaOffset(tf, mag, m, st.seenPos, !stale);
                    }
                    else { st.mode = Mode.Normal; exact++; Restore(el, st); }
                }
            }
            catch (Exception e) { Err("map", e); }
            var gone = new List<IntPtr>(); foreach (var k in mapStates.Keys) if (!seen.Contains(k)) gone.Add(k); foreach (var k in gone) mapStates.Remove(k);
            if (newMonth && (hidden + blurred + exact) > 0) Log($"strategic map: foreign forces - {hidden} not seen, {blurred} inexact, {exact} located");
        }
        // 1.11.65: any direction (moving or stationary force), checked with the game's own land test (CampaignMap.RaycastLand):
        // the straight line from the force to the shown position must not cross land. Up to 12 tries with other directions and
        // a shorter distance, then the force's sea route, then the true position.
        // 1.11.71: the shown position itself must be sea (zero-length land test at the target). A force in port or by the coast
        // fails the line test from its own position, so for it only the target is tested. The route fallback is checked the same
        // way (a route ends in a port, which is land); if nothing passes, the force is shown where it is.
        static int seaLogged; static int seaDirect, seaRoute, seaTrue, seaMonth = -1;
        // 1.11.84: the monthly summary counts each force once (the placement is recomputed every frame; 1.11.65-1.11.83 counted calls)
        static readonly HashSet<string> seaSeen = new HashSet<string>();
        static bool First(CampaignController.TaskForce tf, string kind) { try { return seaSeen.Add(kind + "|" + tf.Id.ToString()); } catch { return false; } }
        static bool PointLand(CampaignMap cm, Vector3 p) { try { return cm.RaycastLand(p, p + new Vector3(0.01f, 0f, 0.01f)); } catch { return true; } }
        static Vector3 SeaOffset(CampaignController.TaskForce tf, float mag, int m, Vector3 pos, bool allowRoute)
        {
            if (m != seaMonth) { if (seaMonth >= 0 && seaDirect + seaRoute + seaTrue > 0) Log($"position error placement: {seaDirect} shifted, {seaRoute} along the route, {seaTrue} shown at the true position (no sea point found)"); seaMonth = m; seaDirect = seaRoute = seaTrue = 0; seaLogged = 0; seaSeen.Clear(); }
            CampaignMap cm = null; try { cm = CampaignMap.Instance; } catch { }
            if (cm == null) { if (First(tf, "t")) seaTrue++; return Vector3.zero; }
            float a0 = Roll($"ang|{m}|{tf.Id}") * Mathf.PI * 2f;
            bool selfLand = PointLand(cm, pos);
            if (seaLogged < 6) { seaLogged++; Log($"position error check: force itself on a land cell = {selfLand} (in port or by the coast), error {mag * KmPerUnit():0} km"); }
            for (int i = 0; i < 16; i++)
            {
                float ang = a0 + i * 2.39996f; // golden angle - spreads the tries around the circle
                float d = mag * (i < 6 ? 1f : i < 12 ? 0.5f : 0.25f);
                var off = new Vector3(Mathf.Cos(ang) * d, 0f, Mathf.Sin(ang) * d);
                if (PointLand(cm, pos + off)) continue;
                if (!selfLand) { bool line = true; try { line = cm.RaycastLand(pos, pos + off); } catch { } if (line) continue; }
                if (First(tf, "d")) seaDirect++; return off;
            }
            float sign = Roll($"dir|{m}|{tf.Id}") < 0.5f ? -1f : 1f;
            if (allowRoute) foreach (var f in new[] { 1f, 0.5f, -1f, -0.5f })
            {
                var r = AlongRoute(tf, sign * f * mag);
                if (r.sqrMagnitude > 1e-6f && !PointLand(cm, pos + r)) { if (First(tf, "r")) seaRoute++; return r; }
            }
            if (First(tf, "t")) seaTrue++; return Vector3.zero;
        }
        static string Heading(CampaignController.TaskForce tf)
        {
            try
            {
                bool moving = false; try { moving = tf.IsMoving(); } catch { }
                if (!moving) return "stationary";
                Il2CppStructArray<Vector3> path = null; try { path = tf.FullPath; } catch { }
                if (path == null || path.Length < 2) { try { path = tf.Path; } catch { } }
                if (path == null || path.Length < 1) return "under way";
                var d = path[path.Length - 1] - tf.WorldPos; if (d.sqrMagnitude < 1e-6f) return "under way";
                float a = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; if (a < 0) a += 360f;
                string[] c = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
                return "heading " + c[(int)Mathf.Round(a / 45f) % 8];
            }
            catch { return "under way"; }
        }
        static MapState StateOf(CampaignController.TaskForce tf) { if (tf == null) return null; foreach (var v in mapStates.Values) { try { if (v.tf != null && v.tf.Pointer == tf.Pointer) return v; } catch { } } return null; }
        static Vector3 AlongRoute(CampaignController.TaskForce tf, float dist)
        {
            try
            {
                Il2CppStructArray<Vector3> path = null;
                try { path = tf.FullPath; } catch { }
                if (path == null || path.Length < 2) { try { path = tf.Path; } catch { } }
                if (path == null || path.Length < 2) return Vector3.zero;
                var pos = tf.WorldPos; int n = path.Length;
                // nearest segment to the force
                int best = 0; float bestD = float.MaxValue; float bestT = 0f;
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 a = path[i], b = path[i + 1]; var ab = b - a; float len2 = ab.sqrMagnitude; if (len2 < 1e-6f) continue;
                    float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / len2); float d = (a + ab * t - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; bestT = t; }
                }
                Vector3 cur = path[best] + (path[best + 1] - path[best]) * bestT;
                float left = Mathf.Abs(dist);
                if (dist >= 0f)
                {
                    int i = best; Vector3 p0 = cur;
                    while (i < n - 1) { Vector3 p1 = path[i + 1]; float sl = Vector3.Distance(p0, p1); if (sl >= left) return p0 + (p1 - p0).normalized * left - pos; left -= sl; p0 = p1; i++; }
                    return p0 - pos;
                }
                else
                {
                    int i = best; Vector3 p0 = cur;
                    while (i >= 0) { Vector3 p1 = path[i]; float sl = Vector3.Distance(p0, p1); if (sl >= left && sl > 0f) return p0 + (p1 - p0).normalized * left - pos; left -= sl; p0 = p1; i--; }
                    return p0 - pos;
                }
            }
            catch { return Vector3.zero; }
        }
        static void Save(CampaignMapElement el, MapState st)
        {
            if (st.saved) return; st.saved = true;
            try { st.elActive = el.gameObject.activeSelf; } catch { st.elActive = true; }
            st.routeActive = true; try { var su = el.TryCast<ShipUI>(); if (su != null && su.Route != null) st.routeActive = su.Route.gameObject.activeSelf; } catch { }
        }
        static void Restore(CampaignMapElement el, MapState st)
        {
            if (!st.saved) return; st.saved = false;
            try { if (el.gameObject.activeSelf != st.elActive) el.gameObject.SetActive(st.elActive); } catch { }
            ShowRoute(el, st.routeActive);
        }
        static void ShowRoute(CampaignMapElement el, bool show)
        {
            try { var su = el.TryCast<ShipUI>(); if (su != null && su.Route != null && su.Route.gameObject.activeSelf != show) su.Route.gameObject.SetActive(show); } catch { }
        }
        internal static void MapLateUpdate(MapUI ui)
        {
            if (ui == null) return;
            float now = Time.unscaledTime;
            if (now >= nextMap) { nextMap = now + 1f; MapRecompute(ui); }
            if (mapStates.Count == 0) return;
            try
            {
                foreach (var kv in ui.shipGroups)
                {
                    var el = kv.Value; if (el == null) continue; MapState st; if (!mapStates.TryGetValue(el.Pointer, out st)) continue;
                    if (st.mode == Mode.Hidden) { if (el.gameObject.activeSelf) el.gameObject.SetActive(false); ShowRoute(el, false); }
                    else if (st.mode == Mode.Blurred) ShowRoute(el, false);
                }
            }
            catch (Exception e) { Err("map late", e); }
        }
        static System.Reflection.MethodInfo w2ui; static System.Reflection.PropertyInfo canvasProp;
        // ShipUI.UpdatePositionScale prefix: shift a blurred force by its position error (in UI space)
        internal static void ShiftPosition(ShipUI el, ref Vector3 newPosition)
        {
            if (el == null || mapUi == null) return; MapState st;
            if (!mapStates.TryGetValue(el.Pointer, out st) || st.mode != Mode.Blurred) return;
            try
            {
                // via reflection: Canvas lives in UnityEngine.UIModule, which the mod does not reference
                if (w2ui == null) { w2ui = typeof(MapUI).GetMethod("WorldToUISpace"); canvasProp = typeof(MapUI).GetProperty("UICanvas"); }
                var c = canvasProp.GetValue(mapUi); var w = el.WorldPos;
                var a = (Vector3)w2ui.Invoke(mapUi, new object[] { c, w }); var b = (Vector3)w2ui.Invoke(mapUi, new object[] { c, w + st.offset });
                newPosition += b - a;
            }
            catch (Exception e) { Err("shift", e); }
        }
        static bool StrategicFogFor(CampaignController.TaskForce tf, out float q)
        {
            q = 1f; if (tf == null || !ReportActive) return false; var main = Main(); if (main == null || tf.Controller == null || Friendly(main, tf.Controller)) return false;
            q = Q(main, tf); return q < ExactQ;
        }

        // ---------------------------------------------------------------- enemy group window
        static bool popupFog; static int popupLogged;
        internal static void GroupPopup(ShipGroupPopupUI ui, CampaignController.TaskForce tf)
        {
            popupFog = false; float q;
            if (ui == null || !StrategicFogFor(tf, out q)) return;
            popupFog = true;
            try
            {
                float p = Mathf.Clamp(0.6f * (1f - q) / 0.65f, 0f, 0.6f);
                p = AirP(Main(), tf, p);   // 1.11.408
                int m = Month(); int shown = 0, hiddenN = 0;
                var list = ui.ships;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    var go = list[i]; if (go == null) continue;
                    var el = go.GetComponent<ShipPopup_Element>(); if (el == null) continue;
                    Ship s = null; try { var v = el.CurrentVessel; if (v != null) s = v.TryCast<Ship>(); } catch { }
                    string id = s != null ? s.id.ToString() : i.ToString();
                    if (shown > 0 && Roll($"gp|{m}|{id}|h") < 0.5f * p) { go.SetActive(false); hiddenN++; continue; }
                    string rep = s != null ? Misreport(TypeName(s), p, $"gp|{m}|{id}", s.player) : "?";
                    Set(el.Name, Cap(FullName(rep)) + " (reported)");
                    Set(el.Tonnes, "?"); Set(el.Damage, "?"); Set(el.Ammo, "?"); Set(el.Fuel, "?"); Set(el.Crew, "?"); Set(el.Speed, "?"); Set(el.Range, "?"); Set(el.Weapons, "?");
                    shown++;
                }
                Set(ui.Header, hiddenN > 0 || q < 0.5f ? "Reported enemy force (incomplete)" : "Reported enemy force");
                var ms = StateOf(tf); int age = ms != null && ms.hasSeen ? Month() - ms.seenMonth : 0;
                Set(ui.FromTo, ms == null || !ms.hasSeen ? "Course and destination unknown" : (age <= 0 ? "Reported this month, " : $"Last reported {age} month{(age > 1 ? "s" : "")} ago, ") + (ms.seenHeading ?? "course unknown") + "; destination unknown");
                try { if (ui.DesignShipInfoRoot != null) ui.DesignShipInfoRoot.SetActive(false); } catch { }
                if (popupLogged++ < 6) Log($"enemy group window: intelligence Q {q:0.00}, misidentification chance {p:0.00}, {shown} ships reported, {hiddenN} not reported");
            }
            catch (Exception e) { Err("group window", e); }
        }
        internal static bool GroupPopupShipInfo(ShipGroupPopupUI ui) { if (!popupFog) return true; try { if (ui.DesignShipInfoRoot != null) ui.DesignShipInfoRoot.SetActive(false); } catch { } return false; }
        static void Set(TMP_Text t, string v) { try { if (t != null) t.text = v; } catch { } }
        static void Set(UnityEngine.UI.Text t, string v) { try { if (t != null) t.text = v; } catch { } }

        // ---------------------------------------------------------------- battle decision window
        static int battlePopupLogged;
        internal static void BattleWindow(BattlePopup ui, CampaignBattle battle)
        {
            if (ui == null || battle == null || !ReportActive) return;
            try
            {
                var main = Main(); if (main == null || !battle.Participate(main)) return;
                bool att = battle.IsAttacker(main);
                var enemy = att ? battle.DefenderShips : battle.AttackerShips; var own = att ? battle.AttackerShips : battle.DefenderShips;
                if (enemy == null || enemy.Count == 0) return;
                // which list holds the enemy: match the number of entries (the window shows one entry per ship)
                int lc = ActiveChildren(ui.LeftShips), rc = ActiveChildren(ui.RightShips);
                Transform root = null;
                int lm = NameHits(ui.LeftShips, enemy), rm = NameHits(ui.RightShips, enemy);
                if (lm != rm) root = rm > lm ? ui.RightShips : ui.LeftShips;
                else if (rc == enemy.Count && lc != enemy.Count) root = ui.RightShips; else if (lc == enemy.Count && rc != enemy.Count) root = ui.LeftShips;
                else { Log($"battle window: cannot tell which list is the enemy (left {lc}, right {rc}, enemy {enemy.Count}) - report left unchanged"); return; }
                if (battlePopupLogged < 4)
                {
                    battlePopupLogged++;
                    var sb = new System.Text.StringBuilder(); var r2 = root; for (int i = 0; i < r2.childCount && i < 4; i++) { var e2 = r2.GetChild(i).GetComponent<BattlePopup_ElementUI>(); if (e2 != null && e2.Text != null) sb.Append("'" + e2.Text.text + "' "); }
                    string desc = ""; try { desc = ui.MissionDesc != null ? ui.MissionDesc.text : ""; } catch { }
                    Log($"battle window diagnostics: attacker={att}, left entries {lc}, right entries {rc}, enemy ships {enemy.Count}, own ships {(own != null ? own.Count : 0)}; enemy list sample: {sb}; mission text: {(desc.Length > 160 ? desc.Substring(0, 160) + "..." : desc)}");
                }
                // contact report quality: era optics and signalling, own scouts (light ships) in the force, technology, budget
                int light = 0; for (int i = 0; own != null && i < own.Count; i++) { var t = TypeName(own[i]); if (t == "cl" || t == "dd" || t == "ca") light++; }
                float qc = Fogged(Clamp01((0.25f + Era(Year()) * 0.5f) * TechF(main) * BudgetF(main) + 0.12f * Mathf.Min(4, light)));
                float p = Mathf.Clamp(0.6f * (1f - qc), 0f, 0.6f);
                string bk = battle.Id.ToString();
                var counts = new Dictionary<string, int>(); int hiddenN = 0, shown = 0;
                for (int i = 0; i < enemy.Count; i++)
                {
                    var s = enemy[i]; if (s == null) continue; string id = s.id.ToString();
                    if (shown > 0 && Roll($"bw|{bk}|{id}|h") < 0.5f * p) { hiddenN++; continue; }
                    string rep = Misreport(TypeName(s), p, $"bw|{bk}|{id}", s.player); int c; counts.TryGetValue(rep, out c); counts[rep] = c + 1; shown++;
                }
                // replace the enemy entries by the report
                for (int i = 0; i < root.childCount; i++) { var ch = root.GetChild(i); if (ch != null && ch.gameObject.activeSelf) ch.gameObject.SetActive(false); }
                var order = new List<string>(Scale); order.Reverse(); foreach (var k in counts.Keys) if (!order.Contains(k)) order.Add(k);
                foreach (var t in order)
                {
                    int c; if (!counts.TryGetValue(t, out c) || c == 0) continue;
                    AddLine(ui, root, (c == 1 ? "1 " + FullName(t) : c + " " + Plural(t)) + (p > 0.05f ? " (reported)" : ""));
                }
                if (hiddenN > 0 || p >= 0.3f) AddLine(ui, root, "Further contacts unconfirmed");
                Log($"battle report ({(att ? "attacking" : "defending")}): {shown} enemy ships reported, {hiddenN} not reported, misidentification chance {p:0.00} (report quality {qc:0.00}, {light} own scouts)");
            }
            catch (Exception e) { Err("battle window", e); }
        }
        static int NameHits(Transform t, Il2CppSystem.Collections.Generic.List<Ship> ships)
        {
            int n = 0; if (t == null) return 0;
            var names = new List<string>(); for (int i = 0; i < ships.Count; i++) { try { var nm = ships[i].vesselName; if (!string.IsNullOrEmpty(nm)) names.Add(nm); } catch { } }
            for (int i = 0; i < t.childCount; i++)
            {
                try { var e = t.GetChild(i).GetComponent<BattlePopup_ElementUI>(); if (e == null || e.Text == null) continue; var tx = e.Text.text; if (string.IsNullOrEmpty(tx)) continue; foreach (var nm in names) if (tx.Contains(nm)) { n++; break; } } catch { }
            }
            return n;
        }
        static int ActiveChildren(Transform t) { int n = 0; if (t == null) return 0; for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c != null && c.gameObject.activeSelf) n++; } return n; }
        static void AddLine(BattlePopup ui, Transform root, string text)
        {
            try
            {
                var el = UnityEngine.Object.Instantiate(ui.Tempalte, root); el.gameObject.SetActive(true);
                if (el.Text != null) el.Text.text = text;
            }
            catch (Exception e) { Err("battle window line", e); }
        }

        // ---------------------------------------------------------------- interceptions (player and AI)
        static int icMonth = -1, icChecked, icBlocked, icCalls;
        internal static void Intercept(Il2CppSystem.Collections.Generic.List<Ship> att, Il2CppSystem.Collections.Generic.List<Ship> def, ref bool result)
        {
            if (!result || !InterceptActive || att == null || def == null || att.Count == 0 || def.Count == 0) return;
            try
            {
                int m = Month(); if (m != icMonth) { if (icMonth > 0 && icCalls > 0) Log($"interceptions last month: {icCalls} checks by the game, {icChecked} against forces at sea, {icBlocked} failed for lack of intelligence"); icMonth = m; icChecked = icBlocked = icCalls = 0; }
                icCalls++;
                Player obs = att[0] != null ? att[0].player : null; if (obs == null) return;
                CampaignController.TaskForce tf = null;
                try { var d = def[0]; if (d != null) { var id = d.SeaGroupId; CampaignController.TaskForce t; if (CampaignController.Instance.CampaignData.TaskForceById.TryGetValue(id, out t)) tf = t; } } catch { }
                if (tf == null || tf.Controller == null || Friendly(obs, tf.Controller)) return;
                icChecked++;
                float q = Q(obs, tf);
                float block = q < HiddenQ ? 0.7f : q < 0.5f ? 0.3f : 0f;
                if (block > 0f && Roll($"ic|{m}|{PName(obs)}|{tf.Id}") < block)
                {
                    result = false; icBlocked++;
                    if (icBlocked <= 6) { string dn = "?"; try { dn = tf.Controller.data.name; } catch { } Log($"interception failed: {obs.data.name} did not find the {dn} force (intelligence Q {q:0.00})"); }
                }
            }
            catch (Exception e) { Err("intercept", e); }
        }

        // ---------------------------------------------------------------- battle: identification
        class Tac { public bool seen, identified; public string reported, label; public float idRange; public Vector3 pos; public float at; } // pos/at: 1.11.256
        // 1.11.256 (author 25.10, 12:40): the nearest smoke reported by the lookouts of the observer's side - an enemy ship sighted
        //           in this battle and not identified yet, seen within the last 2 minutes - for the Scout order (BattleOrders)
        internal static bool NearestSmoke(Player observer, Vector3 from, float maxDist, out Vector3 pos, out float idRange)
        {
            pos = Vector3.zero; idRange = 0f;
            try
            {
                if (tacBattleObj == null || observer == null || tac.Count == 0) return false;
                bool side = SideOf(tacBattleObj, observer); float best = maxDist, now = Time.time; bool ok = false;
                foreach (var kv in tac)
                {
                    if (((kv.Key & 1L) == 1L) != side) continue;
                    var s = kv.Value; if (!s.seen || s.identified || s.at <= 0f || now - s.at > 120f) continue;
                    float d = (s.pos - from).magnitude; if (d < best) { best = d; pos = s.pos; idRange = s.idRange; ok = true; }
                }
                return ok;
            }
            catch { return false; }
        }
        static IntPtr tacBattle; static readonly Dictionary<long, Tac> tac = new Dictionary<long, Tac>();
        static float nextTac; static int idLogged; static bool tacInfoLogged; static Player tacMain; static CampaignBattle tacBattleObj;
        static long Key(bool sideAttacker, Ship t) { return ((long)t.Pointer << 1) | (sideAttacker ? 1L : 0L); }
        static bool SideOf(CampaignBattle b, Player p) { try { return b.IsAttacker(p); } catch { return false; } }
        // 1.11.127: has the observer's side identified this ship (true when tactical identification is off or no campaign battle)
        internal static bool Identified(Player observer, Ship t)
        {
            try
            {
                if (!TacActive || observer == null || t == null) return true;
                var b = tacBattleObj; if (b == null || b.Pointer != tacBattle) return true;
                Tac st; if (!tac.TryGetValue(Key(SideOf(b, observer), t), out st)) return false;
                return st.identified;
            }
            catch { return true; }
        }
        internal static void Tick()
        {
            bool battle = false; try { battle = GameManager.IsBattle; } catch { }
            if (!battle) { if (tacBattle != IntPtr.Zero) { tacBattle = IntPtr.Zero; tac.Clear(); savedNames.Clear(); tacBattleObj = null; panelHidden = false; } return; }
            if (!TacActive) return;
            float now = Time.time; if (now < nextTac) return; nextTac = now + 0.5f;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null) return;
                if (b.Pointer != tacBattle) { tacBattle = b.Pointer; tacBattleObj = b; tac.Clear(); savedNames.Clear(); idLogged = 0; tacInfoLogged = false; tacMain = Main(); }
                var ships = b.Ships; if (ships == null) return;
                float day = 1f, wx = 1f; try { day = BattleManager.Instance.GetDaytimeDetection(); } catch { } try { wx = BattleManager.Instance.GetWeatherDetection(); } catch { }
                if (!tacInfoLogged) { tacInfoLogged = true; Log($"battle visibility: daytime detection {day:0.00}, weather detection {wx:0.00} -> visibility {((day > 1.5f && wx > 1.5f) ? Mathf.Min(day, wx) : 0f):0.0} km, battleship identification up to {(day > 1.5f && wx > 1.5f ? Mathf.Min(day, wx) * 0.45f : 0f):0.0} km"); }
                // 1.11.62: the game reports these as percentages (log of 2026-10-20: daytime 41.67, weather 50.00); read 0-100 as 0-1
                // 1.11.67: these are visibility distances in km, not shares - a daylight battle reported daytime 34.63 and weather
                // 50.00 (1.11.62 read them as 35% / 50% and cut identification to night-time ranges in broad daylight).
                // Visibility = the smaller of the two; identification cap = 45% of it x ship size (a battleship in 34 km
                // visibility ~15.6 km, a torpedo boat ~7.8 km; in 5 km night visibility a battleship ~2.3 km).
                float visKm = (day > 1.5f && wx > 1.5f) ? Mathf.Min(day, wx) : 15f * Mathf.Clamp(day, 0.15f, 1f) * Mathf.Clamp(wx, 0.3f, 1.2f) / 0.45f * 0.45f;
                if (!(day > 1.5f && wx > 1.5f)) visKm = 15f / 0.45f * Mathf.Clamp(day, 0.15f, 1f) * Mathf.Clamp(wx, 0.3f, 1.2f);
                string bk = b.Id.ToString();
                // cache once per tick: alive, side, position (interop calls are the expensive part)
                int n = ships.Count; var arr = new Ship[n]; var al = new bool[n]; var sd = new bool[n]; var ps = new Vector3[n];
                var sideCache = new Dictionary<IntPtr, bool>();
                for (int i = 0; i < n; i++)
                {
                    var x = ships[i]; arr[i] = x; al[i] = Alive(x); if (!al[i]) continue;
                    bool sv; var pp = x.player; if (pp == null) { al[i] = false; continue; }
                    if (!sideCache.TryGetValue(pp.Pointer, out sv)) { sv = SideOf(b, pp); sideCache[pp.Pointer] = sv; }
                    sd[i] = sv; try { ps[i] = x.transform.position; } catch { al[i] = false; }
                }
                for (int i = 0; i < n; i++)
                {
                    if (!al[i]) continue; var t = arr[i];
                    bool tSide = sd[i];
                    for (int side = 0; side < 2; side++)
                    {
                        bool obsSide = side == 1; if (obsSide == tSide) continue;
                        long k = Key(obsSide, t); Tac st; if (!tac.TryGetValue(k, out st)) { st = new Tac(); tac[k] = st; }
                        if (st.identified) continue;
                        Ship o = null; float d = float.MaxValue;
                        for (int j = 0; j < n; j++) { if (!al[j] || sd[j] != obsSide) continue; float dd = Vector3.Distance(ps[j], ps[i]); if (dd < d) { d = dd; o = arr[j]; } }
                        if (o == null) continue;
                        bool vis = false; try { vis = t.IsVisibleFor(o.player); } catch { }
                        if (!vis) continue;
                        float det = 0f; try { det = Ship.GetDetectionRange(o, t); } catch { }
                        if (det <= 0f) det = d * 1.1f;
                        string at = TypeName(t);
                        float sizeF = at == "bb" || at == "bc" ? 1f : at == "ca" ? 0.85f : at == "cl" ? 0.75f : at == "dd" ? 0.6f : at == "tb" ? 0.5f : 0.8f;
                        float smoke = 1f; try { var dv = t.division; if (dv != null && dv.smokeActive != null && dv.smokeActive.isRunning) smoke = 0.5f; } catch { }
                        float cap = 1000f * visKm * 0.45f * sizeF * smoke / Mathf.Max(0.25f, Strength.Value);
                        // 1.11.74: at night / in poor visibility muzzle flashes and fires give a ship away; Japanese night optics
                        if (NightFactors.Value && visKm < 20f)
                        {
                            float nf = 1f; float ls; if (lastShot.TryGetValue(t.Pointer, out ls) && Time.time - ls < 20f) nf *= 1.6f;
                            bool burning = false; try { burning = t.HaveAnyFireOnShip(); } catch { } if (burning) nf *= 1.5f;
                            try { if (o.player != null && o.player.data != null && o.player.data.name == "japan") nf *= 1.3f; } catch { }
                            cap *= Mathf.Min(nf, 2.5f);
                        }
                        float idR = Mathf.Clamp(Mathf.Min(det * 0.6f, cap), 1200f, 20000f);
                        st.idRange = idR; st.pos = ps[i]; st.at = Time.time; // 1.11.256: where the smoke is now (for the Scout order)
                        if (!st.seen)
                        {
                            st.seen = true; st.label = null;
                            float q = Clamp01(1f - (d - idR) / Mathf.Max(1f, det - idR));
                            float p = Mathf.Clamp(0.6f * (1f - q) * Strength.Value, 0f, 0.6f);
                            st.reported = Misreport(at, p, $"tac|{bk}|{t.id}|{side}", t.player);
                            Changed(t, obsSide);
                            try { Dispatch.Sighted(SizeClass(st.reported), d, obsSide == SideOf(b, tacMain)); } catch { }
                        }
                        float dk = d / 1000f; string dist = dk >= 10f ? $"{Mathf.Round(dk):0} km" : $"{Mathf.Round(dk * 2f) / 2f:0.0} km";   // 1.11.447 (author 30.10 17:16: "no distance for the unidentified ones") - rounded so the label changes rarely
                        string lbl = (d <= idR * 1.6f ? "Probable " + FullName(st.reported) + "?" : "Unidentified ship (" + SizeClass(st.reported) + ")") + ", " + dist;
                        if (lbl != st.label) { st.label = lbl; Changed(t, obsSide); }
                        if (d <= idR)
                        {
                            st.identified = true; st.label = null; Changed(t, obsSide);
                            if (obsSide == SideOf(b, tacMain)) Narrative.Record("ident-first", 2, $"First enemy ship identified: {Narrative_Short(t.name)} at {d / 1000f:0.0} km{(st.reported != at ? " - it had been reported as a " + FullName(st.reported) : "")}");
                            try { Dispatch.Identified(t.name, FullName(at), FullName(st.reported), d, obsSide == SideOf(b, tacMain)); } catch { }
                            if (idLogged++ < 40) Log($"{(obsSide == SideOf(b, tacMain) ? "player side" : "AI side")} identified {t.name} ({at}{(st.reported != at ? ", first reported as " + st.reported : "")}) at {d:0} m (identification range {idR:0} m, detection {det:0} m, daylight {day:0.0}, weather {wx:0.0}{(smoke < 1f ? ", smoke" : "")})");
                        }
                    }
                }
            }
            catch (Exception e) { Err("tick", e); }
        }
        static string Narrative_Short(string n) { if (string.IsNullOrEmpty(n)) return n; int i = n.IndexOf(" ("); return i > 0 ? n.Substring(0, i) : n; }
        static bool Alive(Ship x) { try { return x != null && !x.isSinking && !x.isDead && !x.IsSurrendered; } catch { return false; } }
        static void Changed(Ship t, bool obsSide)
        {
            try { if (tacMain != null && tacBattleObj != null && obsSide == SideOf(tacBattleObj, tacMain)) { t.RefreshOverheadUi(); RefreshPanelIfSelected(t); } } catch { }
        }
        // 1.11.63: the ship info panel shown after clicking a ship (class, guns, armour, damage plan) - for an unidentified
        // enemy the panel is replaced by the game's own stub text until the ship is identified
        static bool panelHidden;
        static void RefreshPanelIfSelected(Ship t)
        {
            try { var ui = G.ui; if (ui == null) return; var sel = ui.selectedShipMain; if (sel != null && sel.Pointer == t.Pointer) ui.RefreshShipInfo(); } catch { }
        }
        // 1.11.66: the game re-opens the ship panel outside RefreshShipInfo (report from the game: details showed up again for an
        // unidentified ship), so the hidden state is enforced every frame while such a ship is selected
        internal static void LateTick()
        {
            if (tacBattleObj == null || !TacActive) return;
            try { var ui = G.ui; if (ui == null) return; var sel = ui.selectedShipMain; if (sel == null && !panelHidden) return; if (DetailsLabel(sel) != null || panelHidden) ShipPanel(ui); } catch { }
        }
        internal static void ShipPanel(Ui ui)
        {
            if (ui == null) return;
            try
            {
                Ship sel = null; try { sel = ui.selectedShipMain; } catch { }
                var lbl = DetailsLabel(sel);
                if (lbl != null)
                {
                    if (ui.shipInfoPanel != null && ui.shipInfoPanel.activeSelf) ui.shipInfoPanel.SetActive(false);
                    if (ui.shipInfoStub != null && !ui.shipInfoStub.activeSelf) ui.shipInfoStub.SetActive(true);
                    if (ui.shipInfoStubText != null) ui.shipInfoStubText.text = lbl + (PlayerLabel(sel) != null ? "\nClass, guns and armour unknown until one of your ships identifies it." : "\nEnemy ship - armament, armour and damage are not known.");
                    panelHidden = true;
                }
                else if (panelHidden && sel != null)
                {
                    // 1.11.118: restored only when a ship is selected again - before, deselecting an unidentified enemy cleared the
                    // flag with no ship to restore, and the panel stayed hidden for the player's own ships (only the crew panel showed)
                    panelHidden = false;
                    if (ui.shipInfoPanel != null && !ui.shipInfoPanel.activeSelf) ui.shipInfoPanel.SetActive(true);
                    if (ui.shipInfoStub != null && ui.shipInfoStub.activeSelf) ui.shipInfoStub.SetActive(false);
                }
            }
            catch (Exception e) { Err("ship panel", e); }
        }
        // 1.11.119: label for the info panel, crew panel and tooltip picture - also for an identified enemy when FogHideEnemyDetails is on
        internal static string DetailsLabel(Ship s)
        {
            var lbl = PlayerLabel(s); if (lbl != null) return lbl;
            if (HideEnemyDetails == null || !HideEnemyDetails.Value || s == null || tacBattleObj == null || tacMain == null) return null;
            try
            {
                if (s.player == null) return null; bool ps = SideOf(tacBattleObj, tacMain); if (SideOf(tacBattleObj, s.player) == ps) return null;
                return Narrative_Short(s.name) + " (" + FullName(TypeName(s)) + ")";
            }
            catch { return null; }
        }
        // what the player's side sees of an enemy ship: null when identified (or not an enemy)
        internal static string PlayerLabel(Ship s)
        {
            if (!TacActive || s == null || tacBattleObj == null || tacMain == null) return null;
            try
            {
                if (s.player == null) return null; bool ps = SideOf(tacBattleObj, tacMain); if (SideOf(tacBattleObj, s.player) == ps) return null;
                Tac st; if (!tac.TryGetValue(Key(ps, s), out st)) return "Unidentified contact";
                if (st.identified) return null;
                return st.label ?? "Unidentified contact";
            }
            catch { return null; }
        }
        // AI picture of an enemy ship: reported type (null = never seen), perceived tonnage
        internal static string PType(Player obs, Ship e)
        {
            string act = TypeName(e);
            if (!AiActive || obs == null || e == null || tacBattleObj == null) return act;
            try { Tac st; if (!tac.TryGetValue(Key(SideOf(tacBattleObj, obs), e), out st) || !st.seen) return null; return st.identified ? act : st.reported; } catch { return act; }
        }
        internal static float PTon(Player obs, Ship e, float actualTon)
        {
            var rep = PType(obs, e); if (rep == null) return 0f; var act = TypeName(e);
            if (rep == act) return actualTon;
            float a, r; if (Typical.TryGetValue(act, out a) && Typical.TryGetValue(rep, out r) && a > 0f) return actualTon * r / a;
            return actualTon;
        }
        internal static bool PCapital(string t) { return t == "bb" || t == "bc" || t == "ca"; }

        // ---------------------------------------------------------------- battle UI overrides (player view)
        static readonly Dictionary<IntPtr, string> savedNames = new Dictionary<IntPtr, string>();
        internal static void UiShip(UIShip u)
        {
            if (u == null) return;
            try
            {
                var s = u.CurrentShip; var lbl = PlayerLabel(s);
                if (lbl != null)
                {
                    if (u.Name != null) { if (!savedNames.ContainsKey(u.Pointer) && u.Name.text != lbl) savedNames[u.Pointer] = u.Name.text; u.Name.text = lbl; }
                    if (u.LeftCornerText != null) u.LeftCornerText.text = ""; if (u.RightCorner != null) u.RightCorner.text = "";
                    if (u.ShipIcon != null && u.ShipIcon.enabled) u.ShipIcon.enabled = false;
                }
                else
                {
                    string orig; if (savedNames.TryGetValue(u.Pointer, out orig)) { savedNames.Remove(u.Pointer); if (u.Name != null && s != null) u.Name.text = orig; if (u.ShipIcon != null) u.ShipIcon.enabled = true; }
                }
            }
            catch (Exception e) { Err("ship list", e); }
        }
        internal static void UiDivision(UIDivision u)
        {
            if (u == null) return;
            try
            {
                var dv = u.CurrentDivision; if (dv == null || dv.ships == null) return;
                bool unk = false; for (int i = 0; i < dv.ships.Count; i++) if (PlayerLabel(dv.ships[i]) != null) { unk = true; break; }
                if (unk)
                {
                    if (u.Name != null) { if (!savedNames.ContainsKey(u.Pointer) && u.Name.text != "Unidentified contacts") savedNames[u.Pointer] = u.Name.text; u.Name.text = "Unidentified contacts"; }
                    if (u.ProfileIcon != null && u.ProfileIcon.enabled) u.ProfileIcon.enabled = false;
                }
                else { string orig; if (savedNames.TryGetValue(u.Pointer, out orig)) { savedNames.Remove(u.Pointer); if (u.Name != null) u.Name.text = orig; if (u.ProfileIcon != null) u.ProfileIcon.enabled = true; } }
            }
            catch (Exception e) { Err("division list", e); }
        }
        internal static void Overhead(Ship s)
        {
            try { var lbl = PlayerLabel(s); if (lbl != null && s.uiName != null) s.uiName.text = lbl; } catch (Exception e) { Err("overhead", e); }
        }
        internal static bool InfoText(Ship ship, bool battle, ref string result)
        {
            var lbl = PlayerLabel(ship);
            if (lbl == null) { var dl = DetailsLabel(ship); if (dl == null) return true; result = dl + "\nArmament, armour and damage not known."; return false; }   // 1.11.119
            result = lbl + "\nType, armament and armour unconfirmed until the ship is identified."; return false; // 1.11.63: also when the game asks without the battle flag
        }
        internal static void Tooltip(DivisionTooltip t)
        {
            try
            {
                var dv = t.currentDivision; if (dv == null || dv.ships == null || t.createdElements == null) return;
                int n = Math.Min(dv.ships.Count, t.createdElements.Count);
                for (int i = 0; i < n; i++)
                {
                    var lbl = PlayerLabel(dv.ships[i]); var dl = DetailsLabel(dv.ships[i]); var el = t.createdElements[i]; if (el == null) continue;
                    if (dl == null) { if (el.ShipTex != null && !el.ShipTex.enabled) el.ShipTex.enabled = true; continue; }
                    if (lbl != null && el.Description != null) el.Description.text = lbl; if (el.ShipTex != null) el.ShipTex.enabled = false;   // picture with damage hidden
                }
            }
            catch (Exception e) { Err("tooltip", e); }
        }

        // ---------------------------------------------------------------- patches (applied one by one; a failing target only disables that part)
        internal static void Apply(HarmonyLib.Harmony h)
        {
            var me = typeof(Fog);
            P(h, AccessTools.Method(typeof(MapUI), "LateUpdate"), null, nameof(Post_MapLate));
            P(h, AccessTools.Method(typeof(Effect), "GunShoot"), null, nameof(Post_GunShoot));
            P(h, AccessTools.Method(typeof(ShipUI), "UpdatePositionScale", new[] { typeof(Vector3), typeof(float) }), nameof(Pre_ShipUiPos), null);
            P(h, AccessTools.Method(typeof(ShipGroupPopupUI), "Init", new[] { typeof(CampaignController.TaskForce) }), nameof(Pre_GroupPopup), nameof(Post_GroupPopup));
            P(h, AccessTools.Method(typeof(ShipGroupPopupUI), "SetShipInfoAndImage", new[] { typeof(Ship) }), nameof(Pre_GroupShipInfo), null);
            P(h, AccessTools.Method(typeof(BattlePopup), "Init", new[] { typeof(CampaignBattle) }), null, nameof(Post_BattlePopup));
            P(h, AccessTools.Method(typeof(MissionGenerator), "CanParticipateInBattleByRecon", new[] { typeof(Il2CppSystem.Collections.Generic.List<Ship>), typeof(Il2CppSystem.Collections.Generic.List<Ship>), typeof(BattleTypeEx) }), null, nameof(Post_Recon));
            P(h, AccessTools.Method(typeof(MissionGenerator), "CanParticipateInBattleByRecon", new[] { typeof(Il2CppSystem.Collections.Generic.List<VesselEntity>), typeof(Il2CppSystem.Collections.Generic.List<VesselEntity>), typeof(BattleTypeEx) }), null, nameof(Post_ReconV));
            P(h, AccessTools.Method(typeof(UIShip), "LateUpdate"), null, nameof(Post_UiShip));
            P(h, AccessTools.Method(typeof(UIDivision), "RefreshUI", new[] { typeof(bool) }), null, nameof(Post_UiDivision));
            P(h, AccessTools.Method(typeof(UIDivision), "LateUpdate"), null, nameof(Post_UiDivisionLate));
            P(h, AccessTools.Method(typeof(Ship), "RefreshOverheadUi"), null, nameof(Post_Overhead));
            P(h, AccessTools.Method(typeof(Ui), "GetShipInfoText"), nameof(Pre_InfoText), null);
            P(h, AccessTools.Method(typeof(DivisionTooltip), "UpdateElements"), null, nameof(Post_Tooltip));
            P(h, AccessTools.Method(typeof(Ui), "RefreshShipInfo"), null, nameof(Post_ShipPanel));
            P(h, AccessTools.Method(typeof(Ui), "UpdateCrewInfoPanel", new[] { typeof(Ship) }), nameof(Pre_CrewPanel), null);
        }
        static int applied, failed;
        static void P(HarmonyLib.Harmony h, System.Reflection.MethodBase target, string pre, string post)
        {
            string name = target != null ? target.DeclaringType.Name + "." + target.Name : "?";
            try
            {
                if (target == null) throw new Exception("method not found");
                var prefix = pre != null ? new HarmonyMethod(AccessTools.Method(typeof(Fog), pre)) : null;
                var postfix = post != null ? new HarmonyMethod(AccessTools.Method(typeof(Fog), post)) : null;
                h.Patch(target, prefix, postfix); applied++;
            }
            catch (Exception e) { failed++; MelonLogger.Warning($"[DIP GG Fixes][Fog] patch {name} failed: {e.Message}"); }
        }
        internal static void ReportPatches() { Log($"{applied} hooks active{(failed > 0 ? ", " + failed + " failed" : "")}"); }

        static void Post_GunShoot(GameObject parent)
        {
            try { if (parent == null || !TacActive) return; var sh = parent.GetComponentInParent<Ship>(); if (sh != null) lastShot[sh.Pointer] = Time.time; } catch { }
        }
        static void Post_MapLate(MapUI __instance) { try { MapLateUpdate(__instance); } catch { } }
        static void Pre_ShipUiPos(ShipUI __instance, ref Vector3 newPosition) { try { if (MapActive) ShiftPosition(__instance, ref newPosition); } catch { } }
        static void Pre_GroupPopup(ShipGroupPopupUI __instance) { try { if (popupFog) { popupFog = false; if (__instance.DesignShipInfoRoot != null) __instance.DesignShipInfoRoot.SetActive(true); } } catch { } }
        static void Post_GroupPopup(ShipGroupPopupUI __instance, CampaignController.TaskForce group) { try { GroupPopup(__instance, group); } catch { } }
        static bool Pre_GroupShipInfo(ShipGroupPopupUI __instance) { try { return GroupPopupShipInfo(__instance); } catch { return true; } }
        static void Post_BattlePopup(BattlePopup __instance, CampaignBattle battle) { try { Narrative.SetPopup(__instance); } catch { } BattleWindow(__instance, battle); try { BattleHour.Popup(__instance, battle); } catch { } }
        static void Post_Recon(Il2CppSystem.Collections.Generic.List<Ship> attackerShips, Il2CppSystem.Collections.Generic.List<Ship> defenderShips, ref bool __result) { Intercept(attackerShips, defenderShips, ref __result); }
        static void Post_ReconV(Il2CppSystem.Collections.Generic.List<VesselEntity> attackerShips, Il2CppSystem.Collections.Generic.List<VesselEntity> defenderShips, ref bool __result)
        {
            if (!__result || !InterceptActive) return;
            try
            {
                var a = new Il2CppSystem.Collections.Generic.List<Ship>(); var d = new Il2CppSystem.Collections.Generic.List<Ship>();
                for (int i = 0; attackerShips != null && i < attackerShips.Count; i++) { var s = attackerShips[i] != null ? attackerShips[i].TryCast<Ship>() : null; if (s != null) a.Add(s); }
                for (int i = 0; defenderShips != null && i < defenderShips.Count; i++) { var s = defenderShips[i] != null ? defenderShips[i].TryCast<Ship>() : null; if (s != null) d.Add(s); }
                Intercept(a, d, ref __result);
            }
            catch (Exception e) { Err("intercept v", e); }
        }
        static void Post_UiShip(UIShip __instance) { if (tacBattleObj != null) UiShip(__instance); }
        static void Post_UiDivision(UIDivision __instance) { if (tacBattleObj != null) UiDivision(__instance); }
        static void Post_UiDivisionLate(UIDivision __instance) { if (tacBattleObj != null) UiDivision(__instance); }
        static void Post_Overhead(Ship __instance) { if (tacBattleObj != null) Overhead(__instance); }
        static bool Pre_InfoText(Ship ship, bool battle, ref string __result) { try { if (tacBattleObj == null) return true; return InfoText(ship, battle, ref __result); } catch { return true; } }
        static void Post_Tooltip(DivisionTooltip __instance) { if (tacBattleObj != null) Tooltip(__instance); }
        static void Post_ShipPanel(Ui __instance) { if (tacBattleObj != null || panelHidden) ShipPanel(__instance); }
        static bool Pre_CrewPanel(Ship ship) { try { return tacBattleObj == null || DetailsLabel(ship) == null; } catch { return true; } }
    }
}
