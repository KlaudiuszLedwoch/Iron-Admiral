// 1.11.492 (#116, author 31.10 08:39: "the weight matters too - it can change a lot whether the inner belts are heavy or light
// against the main belt"): log only. Once for every design opened in the ship designer, each armour zone the design has (belt,
// inner belts, deck, inner decks, ends) gets +10 mm through the game's own Ship.SetArmor, the game's weight (Ship.Weight) is read,
// and the zone is set back at once. The log line gives tons per 10 mm of each zone and, with the game's own multipliers for the
// inner belts (2.2x and 3x by the designer tooltips), the effective millimetres per ton - so the trade-off in the current game is
// known in numbers before the citadel layers get physical rules.
using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ArmorWeightProbe
    {
        static readonly HashSet<IntPtr> done = new HashSet<IntPtr>();
        static IntPtr seen; static float seenAt; static int errors;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ArmorWeight] " + m); }
        // zone, label, the game's own effective-thickness factor (designer tooltips: 1st inner belt 2.2x, 2nd 3x; others 1x)
        static readonly (Ship.A z, string n, float k)[] Zones =
        {
            (Ship.A.Belt, "main belt", 1f), (Ship.A.InnerBelt_1st, "1st inner belt", 2.2f), (Ship.A.InnerBelt_2nd, "2nd inner belt", 3f), (Ship.A.InnerBelt_3rd, "3rd inner belt", 1f),
            (Ship.A.Deck, "main deck", 1f), (Ship.A.InnerDeck_1st, "1st inner deck", 1f), (Ship.A.InnerDeck_2nd, "2nd inner deck", 1f), (Ship.A.InnerDeck_3rd, "3rd inner deck", 1f),
            (Ship.A.BeltBow, "fore belt", 1f), (Ship.A.BeltStern, "aft belt", 1f),
        };

        internal static void Tick()
        {
            if (errors > 5) return;
            try
            {
                bool designer = false; try { designer = GameManager.IsConstructor; } catch { }
                if (!designer) { seen = IntPtr.Zero; return; }
                Ship s = null; try { s = G.ui != null ? G.ui.mainShip : null; } catch { }
                if (s == null || s.armor == null) return;
                if (s.Pointer != seen) { seen = s.Pointer; seenAt = Time.unscaledTime; return; }
                if (Time.unscaledTime - seenAt < 3f || done.Contains(s.Pointer)) return;
                done.Add(s.Pointer);
                Probe(s);
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        static void Probe(Ship s)
        {
            float w0 = s.Weight(true, true);
            var sb = new StringBuilder(); int n = 0;
            foreach (var (z, name, k) in Zones)
            {
                float v; if (!s.armor.TryGetValue(z, out v)) continue;
                float dw = float.NaN;
                try
                {
                    s.SetArmor(z, v + 10f, true);
                    dw = s.Weight(true, true) - w0;
                }
                catch (Exception e) { sb.Append($"{name}: failed ({e.GetType().Name}); "); }
                finally { try { s.SetArmor(z, v, true); } catch { } }
                if (float.IsNaN(dw)) continue;
                n++;
                string eff = dw > 0.01f ? $", {10f * k / dw:0.0} effective mm per t{(k != 1f ? $" (game factor {k:0.#}x)" : "")}" : "";
                sb.Append($"{name} {v:0} mm: +10 mm = {dw:0.0} t{eff}; ");
            }
            float w1 = 0f; try { w1 = s.Weight(true, true); } catch { }
            string sn = "?"; try { sn = s.name; } catch { }
            Log($"{sn} ({w0:0} t): {(n == 0 ? "no armour zones read" : sb.ToString())}{(Math.Abs(w1 - w0) > 0.5f ? $" WARNING: weight after the probe {w1:0} t differs" : "")}");
        }
    }
}
