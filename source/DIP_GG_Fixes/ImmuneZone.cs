// 1.11.496 (#117, author 31.10, 10:17): the immune zone - the band of range in which a ship's citadel defeats a given gun: beyond the
// inner limit the belt is no longer pierced, up to the outer limit the deck still holds (from about 1929 the US Navy wrote its
// battleship requirements as an immune zone against a named gun - NavWeaps tech-069; Iowa against the 16"/45 with the 2,700 lb shell
// 20,400-26,700 yd - Naval Gazing, Armor Part 4).
// Computed from the same physics the hits use: the gun's shell (PhysGun: weight, muzzle velocity, shell quality, drag) on its arc
// (Ballistics.Impact at the mount elevation of its generation), belt penetration at normal impact against the belt sloped by the
// angle of fall and by the target angle in plan, deck penetration (Thompson F) against the deck; both plates times their quality
// relative to Krupp cemented (ArmorQuality, from the ship's armour technology). Two target angles: beam-on (90 degrees) and 60
// degrees (the US Navy's cruiser analyses). One zone for the citadel (main belt and main deck; inner layers follow #116).
// Shown: in the designer against a calibre the player picks (the gun mark of the design's own technologies), and in battle in the
// range tag of every identified enemy (our selected ship against that enemy's main battery). Switch: ImmuneZone.
using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class ImmuneZone
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ImmuneZone", true, null, "Immune zone: the band of range in which the citadel (main belt and deck) defeats a gun - shown in the designer against a calibre you pick and in battle in the range tag of every identified enemy (our ship against his main battery)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ImmuneZone] " + m); }
        internal struct Z { public bool Ok, Any; public float Inner, Outer, Max; public bool ToMax; public float Belt, Deck; public int Grade; public float CalMm; public float BeltFrom = -1f, DeckTo = -1f; }

        static readonly float[] histF = { 0.40f, 0.50f, 0.60f, 0.80f, 1.00f, 1.03f, 1.05f, 1.07f, 1.08f, 1.10f, 1.12f };
        // armour quality relative to Krupp cemented from the ship's armour component (armor_0 .. armor_10 = armor_quality_1 .. 11)
        internal static float Quality(Ship s)
        {
            try
            {
                var cl = s.components;
                if (cl != null) foreach (var kv in cl)
                    {
                        var cd = kv.Value; if (cd == null || cd.name == null || !cd.name.StartsWith("armor_", StringComparison.Ordinal)) continue;
                        int k; if (int.TryParse(cd.name.Substring(6), out k) && k >= 0 && k < histF.Length) return histF[k];
                    }
            }
            catch { }
            return 1f;
        }
        static float Plate(Ship s, Ship.A zone) { try { var a = s.armor; float v; if (a != null && a.TryGetValue(zone, out v)) return v; } catch { } return 0f; }

        // target: the ship whose citadel is checked; gun: the shooting gun with the technologies of 'shooter'
        internal static Z Compute(Ship target, PartData gun, Ship shooter, float targetAngleDeg)
        {
            var z = new Z();
            if (target == null || gun == null || shooter == null) return z;
            PhysGun.G g; try { g = PhysGun.Get(gun, shooter, Ship.ShellType.Ap); } catch { return z; }
            if (!g.Ok || g.Kg <= 0f || g.Mv <= 0f) return z;
            float q = Quality(target);
            z.Belt = Plate(target, Ship.A.Belt) * q; z.Deck = Plate(target, Ship.A.Deck) * q; z.Grade = g.Grade; z.CalMm = g.CalMm;
            float cd = Ballistics.CdOf(shooter), elev = MountElevation.Elev(Math.Max(1, g.Grade));
            float plan = Mathf.Sin(Mathf.Clamp(targetAngleDeg, 10f, 90f) * Mathf.Deg2Rad);
            float inner = -1f, outer = -1f, max = 0f; bool beltHeldAll = true;
            for (float r = 500f; r <= 45000f; r += 250f)
            {
                float vs, fall, tt; bool beyond;
                Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, cd, r, elev, out vs, out fall, out tt, out beyond);
                if (beyond) break;
                max = r;
                float bp = PhysPen.BeltPen(g.Kg, vs, fall, g.Q, g.CalMm);
                float effBelt = z.Belt / Mathf.Max(0.05f, Mathf.Cos(Mathf.Clamp(fall, 0f, 85f) * Mathf.Deg2Rad) * plan);
                bool beltHolds = z.Belt > 0.5f && bp < effBelt;
                bool deckHolds = z.Deck > 0.5f && PhysPen.DeckPen(g.Kg, g.CalMm, vs, fall) < z.Deck;
                if (beltHolds && z.BeltFrom < 0f) z.BeltFrom = r; if (!beltHolds) z.BeltFrom = -1f;
                if (!deckHolds && z.DeckTo < 0f) z.DeckTo = r;
                if (beltHolds && inner < 0f) inner = r;
                if (!beltHolds) { inner = -1f; beltHeldAll = false; }   // the belt must hold from the inner limit outwards
                if (deckHolds) outer = r; else if (inner >= 0f) break;   // the first deck penetration beyond the inner limit closes the zone
            }
            z.Ok = true; z.Max = max;
            if (inner >= 0f && outer >= inner) { z.Any = true; z.Inner = inner; z.Outer = outer; z.ToMax = outer >= max - 1f; }
            return z;
        }
        // why there is no zone: where the belt starts to hold and where the deck stops holding
        internal static string Why(Z z)
        {
            string b = z.BeltFrom < 0f ? "belt pierced out to his reach" : $"belt holds from {z.BeltFrom / 1000f:0.0} km";
            string d = z.DeckTo < 0f ? "deck holds" : $"deck pierced from {z.DeckTo / 1000f:0.0} km";
            return $" ({b}, {d})";
        }
        internal static string Text(Z z)
        {
            if (!z.Ok) return "-";
            if (!z.Any) return "none" + Why(z);
            return $"{z.Inner / 1000f:0.0}-{(z.ToMax ? $"{z.Outer / 1000f:0.0} (gun's max)" : $"{z.Outer / 1000f:0.0}")} km";
        }

        // ------------------------------------------------------------------ battle: cached per pair
        class C { public Z Z; public float At; }
        static readonly Dictionary<string, C> cache = new Dictionary<string, C>(); static IntPtr cacheBattle; static int pairLogs;
        internal static Z Battle(Ship own, Ship enemy)
        {
            var z = new Z(); if (On == null || !On.Value || own == null || enemy == null) return z;
            try
            {
                IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb != null) b = cb.Pointer; } catch { }
                if (b != cacheBattle) { cacheBattle = b; cache.Clear(); pairLogs = 0; }
                string key = own.Pointer + "|" + enemy.Pointer;
                if (cache.TryGetValue(key, out var c) && Time.time - c.At < 30f) return c.Z;
                var gun = BattleAi.MainBatteryGun(enemy);
                z = Compute(own, gun, enemy, 90f);
                cache[key] = new C { Z = z, At = Time.time };
                if (pairLogs++ < 40) Log($"{own.name} against {enemy.name} ({z.CalMm / 25.4f:0.#} in Mk{z.Grade}): belt {z.Belt:0} mm, deck {z.Deck:0} mm (Krupp-equivalent) - immune zone beam-on {Text(z)}; gun's reach {z.Max / 1000f:0.0} km");
            }
            catch (Exception e) { if (errors++ < 5) Log("battle: " + e.Message); }
            return z;
        }
        internal static string Tag(Ship own, Ship enemy)
        {
            var z = Battle(own, enemy); if (!z.Ok) return "";
            if (!z.Any) return " · no IZ";
            return $" · IZ {z.Inner / 1000f:0}-{z.Outer / 1000f:0}{(z.ToMax ? "+" : "")}";
        }

        // ------------------------------------------------------------------ battle row under the gun orders (1.11.499, author 31.10, 11:17:
        // "no parameters of my ship or the enemy's"): the selected ship against its nearest identified enemy - his gun, our plates and
        // our zone, and the other way round (his plates against our main battery)
        static GUIStyle sRow;
        internal static void Row(Ship s, float x, float y, float w, float h)
        {
            if (On == null || !On.Value || s == null) return;
            x -= 200f; w += 400f;   // a wider row - the text carries both sides
            Ship e = Nearest(s);
            if (sRow == null) { sRow = new GUIStyle(); sRow.fontSize = 11; sRow.richText = true; sRow.normal.textColor = new Color(0.9f, 0.9f, 0.9f); }
            GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), "<b>Immune zone</b>", sRow);
            string txt;
            if (e == null) txt = "<color=#C0C0C0>no identified enemy in sight</color>";
            else
            {
                var z = Battle(s, e); var r = Battle(e, s);
                string en = e.name ?? "?"; int cut = en.IndexOf(" ("); if (cut > 0) en = en.Substring(0, cut);
                txt = $"vs {en} ({z.CalMm / 25.4f:0.#} in Mk{z.Grade}, reach {z.Max / 1000f:0} km): ours {Text(z)} - belt {z.Belt:0} / deck {z.Deck:0} mm   |   his vs our {r.CalMm / 25.4f:0.#} in: {Text(r)} - belt {r.Belt:0} / deck {r.Deck:0} mm";
            }
            GUI.Label(new Rect(x + 98f, y + 4f, w - 104f, 18f), txt, sRow);
        }
        static Ship Nearest(Ship s)
        {
            Ship best = null; float bd = float.MaxValue;
            try
            {
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || s.player == null) return null;
                var en = b.ShipsOf(b.OppositePlayer(s.player), true); if (en == null) return null;
                Vector3 p = s.transform.position;
                for (int i = 0; i < en.Count; i++)
                {
                    var e = en[i]; if (e == null || e.isDead || e.isSinking) continue;
                    bool vis = false; try { vis = e.IsVisibleFor(s); } catch { } if (!vis) continue;
                    try { if (Fog.PlayerLabel(e) != null) continue; } catch { }
                    float d = (e.transform.position - p).magnitude; if (d < bd) { bd = d; best = e; }
                }
            }
            catch { }
            return best;
        }

        // ------------------------------------------------------------------ designer panel
        static int errors; static float nextCalc; static string shown; static string shownKey; static int calIdx = -1;
        static readonly List<KeyValuePair<int, PartData>> calibres = new List<KeyValuePair<int, PartData>>(); static string calsFor;
        static GUIStyle sL;
        static void BuildCalibres(Ship s)
        {
            string key = s.Pointer.ToString(); if (calsFor == key && calibres.Count > 0) return;
            calsFor = key; calibres.Clear();
            var seen = new HashSet<int>();
            try
            {
                foreach (var kv in G.GameData.parts)
                {
                    var pd = kv.Value; if (pd == null) continue;
                    bool gun = false; try { gun = pd.isGun; } catch { } if (!gun) continue;
                    PhysGun.G g; try { g = PhysGun.Get(pd, s, Ship.ShellType.Ap); } catch { continue; }
                    if (!g.Ok || g.CalMm < 100f) continue;
                    int inch = Mathf.RoundToInt(g.CalMm / 25.4f);
                    if (seen.Add(inch)) calibres.Add(new KeyValuePair<int, PartData>(inch, pd));
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("calibres: " + e.Message); }
            calibres.Sort((a, b) => a.Key.CompareTo(b.Key));
            if (calIdx < 0 || calIdx >= calibres.Count) { calIdx = calibres.FindIndex(c => c.Key == 15); if (calIdx < 0) calIdx = calibres.Count - 1; }
        }
        internal static void OnGUI()
        {
            if (On == null || !On.Value) return;
            bool cons = false; try { cons = GameManager.IsConstructor; } catch { }
            if (!cons) { shown = null; shownKey = null; return; }
            Ship s = null; try { s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
            if (s == null) return;
            try
            {
                BuildCalibres(s);
                if (calibres.Count == 0) return;
                if (Time.unscaledTime >= nextCalc)
                {
                    nextCalc = Time.unscaledTime + 1f;
                    var pd = calibres[calIdx].Value;
                    string key = $"{s.Pointer}|{calIdx}|{Plate(s, Ship.A.Belt):0}|{Plate(s, Ship.A.Deck):0}|{Quality(s):0.00}";
                    if (key != shownKey)
                    {
                        shownKey = key;
                        var z90 = Compute(s, pd, s, 90f); var z60 = Compute(s, pd, s, 60f);
                        shown = $"Immune zone against {calibres[calIdx].Key} in Mk{z90.Grade}: beam-on {Text(z90)}, at 60° {Text(z60)}  (belt {z90.Belt:0} / deck {z90.Deck:0} mm Krupp-equivalent)";
                        Log($"designer {s.name}: {shown}");
                    }
                }
                float k = UiScale.F > 0.01f ? UiScale.F : 1f, W = Screen.width / k;
                if (sL == null) { sL = new GUIStyle(); sL.fontSize = 13; sL.alignment = TextAnchor.MiddleLeft; sL.normal.textColor = new Color(0.8f, 0.95f, 1f); }
                float w = 760f, x = (W - w) / 2f, y = 140f;
                GUI.Box(new Rect(x, y, w, 24f), "");
                if (GUI.Button(new Rect(x + 4f, y + 2f, 22f, 20f), "<")) { calIdx = Math.Max(0, calIdx - 1); shownKey = null; nextCalc = 0f; }
                if (GUI.Button(new Rect(x + 28f, y + 2f, 22f, 20f), ">")) { calIdx = Math.Min(calibres.Count - 1, calIdx + 1); shownKey = null; nextCalc = 0f; }
                GUI.Label(new Rect(x + 56f, y, w - 60f, 24f), shown ?? "", sL);
            }
            catch (Exception e) { if (errors++ < 5) Log("designer: " + e.Message); }
        }
    }
}
