// 1.11.462 (author 30.10 19:55: "I would like ships to sink more spectacularly - capital ships hit by torpedoes on one side should
// capsize, they should break in half, sometimes blow up - more fire and effects"): when a large ship (battleship, battlecruiser,
// heavy cruiser) starts to sink, the mod picks how she goes and draws it with the game's own effects:
//  - torpedoes on one side (two or more hits more on one beam): she lists to that side and rolls over, keel up, as she goes
//    (Barham, Royal Oak, Oklahoma);
//  - a magazine hit before she sank, or by chance: she blows up - flash fires in the gun houses one after another, turrets
//    thrown off (the game's FlyAway), and the hull plunges bow first (Hood, Invincible, Queen Mary);
//  - otherwise she breaks her back by chance - explosions amidships and a steep plunge - or settles burning;
//  - always: fires spread along the hull and secondary explosions for the first half minute.
// The real two-piece break of the hull needs a split model (not in the game); the plunge and explosions amidships stand for it.
// The extra roll and pitch are put on top of the game's own sinking motion every frame. Switch: SpectacularSinking.
using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Spectacle
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("SpectacularSinking", true, null, "Battle: large ships sink spectacularly - torpedoed on one side they capsize, a magazine hit blows them up (turrets thrown off, fires, bow-first plunge), others break their back or settle burning");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Sinking] " + m); }
        class S { public int Port, Stbd; public bool Magazine; public string Fate; public float Start, Roll, Pitch, Dur, NextBoom; public int Booms; public Quaternion Last = Quaternion.identity; }
        static readonly Dictionary<IntPtr, S> st = new Dictionary<IntPtr, S>();
        static IntPtr battle; static int errors, logged;
        static readonly System.Random rnd = new System.Random();

        static S Of(Ship s) { if (!st.TryGetValue(s.Pointer, out var x)) { x = new S(); st[s.Pointer] = x; } return x; }
        // from the damage path: a torpedo hit and its side
        internal static void Torpedo(Ship ship, IntPtr torp)
        {
            if (On == null || !On.Value || ship == null || torp == IntPtr.Zero) return;
            try
            {
                var t = new Il2Cpp.Torpedo(torp); var d = t.transform.position - ship.transform.position;
                if (Vector3.Dot(d, ship.transform.right) >= 0f) Of(ship).Stbd++; else Of(ship).Port++;
            }
            catch { }
        }
        internal static void Magazine(Ship ship) { if (ship != null) try { Of(ship).Magazine = true; } catch { } }

        static bool Big(Ship s) { string t = ""; try { t = s.shipType != null ? s.shipType.name : ""; } catch { } float ton = 0f; try { ton = s.Tonnage(); } catch { } return t == "bb" || t == "bc" || t == "ca" || ton >= 9000f; }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool b = false; try { b = GameManager.IsBattle; } catch { }
                if (!b) { if (st.Count > 0) st.Clear(); battle = IntPtr.Zero; return; }
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null || cb.Ships == null) return;
                if (cb.Pointer != battle) { battle = cb.Pointer; st.Clear(); }
                for (int i = 0; i < cb.Ships.Count; i++)
                {
                    var s = cb.Ships[i]; if (s == null) continue;
                    bool sinking = false; try { sinking = s.isSinking; } catch { }
                    if (!sinking) continue;
                    var x = Of(s);
                    if (x.Fate == null) Begin(s, x);
                    if (x.Fate == "none") continue;
                    float age = Time.time - x.Start;
                    if (age < 30f && Time.time >= x.NextBoom && x.Booms < 8) { x.NextBoom = Time.time + 2.5f + (float)rnd.NextDouble() * 4f; x.Booms++; Boom(s, x.Fate == "explode" && x.Booms <= 4); }
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
        static void Begin(Ship s, S x)
        {
            x.Start = Time.time; x.NextBoom = Time.time + 0.5f;
            if (!Big(s)) { x.Fate = "none"; return; }
            int asym = x.Stbd - x.Port; double r = rnd.NextDouble();
            if (Math.Abs(asym) >= 2 || (x.Port + x.Stbd >= 2 && Math.Abs(asym) >= 1 && r < 0.6)) { x.Fate = "capsize"; x.Roll = (asym > 0 ? -1f : 1f) * (130f + (float)rnd.NextDouble() * 40f); x.Pitch = 4f; x.Dur = 45f + (float)rnd.NextDouble() * 25f; }
            else if (x.Magazine || r < 0.15) { x.Fate = "explode"; x.Roll = ((float)rnd.NextDouble() - 0.5f) * 20f; x.Pitch = 28f + (float)rnd.NextDouble() * 15f; x.Dur = 25f; }
            else if (r < 0.45) { x.Fate = "break"; x.Roll = ((float)rnd.NextDouble() - 0.5f) * 30f; x.Pitch = (rnd.NextDouble() < 0.5 ? 1f : -1f) * (30f + (float)rnd.NextDouble() * 20f); x.Dur = 40f; }
            else { x.Fate = "settle"; x.Roll = ((float)rnd.NextDouble() - 0.5f) * 50f; x.Pitch = ((float)rnd.NextDouble() - 0.5f) * 16f; x.Dur = 60f; }
            // fires along the hull
            try
            {
                var secs = s.sections; int n = 0;
                if (secs != null) { var e = secs.GetEnumerator(); while (e.MoveNext() && n < 6) { var sec = e.Current.Value; if (sec == null || rnd.NextDouble() < 0.4) continue; var p = s.transform.position + s.transform.forward * (float)((rnd.NextDouble() - 0.5) * 100.0) + Vector3.up * 6f; try { s.StartFire(sec, p, true, true); n++; } catch { } } }
            }
            catch { }
            if (x.Fate == "explode") { for (int k = 0; k < 2; k++) FlyTurret(s); }
            if (logged++ < 30) Log($"{s.name}: {x.Fate} (torpedo hits port {x.Port}, starboard {x.Stbd}{(x.Magazine ? ", magazine hit" : "")}) - roll {x.Roll:0} deg, pitch {x.Pitch:0} deg over {x.Dur:0} s");
        }
        static void Boom(Ship s, bool big)
        {
            try
            {
                var guns = Guns(s, false); Part p = null;
                if (guns != null && guns.Count > 0) p = guns[rnd.Next(guns.Count)];
                if (p != null) Effect.FlashFire(p, big ? 50000f : 15000f);
                else Effect.WaterSplash(big ? 3f : 1.5f, s.transform.position + s.transform.forward * (float)((rnd.NextDouble() - 0.5) * 80.0), Quaternion.identity);
            }
            catch (Exception e) { if (errors++ < 5) Log("boom: " + e.Message); }
        }
        static List<Part> Guns(Ship s, bool big)
        {
            var l = new List<Part>(); float max = 0f;
            try { var ps = s.parts; for (int i = 0; ps != null && i < ps.Count; i++) { var p = ps[i]; if (p != null && p.data != null && p.data.isGun) { l.Add(p); if (p.data.caliber > max) max = p.data.caliber; } } } catch { }
            if (big && max > 0f) l.RemoveAll(p => p.data.caliber < max - 1f);
            return l;
        }
        static void FlyTurret(Ship s)
        {
            try
            {
                var guns = Guns(s, true); if (guns.Count == 0) return;
                var p = guns[rnd.Next(guns.Count)]; if (p == null || p.gameObject == null) return;
                Effect.FlashFire(p, 60000f);
                Effect.FlyAway(p.gameObject, p.transform.position, new Vector3(12f, 8f, 12f), 1f, 2f);
            }
            catch (Exception e) { if (errors++ < 5) Log("turret: " + e.Message); }
        }

        // every frame after the game's own motion: the extra roll and pitch of the way she goes
        internal static void LateTick()
        {
            if (On == null || !On.Value || st.Count == 0) return;
            foreach (var kv in st)
            {
                var x = kv.Value; if (x.Fate == null || x.Fate == "none") continue;
                try
                {
                    var s = new Ship(kv.Key); var t = s.transform; if (t == null) continue;
                    float k = Mathf.Clamp01((Time.time - x.Start) / Math.Max(1f, x.Dur)); k = k * k * (3f - 2f * k);   // smooth in and out
                    if (x.Fate == "capsize") k = Mathf.Pow(Mathf.Clamp01((Time.time - x.Start) / Math.Max(1f, x.Dur)), 1.6f); // slow list, then over
                    var extra = Quaternion.Euler(x.Pitch * k, 0f, x.Roll * k);
                    var game = t.rotation * Quaternion.Inverse(x.Last);   // remove what was put on last frame
                    t.rotation = game * extra; x.Last = extra;
                }
                catch { }
            }
        }
    }
}
