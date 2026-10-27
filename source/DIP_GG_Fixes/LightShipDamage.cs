// 1.11.322 (#67, author's order 27.10, 20:32 - forum reports: torpedo boats "unkillable", 2-4 in guns do nothing, 12 in AP and HE
// barely scratch them): torpedo boats, destroyers and light cruisers take damage in proportion to their size again.
// Test 27.10 (1027-071802): TB Hasty took 72 hits from 2 to 12 in (50 full penetrations, about 9,900 damage points in the hit log)
// and ended at 6 % damage. The game sizes a ship's structure (the sum of its sections' HP) from the ship type's HP curve, which gives
// small ships far more structure per ton than battleships; the game's own hit damage made up for it by multiplying the damage with
// the margin of penetration over the plate (up to x11 on unarmoured hulls). The historical damage model (1.11.278) took that
// multiplier away, so light ships kept their large structure pool and lost the large hits.
// Now: the structure per ton of a light ship is compared with that of the capital ships in the battle (bb, bc, ca; the last value
// seen is kept for battles without them), and every hit on the light ship deals damage x (its structure per ton / the reference),
// at least x1 and at most LightShipDamageMax. A shell then takes away the same share of a light ship's tonnage as of a battleship's:
// a 6 in penetration that costs a 25,000 t battleship 0.5 % of its structure costs a 1,000 t destroyer about 12 %.
// Switches: LightShipDamage, LightShipDamageMax, LightShipTypes (F10, Battle). Log: one line per light ship at its first hit.
using System;
using System.Collections.Generic;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class LightShipDamage
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Max, RefSaved;
        internal static MelonPreferences_Entry<string> Types;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("LightShipDamage", true, null, "Battle: torpedo boats, destroyers and light cruisers take damage in proportion to their size - every hit on them is multiplied by their structure per ton over that of the capital ships in the battle (the game gives small ships far more structure per ton, and the historical damage model no longer multiplies hits on thin plates)");
            Max = cat.CreateEntry("LightShipDamageMax", 12f, null, "Battle: the largest damage multiplier for a light ship");
            Types = cat.CreateEntry("LightShipTypes", "tb,dd,cl", null, "Battle: ship types counted as light ships (comma-separated: tb, dd, cl, ca, tr)");
            RefSaved = cat.CreateEntry("LightShipRefHpPerTon", 0f, null, "Battle (kept by the mod): structure per ton of the capital ships last seen in a battle - used when a battle has none");
        }

        static readonly Dictionary<long, float> mult = new Dictionary<long, float>();
        static float refHpt; static int logged, errors, applied;
        static float sumRaw, sumNew;
        internal static void BattleStart() { mult.Clear(); refHpt = 0f; logged = 0; applied = 0; sumRaw = 0f; sumNew = 0f; }

        static string TypeOf(Ship s) { try { return s.shipType.name.ToLowerInvariant(); } catch { return ""; } }
        static bool IsLight(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            foreach (var x in (Types != null ? Types.Value : "tb,dd,cl").Split(',')) if (x.Trim().ToLowerInvariant() == t) return true;
            return false;
        }
        static float HpPerTon(Ship s, out float hp, out float tons)
        {
            hp = 0f; tons = 0f;
            try { hp = s.GetAllSectionsCalculateHP(false, false); } catch { }
            try { tons = s.Tonnage(); } catch { }
            return hp > 0f && tons > 0f ? hp / tons : 0f;
        }
        // structure per ton of the capital ships in the battle (tonnage-weighted)
        static float Reference(out string from)
        {
            from = "saved";
            if (refHpt > 0f) { from = "capital ships in this battle"; return refHpt; }
            float hpSum = 0f, tSum = 0f; int n = 0;
            try
            {
                var all = Ship.AllShips;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var s = all[i]; if (s == null) continue; string t = TypeOf(s);
                        if (t != "bb" && t != "bc" && t != "ca") continue;
                        float hp, tons; if (HpPerTon(s, out hp, out tons) > 0f) { hpSum += hp; tSum += tons; n++; }
                    }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] light ships, reference: " + e.Message); }
            if (n > 0 && tSum > 0f)
            {
                refHpt = hpSum / tSum; from = $"capital ships in this battle ({n})";
                try { if (RefSaved != null && Math.Abs(RefSaved.Value - refHpt) > 1e-4f) RefSaved.Value = refHpt; } catch { }
                return refHpt;
            }
            return RefSaved != null ? RefSaved.Value : 0f;
        }

        internal static float For(Ship ship)
        {
            long key = ship.Pointer.ToInt64(); float m;
            if (mult.TryGetValue(key, out m)) return m;
            m = 1f;
            string t = TypeOf(ship);
            if (IsLight(t))
            {
                float hp, tons; float hpt = HpPerTon(ship, out hp, out tons);
                string from; float r = Reference(out from);
                if (hpt > 0f && r > 0f) m = Math.Max(1f, Math.Min(Max != null ? Max.Value : 12f, hpt / r));
                if (logged++ < 30) MelonLogger.Msg($"[DIP GG Fixes][Damage] light ship {ship.name} ({t}): structure {hp:0} for {tons:0} t = {hpt:0.##} per ton; reference {r:0.##} per ton ({from}) - damage x{m:0.00}");
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
                float m = For(ship); if (m <= 1.001f) return;
                sumRaw += damage; damage *= m; sumNew += damage; applied++;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] light ships: " + e.Message); }
        }
        internal static void Report()
        {
            if (applied == 0) return;
            MelonLogger.Msg($"[DIP GG Fixes][Damage] battle summary - light ships: {applied} hits multiplied, damage {sumRaw:0} -> {sumNew:0}");
        }
    }
}
