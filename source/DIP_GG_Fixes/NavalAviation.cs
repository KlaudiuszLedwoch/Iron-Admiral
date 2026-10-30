// 1.11.407 (#85, step 1 - air reconnaissance in the campaign; author 29.10 22:58: technologies as the new type naval_aviation,
// "the list looks OK"; 23:46: "work to the plan"): the Naval Aviation technologies a nation has researched decide how far its
// air patrols reach from its own ports:
//   av_recon_1 coastal seaplane stations (1912)   ~100 km
//   av_recon_2 naval airships (1915)              ~400 km
//   av_recon_3 flying boats (1918)                ~500 km
//   av_recon_4 long-range maritime patrol (1930)  ~900 km
//   av_recon_5 airborne search radar (1941)       ~1000 km, and flies in bad weather
// (ranges: the working list claude/technologie_lotnicze_85.md - estimates, not from a source). An enemy force within that range
// of one of the nation's ports adds up to AirReconQ (0.30) to the intelligence quality Fog.Q, falling off with distance. Air
// patrols do not fly in bad weather: in the winter months 50 %, otherwise 30 % of the months in each 800 km sea area are
// unflyable (with the airborne radar 15 %). Switch: NavalAviationRecon.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class NavalAviation
    {
        internal static MelonPreferences_Entry<bool> Recon;
        internal static MelonPreferences_Entry<float> ReconQ;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Recon = cat.CreateEntry("NavalAviationRecon", true, null, "Fog of war: the Naval Aviation technologies (seaplane stations, airships, flying boats, long-range patrol, airborne radar) let a nation's air patrols find enemy forces within their range of its ports - not in bad weather");
            ReconQ = cat.CreateEntry("NavalAviationReconQ", 0.30f, null, "Fog of war: the most that air patrols add to the intelligence quality of a force close to one of our ports (0.30)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][NavalAviation] " + m); }

        // researched Naval Aviation technologies per nation, refreshed each month
        static int month = -1; static readonly Dictionary<IntPtr, HashSet<string>> have = new Dictionary<IntPtr, HashSet<string>>();
        static int Month() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; } }
        internal static bool Has(Player p, string id)
        {
            if (p == null) return false;
            int m = Month(); if (m != month) { month = m; have.Clear(); }
            if (!have.TryGetValue(p.Pointer, out var set))
            {
                set = new HashSet<string>();
                try
                {
                    var tl = p.technologies;
                    for (int i = 0; tl != null && i < tl.Count; i++)
                    {
                        var t = tl[i]; if (t == null || t.data == null) continue;
                        string n = t.data.name ?? ""; if (!n.StartsWith("av_") && !n.StartsWith("at_") && !n.StartsWith("aa_")) continue;
                        bool ok = false; try { ok = t.isResearched; } catch { }
                        if (ok) set.Add(n);
                    }
                }
                catch { }
                have[p.Pointer] = set;
            }
            return set.Contains(id);
        }
        internal static int ReconLevel(Player p)
        {
            for (int l = 5; l >= 1; l--) if (Has(p, "av_recon_" + l)) return l;
            return 0;
        }
        // -----------------------------------------------------------------------------------------------------------------------
        // 1.11.408 (#85, plan point 1 of claude/technologie_lotnicze_85.md - groups C, D, E): in campaign battles an air strike
        // follows the researched technologies of the attacking nation (torpedo, carrier, aircraft type) and the anti-aircraft fire
        // follows those of each defending ship's nation; custom battles keep using the battle year.
        internal static bool CampaignBattle() { try { return !GameManager.IsCustomBattle && CampaignController.Instance != null; } catch { return false; } }
        static int Level(Player p, string pre, int max) { for (int l = max; l >= 1; l--) if (Has(p, pre + l)) return l; return 0; }
        internal static int TorpLevel(Player p) { return Level(p, "at_", 7); }
        internal static int CarrierLevel(Player p) { return Level(p, "av_carrier_", 4); }
        internal static int TypesLevel(Player p) { return Level(p, "av_types_", 3); }
        // a strike needs a seaplane carrier (av_carrier_1) and an aerial torpedo (at_1)
        internal static bool CanStrike(Player p, out string why)
        {
            int c = CarrierLevel(p), t = TorpLevel(p);
            why = c == 0 && t == 0 ? "no seaplane carrier and no aerial torpedo researched" : c == 0 ? "no seaplane carrier researched (av_carrier_1)" : t == 0 ? "no aerial torpedo researched (at_1)" : "";
            return c > 0 && t > 0;
        }
        // the year of the aircraft and attack profile: the newest researched type; before av_types_1 the seaplane of the 1910s,
        // or the early carrier aircraft once a flying-off deck is researched
        internal static int StrikeYear(Player p)
        {
            int ty = TypesLevel(p);
            if (ty >= 3) return 1943; if (ty == 2) return 1940; if (ty == 1) return 1936;
            return CarrierLevel(p) >= 2 ? 1925 : 1916;
        }
        // limits from the carrier technologies: av_carrier_1 two waves of up to 3 aircraft, av_carrier_2 up to 6 in a wave,
        // av_carrier_3 three waves, av_carrier_4 (arrester wires) a quarter shorter interval between waves
        internal static int MaxWaves(Player p) { return CarrierLevel(p) >= 3 ? 3 : 2; }
        internal static int MaxPerWave(Player p) { return CarrierLevel(p) >= 2 ? 6 : 3; }
        internal static float IntervalF(Player p) { return CarrierLevel(p) >= 4 ? 0.75f : 1f; }
        // aerial warhead against the ship torpedo whose data are used: at_1 experimental x0.4 (small ships only in the list -
        // here a weak warhead), at_2 18-inch ~145 kg x0.5, at_3/at_4 ~170 kg x0.6, at_5 heavy warhead x0.75, at_6 Torpex x0.9
        internal static float WarheadF(Player p)
        {
            int t = TorpLevel(p);
            return t >= 6 ? 0.9f : t >= 5 ? 0.75f : t >= 3 ? 0.6f : t == 2 ? 0.5f : 0.4f;
        }
        // light AA strength (same scale as the year factor of custom battles): none 0.2, aa_1 0.35, aa_2 0.6, aa_3 director 1.0,
        // aa_5 20-40 mm 1.5, aa_6 proximity fuze 2.5
        internal static float AaF(Player p)
        {
            if (Has(p, "aa_6")) return 2.5f; if (Has(p, "aa_5")) return 1.5f; if (Has(p, "aa_3")) return 1f;
            if (Has(p, "aa_2")) return 0.6f; if (Has(p, "aa_1")) return 0.35f; return 0.2f;
        }
        // the splash barrage of the heavy guns needs aa_2
        internal static bool Splash(Player p) { return Has(p, "aa_2"); }
        internal static string Summary(Player p)
        {
            return $"carrier {CarrierLevel(p)}/4, aerial torpedo {TorpLevel(p)}/7, aircraft types {TypesLevel(p)}/3 -> aircraft of {StrikeYear(p)}, warhead x{WarheadF(p):0.00}, up to {MaxWaves(p)} waves of {MaxPerWave(p)}";
        }

        // -----------------------------------------------------------------------------------------------------------------------
        // 1.11.410 (#85 step 2 - group B): spotting aircraft from catapults. Battleships, battlecruisers and cruisers of a nation
        // with the ship catapult (av_ship_2) launch a spotter in daylight; the observer reports the fall of shot beyond the
        // horizon of the ship's own control top. Hit chance x1.10 with the catapult, x1.25 with air spotting procedures
        // (av_ship_3), growing from nothing at 12 km to the full bonus at 18 km (estimates, not from a source). Custom battles:
        // catapult from 1922, spotting from 1925. Switch: NavalAviationSpotting.
        internal static MelonPreferences_Entry<bool> Spotting;
        internal static void CreateSpottingPrefs(MelonPreferences_Category cat)
        {
            Spotting = cat.CreateEntry("NavalAviationSpotting", true, null, "Battle: battleships and cruisers of a nation with the ship catapult launch spotting aircraft in daylight - better hit chance beyond 12 km (more with air spotting procedures); custom battles by the year (catapult 1922, spotting 1925)");
        }
        static readonly Dictionary<IntPtr, int> spotLv = new Dictionary<IntPtr, int>(); static IntPtr spotBattle; static int spotLogged;
        static int SpotLevel(Ship s)
        {
            IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer; } catch { }
            if (b != spotBattle) { spotBattle = b; spotLv.Clear(); spotLogged = 0; }
            if (spotLv.TryGetValue(s.Pointer, out int lv)) return lv;
            lv = 0;
            try
            {
                string t = s.shipType != null ? s.shipType.name : "";
                if (t == "bb" || t == "bc" || t == "ca" || t == "cl")
                {
                    if (CampaignBattle()) { if (s.player != null) lv = Has(s.player, "av_ship_3") ? 2 : Has(s.player, "av_ship_2") ? 1 : 0; }
                    else { int y = (int)s.GetYear(s); lv = y >= 1925 ? 2 : y >= 1922 ? 1 : 0; }
                }
            }
            catch { }
            spotLv[s.Pointer] = lv;
            if (lv > 0 && spotLogged++ < 4) Log($"{s.name}: spotting aircraft from the catapult{(lv >= 2 ? " with air spotting procedures" : "")}");
            return lv;
        }
        internal static int SpotLevelOf(Ship s) { if (Spotting == null || !Spotting.Value || s == null) return 0; return SpotLevel(s); }
        internal static float SpotF(Ship s, float d)
        {
            if (Spotting == null || !Spotting.Value || s == null || d <= 12000f) return 1f;
            bool night = false; try { night = BattleAi.IsNight(); } catch { }
            if (night) return 1f;
            int lv = SpotLevel(s); if (lv == 0) return 1f;
            if (!Spotters.Airborne(s)) return 1f;   // 1.11.417: only while the drawn aircraft is up
            float full = lv >= 2 ? 1.25f : 1.10f;
            return 1f + (full - 1f) * Mathf.Clamp01((d - 12000f) / 6000f);
        }

        static readonly float[] rangeKm = { 0f, 100f, 400f, 500f, 900f, 1000f };
        // misidentification of air reports by patrol type (claude/technologie_lotnicze_85.md group A; the airships of 19 August 1916:
        // 4 of 7 reports wrong, L 13 took cruisers for battleships)
        static readonly float[] misRate = { 0f, 0.40f, 0.45f, 0.25f, 0.15f, 0.10f };
        internal static float MisRate(int lv) { return lv >= 0 && lv < misRate.Length ? misRate[lv] : 0f; }
        static readonly string[] what = { "", "seaplane stations", "airships", "flying boats", "long-range patrol", "patrol aircraft with radar" };
        static int logged;

        // added to Fog.Q: own port portKm away from the force, month m, 800 km sea area cx/cz
        internal static float AirQ(Player obs, float portKm, int m, int cx, int cz, out string why)
        {
            why = "";
            if (Recon == null || !Recon.Value || obs == null) return 0f;
            int lv = ReconLevel(obs); if (lv == 0) return 0f;
            float range = rangeKm[lv]; if (portKm >= range) { why = $"air {what[lv]}: out of range ({portKm:0} of {range:0} km)"; return 0f; }
            int mon = ((m - 1) % 12 + 12) % 12 + 1; bool winter = mon == 11 || mon == 12 || mon == 1 || mon == 2;
            float bad = lv >= 5 ? 0.15f : winter ? 0.5f : 0.3f;
            if (Fog.Roll($"air|{m}|{cx}|{cz}") < bad) { why = $"air {what[lv]}: grounded by the weather"; return 0f; }
            float q = (ReconQ != null ? ReconQ.Value : 0.30f) * Mathf.Clamp01(1f - portKm / range);
            why = $"air {what[lv]} +{q:0.00} ({portKm:0} of {range:0} km)";
            if (obs.isMain && logged++ < 10) Log($"{obs.data.name}: {why}");
            return q;
        }
    }
}
