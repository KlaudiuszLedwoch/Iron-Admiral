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
// kept for battles without them), between DamageByTonnageMin and DamageByTonnageMax.
// 1.11.324 (tests 27.10, 21:33-22:27): the game gives a 635 t torpedo boat 12,594 structure (19.8 per ton) against 11,787 for a
// 25,796 t battleship (0.46 per ton) - x43; with the x12 cap of 1.11.323 torpedo boats ended at 1-15 % after up to 13 hits. The cap is
// now x60: two battles of 4 torpedo boats - 3 sunk after 10, 16 and 21 hits, the others wrecks (81-96 %) after 14-20 hits. A shell then destroys the same amount of tonnage in any
// ship: a 6 in penetration that costs a 25,000 t battleship 0.5 % of its structure costs a 1,000 t destroyer about 12 %.
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
        internal static MelonPreferences_Entry<float> Max, Min, RefSaved, SinkPct, DestroyedPenalty;
        internal static MelonPreferences_Entry<string> Types;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DamageByTonnage", true, null, "Battle: every ship takes damage in proportion to its tonnage, whatever its class - each hit is multiplied by the ship's structure per ton over that of the battleships in the battle (the game gives small ships far more structure per ton, and the historical damage model no longer multiplies hits on thin plates)");
            Min = cat.CreateEntry("DamageByTonnageMin", 0.5f, null, "Battle: the smallest damage multiplier (ships with less structure per ton than the battleships)");
            Max = cat.CreateEntry("DamageByTonnageMax", 60f, null, "Battle: the largest damage multiplier (small ships; a 635 t torpedo boat has 43x the structure per ton of a battleship in the game)");
            Types = cat.CreateEntry("DamageByTonnageTypes", "all", null, "Battle: ship types the rule applies to - all, or a comma-separated list (tb, dd, cl, ca, bc, bb, tr)");
            RefSaved = cat.CreateEntry("DamageByTonnageRefHpPerTon", 0f, null, "Battle (kept by the mod): structure per ton of the battleships last seen in a battle - used when a battle has none");
            SinkPct = cat.CreateEntry("StructureSinkPercent", -1f, null, "Battle: structure damage (percent of the whole structure) at which a ship sinks - the game parameter structure_sink (vanilla 40, DIP GG 100); -1 = the mod's default 40 (tests 27-28.10: with 100 destroyers survived 136 hits, with 40 torpedo boats, destroyers, cruisers and battleships sink after as many hits as in history); 100 = DIP GG");
            DestroyedPenalty = cat.CreateEntry("DestroyedSectionDamagePenalty", -1f, null, "Battle: share of damage a hit still does to a section that is already destroyed - the game parameter destroyed_section_damage_penalty (DIP GG 0.15); -1 = keep the game value");
        }

        static readonly Dictionary<long, float> mult = new Dictionary<long, float>();
        static readonly SortedDictionary<string, float[]> byType = new SortedDictionary<string, float[]>();
        static float refHpt; static string refFrom; static int logged, errors, applied;
        static float sumRaw, sumNew; static int skippedTorp; internal static int InTorpedo; /* >0 while a torpedo resolves its hit (Torpedo.Update/FixedUpdate) */ static readonly SortedDictionary<string, int> kinds = new SortedDictionary<string, int>();
        internal static void BattleStart() { SetParams(); mult.Clear(); byType.Clear(); refHpt = 0f; refFrom = null; logged = 0; applied = 0; sumRaw = 0f; sumNew = 0f; skippedTorp = 0; kinds.Clear(); }

        // 1.11.324: the game parameters that decide when accumulated structure damage sinks a ship (tests of #67)
        static void SetParams()
        {
            try
            {
                var p = G.GameData.parms; if (p == null) return; var sb = new System.Text.StringBuilder();
                foreach (var kv in new[] { new KeyValuePair<string, MelonPreferences_Entry<float>>("structure_sink", SinkPct), new KeyValuePair<string, MelonPreferences_Entry<float>>("destroyed_section_damage_penalty", DestroyedPenalty) })
                {
                    float cur = 0f; bool has = p.ContainsKey(kv.Key); if (has) cur = p[kv.Key];
                    // 1.11.326: structure_sink -1 = the mod's default 40 (vanilla value; DIP GG 100 kept destroyers afloat after 136 hits)
                    float want = kv.Value != null ? kv.Value.Value : -1f; if (want < 0f && kv.Key == "structure_sink") want = 40f;
                    if (want >= 0f && (!has || Math.Abs(cur - want) > 1e-4f)) { p[kv.Key] = want; sb.Append($"{kv.Key} {cur:0.##} -> {want:0.##}; "); }
                    else sb.Append($"{kv.Key} {cur:0.##}; ");
                }
                MelonLogger.Msg("[DIP GG Fixes][Damage] sinking parameters: " + sb);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] sinking parameters: " + e.Message); }
        }
        static string TypeOf(Ship s) { try { return s.shipType.name.ToLowerInvariant(); } catch { return ""; } }
        static bool Applies(string t)
        {
            string v = (Types != null ? Types.Value : "all") ?? "all";
            if (v.Trim().ToLowerInvariant() == "all") return true;
            if (string.IsNullOrEmpty(t)) return false;
            foreach (var x in v.Split(',')) if (x.Trim().ToLowerInvariant() == t) return true;
            return false;
        }
        internal static float Structure(Ship s) { try { HpPerTon(s, out float hp, out float t); return hp; } catch { return 0f; } } // 1.11.350
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
            // 1.11.328 (test 28.10, 03:30: a cruiser battle with no battleship and nothing saved had reference 0 - every ship x1, the
            // fix did nothing): the structure per ton of the battleships in the tests (Konig 0.46, Revenge 0.36; four battleships 0.40)
            else { refHpt = 0.40f; refFrom = "fixed value (battleships in the tests 27-28.10)"; }
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
                if (hpt > 0f && r > 0f) m = Math.Max(Min != null ? Min.Value : 0.5f, Math.Min(Max != null ? Max.Value : 60f, hpt / r));
                if (logged++ < 40) MelonLogger.Msg($"[DIP GG Fixes][Damage] by tonnage {ship.name} ({t}): structure {hp:0} for {tons:0} t = {hpt:0.##} per ton; reference {r:0.##} - damage x{m:0.00}");
            }
            mult[key] = m;
            return m;
        }

        // last step of the damage path (OverPen.Deal), after the historical model and the pass-through rule
        internal static IntPtr CurTorp;
        internal static void Apply(Ship ship, ref float damage, string kind)
        {
            // 1.11.385 (#85, author's custom battle 29.10 16:55: five torpedoes of the first wave of Camels sank the battleship Kaga, hits of
            // up to 17,500): a torpedo dropped by an aircraft hits with an aerial warhead - a share of the ship torpedo's damage by year
            try { if (ship != null && damage > 0f && InTorpedo > 0 && AirStrike.IsAerial(CurTorp)) { damage *= AirStrike.WarheadFactor(ship); AirStrike.OnAerialHit(ship); } } catch { }
            if (On == null || !On.Value || ship == null || !(damage > 0f)) return;
            try
            {
                // 1.11.325 (test 27.10, 22:52): a torpedo is not multiplied - one torpedo sank a 14,786 t armoured cruiser (game damage
                // 11,359 x1.88 = 21,375 > its whole structure 12,714). The game's torpedo damage is already the damage of a warhead of
                // the torpedo technologies (HistoricalDamage) and it is about one battleship's whole structure.
                string k = kind ?? "?"; int kc; kinds.TryGetValue(k, out kc); kinds[k] = kc + 1;
                if (InTorpedo > 0) { k = "torpedo"; kinds.TryGetValue(k, out kc); kinds[k] = kc + 1; skippedTorp++; return; }
                float m = For(ship); if (Math.Abs(m - 1f) < 0.001f) return;
                float raw = damage; damage *= m; sumRaw += raw; sumNew += damage; applied++;
                string t = TypeOf(ship); float[] a; if (!byType.TryGetValue(t, out a)) { a = new float[3]; byType[t] = a; }
                a[0]++; a[1] += raw; a[2] += damage;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Damage] by tonnage: " + e.Message); }
        }
        internal static void Report()
        {
            if (applied == 0 && skippedTorp == 0) return;
            var sb = new System.Text.StringBuilder($"[DIP GG Fixes][Damage] battle summary - damage by tonnage: {applied} hits, damage {sumRaw:0} -> {sumNew:0}; torpedo hits not multiplied {skippedTorp}; game damage types {string.Join(", ", System.Linq.Enumerable.Select(kinds, x => x.Key + " " + x.Value))}; by type (hits | damage before | after):");
            foreach (var kv in byType) sb.Append($"\n   {kv.Key} | {kv.Value[0]:0} | {kv.Value[1]:0} | {kv.Value[2]:0}");
            MelonLogger.Msg(sb.ToString());
        }
    }

    // 1.11.325: a torpedo deals its damage from inside its own Update / FixedUpdate (collision with a ship); the game's damage type
    // has no torpedo member (none, gunDamage, ammoExplosionDamage, fireDamage, floadingDamage, rammingDamage)
    [HarmonyLib.HarmonyPatch(typeof(Torpedo), "Update")]
    internal static class Patch_TorpedoUpdateMark
    {
        private static void Prefix(Torpedo __instance) { LightShipDamage.InTorpedo++; try { LightShipDamage.CurTorp = __instance != null ? __instance.Pointer : IntPtr.Zero; } catch { } }
        private static void Finalizer() { if (LightShipDamage.InTorpedo > 0) LightShipDamage.InTorpedo--; }
    }
    [HarmonyLib.HarmonyPatch(typeof(Torpedo), "FixedUpdate")]
    internal static class Patch_TorpedoFixedUpdateMark
    {
        private static void Prefix(Torpedo __instance) { LightShipDamage.InTorpedo++; try { LightShipDamage.CurTorp = __instance != null ? __instance.Pointer : IntPtr.Zero; } catch { } }
        private static void Finalizer() { if (LightShipDamage.InTorpedo > 0) LightShipDamage.InTorpedo--; }
    }
}