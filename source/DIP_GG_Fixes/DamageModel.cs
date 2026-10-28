// 1.11.278 (#33 steps 3b and 3c, author's decisions 25.10, 18:45-18:59): the damage of a hit no longer grows with the margin of
// penetration over the plate. The game's own hit damage (observed in five battles of 25.10) is about 0.5 x the gun's base damage x
// (penetration / plate), capped near x11 on unarmoured targets (12 in HE into a transport: 17,050; 12 in AP into a destroyer: 3,560;
// the same shell into a battleship: 1,383). Historically the damage of a burst comes from the shell's bursting charge and from where
// it bursts; the margin of penetration only decides whether the fuze works inside or the shell passes through.
//   3c - full penetration ("normal"): damage = base damage of the shell type (the gun window's "Damage AP" / "Damage HE", i.e. the
//        game's own charge-and-calibre value with the ship's technologies) x the factor of the place: citadel 1.0, machinery or
//        magazine section 1.2, hull ends 0.6, turret or barbette 0.8, upper works 0.5; +0.1 at or below the waterline. Pass-throughs
//        keep their fraction of that (OverPen).
//   3b - a burst on armour ("armor"): an HE shell on a turret, barbette or the upper works does the damage of a full penetration
//        there (author 18:45: as the historical record - Tsushima, Jutland); an HE shell on the belt or the hull ends does next to
//        nothing to the hull (author 18:46: "HE hitting the belt can do little") - the game's own small value stays. AP bursts on
//        armour keep the game's value (1-55).
// Switch: DamageModelHistorical (F10, Battle). Log: the first 40 replaced hits.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DamageModel
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> FCitadel, FMachinery, FEnds, FTurret, FUpper, FWaterline;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DamageModelHistorical", true, null, "Battle: the damage of a full penetration is the shell's base damage (charge and calibre, gun window) times the place of the burst, not the game's margin of penetration over the plate (which gave up to x11 on unarmoured targets); an HE burst on a turret or the upper works counts as a penetration there, on the belt or hull ends it stays small");
            FCitadel = cat.CreateEntry("DamageFactorCitadel", 1.0f, null, "Place factor: citadel");
            FMachinery = cat.CreateEntry("DamageFactorMachinery", 1.2f, null, "Place factor: machinery or magazine section");
            FEnds = cat.CreateEntry("DamageFactorHullEnds", 0.6f, null, "Place factor: hull ends");
            FTurret = cat.CreateEntry("DamageFactorTurret", 0.8f, null, "Place factor: turret or barbette");
            FUpper = cat.CreateEntry("DamageFactorUpperWorks", 0.5f, null, "Place factor: upper works");
            FWaterline = cat.CreateEntry("DamageFactorWaterline", 0.1f, null, "Place factor: added at or below the waterline");
        }
        static int logged, errors; static readonly Dictionary<long, float[]> baseCache = new Dictionary<long, float[]>();
        internal static void BattleStart() { logged = 0; baseCache.Clear(); tally.Clear(); }

        // 1.11.350 (#73, campaign 28.10: a 31,111 t battleship surrendered after 77 hits, but only 11 of them were in the capped log):
        // every hit the historical model sets is counted per target ship, calibre and result; the battle summary lists the ships
        // with their structure, so the share of heavy and medium hits in the damage can be read for each ship
        static readonly Dictionary<string, Dictionary<string, float[]>> tally = new Dictionary<string, Dictionary<string, float[]>>();
        static readonly Dictionary<string, float> structure = new Dictionary<string, float>();
        static void Tally(Ship ship, string key, float dmg)
        {
            try
            {
                string n = ship.name;
                if (!tally.TryGetValue(n, out var m)) { tally[n] = m = new Dictionary<string, float[]>(); try { structure[n] = LightShipDamage.Structure(ship); } catch { } }
                if (!m.TryGetValue(key, out var v)) m[key] = v = new float[2];
                v[0]++; v[1] += dmg;
            }
            catch { }
        }
        internal static void Report()
        {
            if (tally.Count == 0) return;
            var sb = new System.Text.StringBuilder("[DIP GG Fixes][Damage] battle summary - historical model damage by target ship (calibre and result | hits | structure):");
            foreach (var kv in tally)
            {
                float tot = 0f; int hits = 0; foreach (var v in kv.Value.Values) { tot += v[1]; hits += (int)v[0]; }
                structure.TryGetValue(kv.Key, out float hp0);
                sb.Append($"\n   {kv.Key}: {hits} hits, {tot:0}{(hp0 > 0f ? $" (ship structure {hp0:0})" : "")}");
                foreach (var e in kv.Value) sb.Append($"\n      {e.Key} | {e.Value[0]:0} | {e.Value[1]:0}");
            }
            MelonLogger.Msg(sb.ToString());
        }

        static float Factor(string loc)
        {
            float f;
            if (loc.StartsWith("machinery")) f = FMachinery.Value;
            else if (loc.StartsWith("citadel")) f = FCitadel.Value;
            else if (loc.StartsWith("hull ends")) f = FEnds.Value;
            else if (loc.StartsWith("turret")) f = FTurret.Value;
            else if (loc.StartsWith("upper works")) f = FUpper.Value;
            else f = FEnds.Value;
            if (loc.Contains("waterline")) f += FWaterline.Value;
            return Math.Max(0.05f, f);
        }
        // the gun window's "Damage AP" / "Damage HE" (the game's own value with the ship's technologies), parsed once per gun and ship
        static float Base(PhysPen.Shot sh, bool he)
        {
            if (sh == null || sh.Pd == null || sh.Shooter == null) return 0f;
            long key = (sh.Pd.Pointer.ToInt64() * 31) ^ (sh.Shooter.Pointer.ToInt64() << 3);
            float[] v;
            if (!baseCache.TryGetValue(key, out v))
            {
                v = new float[2];
                try
                {
                    string txt = Ui.GetPartInfoText(sh.Pd, sh.Shooter, 0, true);
                    if (!string.IsNullOrEmpty(txt))
                        foreach (var raw in txt.Split('\n'))
                        {
                            string line = Strip(raw).Trim();
                            if (line.StartsWith("Damage AP", StringComparison.OrdinalIgnoreCase)) v[0] = Num(line);
                            else if (line.StartsWith("Damage HE", StringComparison.OrdinalIgnoreCase)) v[1] = Num(line);
                        }
                }
                catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] base damage: " + e.Message); }
                if (baseCache.Count > 2000) baseCache.Clear();
                baseCache[key] = v;
            }
            return he ? (v[1] > 0f ? v[1] : v[0]) : (v[0] > 0f ? v[0] : v[1]);
        }
        static string Strip(string s) { var sb = new System.Text.StringBuilder(s.Length); bool tag = false; foreach (char c in s) { if (c == '<') { tag = true; continue; } if (c == '>' && tag) { tag = false; continue; } if (!tag) sb.Append(c); } return sb.ToString(); }
        static float Num(string line)
        {
            int a = line.IndexOf(':'); if (a < 0) return 0f; int i = a + 1; var sb = new System.Text.StringBuilder();
            while (i < line.Length && line[i] == ' ') i++;
            while (i < line.Length && (char.IsDigit(line[i]) || line[i] == ',' || line[i] == '.')) { if (line[i] != ',') sb.Append(line[i]); i++; }
            float v; return float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        // called from OverPen.Deal with the hit type known; returns true when the damage was replaced (the pass-through rule then
        // applies to the new value inside)
        internal static bool Apply(Ship ship, string type, bool useHe, string loc, float overFrac, ref float damage, out string note)
        {
            note = null;
            if (On == null || !On.Value || ship == null || type == null) return false;
            try
            {
                var sh = PhysPen.ShotOf(PhysPen.Current); if (sh == null) return false;
                float b = Base(sh, useHe); if (!(b > 0f)) return false;
                float f = Factor(loc);
                float raw = damage; float want;
                if (type == "normal") want = b * f;
                else if (type == "over") want = b * f * overFrac;
                else if (type == "armor" && useHe && (loc.StartsWith("turret") || loc.StartsWith("upper works"))) want = b * f;
                else return false;
                damage = want;
                note = $"{type}{(useHe ? " HE" : " AP")} at {loc}: base {b:0} x {f:0.00}{(type == "over" ? $" x {overFrac:0.00}" : "")} = {want:0} (game {raw:0})";
                if (logged++ < 40) MelonLogger.Msg($"[DIP GG Fixes][Damage] historical model {ship.name}: {sh.G.CalMm:0.#} mm {note}");
                Tally(ship, $"{sh.G.CalMm:0} mm {type}{(useHe ? " HE" : " AP")}", want); // 1.11.350 (#73)
                return true;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] model: " + e.Message); return false; }
        }
    }
}
