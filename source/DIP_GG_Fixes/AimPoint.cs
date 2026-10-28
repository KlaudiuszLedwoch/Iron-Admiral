// 1.11.279 (#42, author 25.10, 18:47-18:52): deliberate aiming at close range. At a flat trajectory (a few km, angle of fall of a
// few degrees) gunners chose the point of aim: armour-piercing shells at the waterline and the belt (Guadalcanal and Savo night
// actions), high-explosive shells at the upper works to blind and burn (Tsushima). The game picks the part and section a shell
// strikes when the gun fires; this module changes that choice at the moment the hit is resolved (the game's static Hit entry, before
// the penetration is judged, so the new section's own plate decides): with a probability that grows as the range shortens and with
// the crew's training, an AP shell that would have struck elsewhere strikes a belt section at the waterline, an HE shell strikes the
// upper works (towers, funnels). The author's and the AI's ships alike. Orders from the ship go to the dispatch log.
// Settings (F10, Battle): AimPoint, AimPointMaxRange (m, no effect beyond), AimPointChance (at point-blank range with a fully
// trained crew). Author 18:49: the effect grows with crew training; 18:51: the point of aim is told in the dispatches.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class AimPoint
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> MaxRange, Chance;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AimPoint", true, null, "Battle: deliberate aiming at close range - AP shells are laid on the waterline and the belt, HE shells on the upper works; the chance that a hit lands where it was aimed grows as the range shortens and with the crew's training (#42)");
            MaxRange = cat.CreateEntry("AimPointMaxRange", 6000f, null, "Deliberate aiming: no effect beyond this range (m); full effect near zero range");
            Chance = cat.CreateEntry("AimPointChance", 0.6f, null, "Deliberate aiming: chance at point-blank range with a fully trained crew that a hit lands at the point of aim");
        }
        static readonly System.Random rnd = new System.Random();
        static int logged, errors, moved, seen; static readonly Dictionary<long, float> said = new Dictionary<long, float>();
        internal static void BattleStart() { logged = 0; moved = 0; seen = 0; said.Clear(); }
        internal static string Owner = "?";
        internal class Struck { public string PartType; public bool Hull, Gun, Barb; public string Section; public string Zone; public float Underwater; }
        static readonly Dictionary<long, Struck> struckBy = new Dictionary<long, Struck>();
        internal static Struck StruckOf(Shell s) { Struck st; return s != null && struckBy.TryGetValue(s.Pointer.ToInt64(), out st) ? st : null; }
        static void Remember(Projectile proj, Part part, Ship.HitPrecalc hp)
        {
            try
            {
                var shell = proj != null ? proj.TryCast<Shell>() : null; if (shell == null) return;
                if (struckBy.Count > 4000) struckBy.Clear();
                var st = new Struck(); PartData d = null; try { d = part != null ? part.data : null; } catch { }
                if (d != null) { st.PartType = d.type; st.Hull = d.isHull; st.Gun = d.isGun; st.Barb = d.isBarbette; } else st.PartType = part != null ? "part" : "no part";
                Ship.Section sec = null; try { sec = hp != null ? hp.section : null; } catch { }
                if (sec != null) { st.Section = sec.name; st.Zone = sec.armor.ToString(); st.Underwater = sec.underwater; }
                struckBy[shell.Pointer.ToInt64()] = st;
            }
            catch { }
        }
        internal static MethodBase Find()
        {
            IEnumerable<Type> types;
            try { types = typeof(Ship).Assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null); }
            foreach (var t in types)
            {
                try
                {
                    var m = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(x =>
                    {
                        if (x.Name != "Hit") return false; var ps = x.GetParameters(); if (ps.Length != 8) return false;
                        return ps[0].ParameterType == typeof(Ship) && ps[1].ParameterType == typeof(string) && ps[4].ParameterType == typeof(Part) && ps[5].ParameterType == typeof(Ship.HitPrecalc);
                    });
                    if (m != null) { Owner = t.FullName; MelonLogger.Msg("[DIP GG Fixes][Aim] hit entry found in " + t.FullName); return m; }
                }
                catch { }
            }
            return null;
        }

        static float Training(Ship s) { try { float t = s.CrewTrainingAmount; if (t > 1.5f) t /= 100f; return Mathf.Clamp01(t); } catch { return 0.5f; } }

        // the hit is resolved: part and section may be exchanged for the point of aim
        internal static void Resolve(Ship target, ref Part part, Ship.HitPrecalc hp, Projectile proj)
        {
            Remember(proj, part, hp);
            if (On == null || !On.Value || target == null || hp == null || proj == null) return;
            try
            {
                var shell = proj.TryCast<Shell>(); if (shell == null) return;
                var sh = PhysPen.ShotOf(shell); if (sh == null || sh.Shooter == null) return;
                seen++;
                float maxR = MaxRange != null ? MaxRange.Value : 6000f;
                if (sh.Range >= maxR || sh.Range <= 1f) return;
                bool he = false; try { he = shell.shellType == Ship.ShellType.He; } catch { }
                int mode = GunOrders.AimModeFor(sh.Shooter); if (mode == 1) he = false; else if (mode == 2) he = true; // the player's strip (#42)
                float train = Training(sh.Shooter);
                float p = (Chance != null ? Chance.Value : 0.6f) * (1f - sh.Range / maxR) * (0.2f + 0.8f * train);
                Say(sh.Shooter, target, he, sh.Range, p);
                if (rnd.NextDouble() >= p) return;
                var hull = target.hull;
                if (!he)
                {
                    // AP: a belt section at or below the waterline of the citadel (the ship's vitals); the original is kept when it already is one
                    Ship.Section cur = null; try { cur = hp.section; } catch { }
                    if (cur != null && cur.armor == Ship.A.Belt && cur.underwater > 0.01f) return;
                    var secs = target.sections; if (secs == null || hull == null) return;
                    var cands = new List<Ship.Section>();
                    var e = secs.GetEnumerator(); while (e.MoveNext()) { var s = e.Current.Value; if (s == null) continue; if (s.armor == Ship.A.Belt && s.underwater > 0.01f) cands.Add(s); }
                    if (cands.Count == 0) { e = secs.GetEnumerator(); while (e.MoveNext()) { var s = e.Current.Value; if (s != null && s.armor == Ship.A.Belt) cands.Add(s); } }
                    if (cands.Count == 0) return;
                    var pick = cands[rnd.Next(cands.Count)];
                    hp.part = hull; hp.section = pick; part = hull; moved++; Remember(proj, part, hp);
                    if (logged++ < 30) MelonLogger.Msg($"[DIP GG Fixes][Aim] {sh.Shooter.name} -> {target.name}: {sh.G.CalMm:0.#} mm AP at {sh.Range:0} m laid on the waterline (p {p:0.00}, crew {train:0.00}) - strikes belt section {pick.name} ({(cur != null ? cur.name + "/" + cur.armor : "upper works")} before)");
                }
                else
                {
                    // HE: the upper works - towers and funnels (not guns, barbettes or torpedo mounts); the original is kept when it is one
                    PartData cpd = null; try { cpd = part != null ? part.data : null; } catch { }
                    if (cpd != null && !cpd.isHull && !cpd.isGun && !cpd.isBarbette && !cpd.isTorpedo) return;
                    var parts = target.parts; if (parts == null) return;
                    var cands = new List<Part>();
                    for (int i = 0; i < parts.Count; i++) { var q = parts[i]; if (q == null) continue; PartData d = null; try { d = q.data; } catch { } if (d == null || d.isHull || d.isGun || d.isBarbette || d.isTorpedo) continue; cands.Add(q); }
                    if (cands.Count == 0) return;
                    var pick = cands[rnd.Next(cands.Count)];
                    string before = cpd != null ? cpd.type : "?";
                    hp.part = pick; hp.section = null; part = pick; moved++; Remember(proj, part, hp);
                    if (logged++ < 30) MelonLogger.Msg($"[DIP GG Fixes][Aim] {sh.Shooter.name} -> {target.name}: {sh.G.CalMm:0.#} mm HE at {sh.Range:0} m laid on the upper works (p {p:0.00}, crew {train:0.00}) - strikes {pick.data.type} ({before} before)");
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Aim] " + e.Message); }
        }

        // one order per ship per 90 s, in the voice of the gunnery officer
        static void Say(Ship shooter, Ship target, bool he, float range, float p)
        {
            try
            {
                if (p < 0.05f) return;
                long k = shooter.Pointer.ToInt64(); float t; float now = Time.time;
                if (said.TryGetValue(k, out t) && now - t < 90f) return; said[k] = now;
                string tn = Dispatch.Ship(target.name); string sn = Dispatch.Ship(shooter.name);
                // 1.11.283: the line from the language files, in the firing navy's language
                string line = DLang.T(DLang.For(shooter), he ? "aim.he" : "aim.ap", "range", $"{range / 1000f:0.0}", "tn", tn, "sn", sn);
                if (line == null) return;
                Dispatch.SayHit("aim|" + sn + "|" + (he ? "he" : "ap"), line);
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Hit_AimPoint
    {
        static MethodBase TargetMethod() { return AimPoint.Find(); }
        static bool Prepare() { return AimPoint.Find() != null; }
        static void Prefix(Ship __0, ref Part __4, Ship.HitPrecalc __5, Projectile __7) { try { AimPoint.Resolve(__0, ref __4, __5, __7); } catch { } }
    }
}
