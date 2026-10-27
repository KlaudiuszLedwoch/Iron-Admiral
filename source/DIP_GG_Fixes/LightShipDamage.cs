// 1.11.322 (#67, author's order 27.10, 20:32 - forum reports: torpedo boats "unkillable", 2-4 in guns do nothing, 12 in AP and HE
// barely scratch them): light ships take damage in proportion to their size again.
// Test 27.10 (1027-071802): TB Hasty took 72 hits from 2 to 12 in (50 full penetrations, about 9,900 damage points in the hit log)
// and ended at 6 % damage. The game sizes a ship's structure (the sum of its sections' HP) from the ship type's HP curve, which gives
// small ships far more structure per ton than battleships; the game's own hit damage made up for it by multiplying the damage with
// the margin of penetration over the plate (up to x11 on unarmoured hulls). The historical damage model (1.11.278) took that
// multiplier away, so light ships kept their large structure pool and lost the large hits.
// 1.11.323 (author 27.10, 20:37: "CA, BC and BB too - proportional to tonnage everywhere, regardless of class"): every ship's
// structure counts as proportional to its tonnage. Each hit deals damage x (the ship's structure per ton / the reference structure
// per ton), the reference being that of the battleships and battlecruisers in the battle (tonnage-weighted; the last value seen is
// kept for battles without them), between DamageByTonnageMin and DamageByTonnageMax. A shell then takes away the same share of any
// ship's tonnage: a 6 in penetration that costs a 25,000 t battleship 0.5 % of its structure costs a 1,000 t destroyer about 12 %.
// Switches: DamageByTonnage, DamageByTonnageMin, DamageByTonnageMax, DamageByTonnageTypes (F10, Battle). Log: one line per ship at
// its first hit, the reference, and a battle summary.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class LightShipDamage
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Max, Min, RefSaved;
        internal static MelonPreferences_Entry<string> Types;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DamageByTonnage", true, null, "Battle: every ship takes damage in proportion to its tonnage, whatever its class - each hit is multiplied by the ship's structure per ton over that of the battleships in the battle (the game gives small ships far more structure per ton, and the historical damage model no longer multiplies hits on thin plates)");
            Min = cat.CreateEntry("DamageByTonnageMin", 0.5f, null, "Battle: the smallest damage multiplier (ships with less structure per ton than the battleships)");
            Max = cat.CreateEntry("DamageByTonnageMax", 12f, null, "Battle: the largest damage multiplier (small ships)");
            Types = cat.CreateEntry("DamageByTonnageTypes", "all", null, "Battle: ship types the rule applies to - all, or a comma-separated list (tb, dd, cl, ca, bc, bb, tr)");
            RefSaved = cat.CreateEntry("DamageByTonnageRefHpPerTon", 0f, null, "Battle (kept by the mod): structure per ton of the battleships last seen in a battle - used when a battle has none");
        }

        static readonly Dictionary<long, float> mult = new Dictionary<long, float>();
        static readonly SortedDictionary<string, float[]> byType = new SortedDictionary<string, float[]>();
        static float refHpt; static string refFrom; static int logged, errors, applied;
        static float sumRaw, sumNew;
        internal static void BattleStart() { mult.Clear(); byType.Clear(); refHpt = 0f; refFrom = null; logged = 0; applied = 0; sumRaw = 0f; sumNew = 0f; }

        static string TypeOf(Ship s) { try { return s.shipType.name.ToLowerInvariant(); } catch { return ""; } }
        static bool Applies(string t)
        {
            string v = (Types != null ? Types.Value : "all") ?? "all";
            if (v.Trim().ToLowerInvariant() == "all") return true;
            if (string.IsNullOrEmpty(t)) return false;
            foreach (var x in v.Split(',')) if (x.Trim().ToLowerInvariant() == t) return true;
            return false;
        }
        static float HpPerTon(Ship s, out float hp, out float tons)
        {
            hp = 0f; tons = 0f;
            try { hp = s.GetAllSectionsCalculateHP(false, false); } catch { }
            try { tons = s.Tonnage(); } catch { }
            return hp > 0f && tons > 0f ? hp / tons : 0f;
        }
        // structure per ton of the battleships and battlecruisers in the battle (tonnage-weighted)
        static float Reference()
        {
            if (refHpt > 0f) return refHpt;
            float hpSum = 0f, tSum = 0f; int n = 0;
            try
            {
                var all = Ship.AllShips;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var s = all[i]; if (s == null) continue; string t = TypeOf(s);
                        if (t != "bb" && t != "bc") continue;
                        float hp, tons; if (HpPerTon(s, out hp, out tons) > 0f) { hpSum += hp; tSum += tons; n++; }
                    }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] by tonnage, reference: " + e.Message); }
            if (n > 0 && tSum > 0f)
            {
                refHpt = hpSum / tSum; refFrom = $"battleships in this battle ({n})";
                try { if (RefSaved != null && Math.Abs(RefSaved.Value - refHpt) > 1e-4f) RefSaved.Value = refHpt; } catch { }
            }
            else if (RefSaved != null && RefSaved.Value > 0f) { refHpt = RefSaved.Value; refFrom = "saved from an earlier battle"; }
            if (refHpt > 0f) MelonLogger.Msg($"[DIP GG Fixes][Damage] by tonnage: reference structure {refHpt:0.##} per ton ({refFrom})");
            return refHpt;
        }

        internal static float For(Ship ship)
        {
            long key = ship.Pointer.ToInt64(); float m;
            if (mult.TryGetValue(key, out m)) return m;
            m = 1f;
            string t = TypeOf(ship);
            if (Applies(t))
            {
                float hp, tons; float hpt = HpPerTon(ship, out hp, out tons);
                float r = Reference();
                if (hpt > 0f && r > 0f) m = Math.Max(Min != null ? Min.Value : 0.5f, Math.Min(Max != null ? Max.Value : 12f, hpt / r));
                if (logged++ < 40) MelonLogger.Msg($"[DIP GG Fixes][Damage] by tonnage {ship.name} ({t}): structure {hp:0} for {tons:0} t = {hpt:0.##} per ton; reference {r:0.##} - damage x{m:0.00}");
            }
            mult[key] = m;
            return m;
        }

        // last step of the damage path (OverPen.Deal), after the historical model and the pass-through rule
        internal static void Apply(Ship ship, ref float damage)
        {
            if (On == null || !On.Value || ship == null || !(damage > 0f)) return;
            try
            {
                float m = For(ship); if (Math.Abs(m - 1f) < 0.001f) return;
                float raw = damage; damage *= m; sumRaw += raw; sumNew += damage; applied++;
                string t = TypeOf(ship); float[] a; if (!byType.TryGetValue(t, out a)) { a = new float[3]; byType[t] = a; }
                a[0]++; a[1] += raw; a[2] += damage;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] by tonnage: " + e.Message); }
        }
        internal static void Report()
        {
            if (applied == 0) return;
            var sb = new System.Text.StringBuilder($"[DIP GG Fixes][Damage] battle summary - damage by tonnage: {applied} hits, damage {sumRaw:0} -> {sumNew:0}; by type (hits | damage before | after):");
            foreach (var kv in byType) sb.Append($"\n   {kv.Key} | {kv.Value[0]:0} | {kv.Value[1]:0} | {kv.Value[2]:0}");
            MelonLogger.Msg(sb.ToString());
        }
    }
}
