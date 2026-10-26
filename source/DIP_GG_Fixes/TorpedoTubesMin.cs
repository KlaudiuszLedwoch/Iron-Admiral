// 1.11.309 (#47, part 2): generated destroyers carried too few torpedo tubes (a player comparison 26.10: an AI destroyer with
// 3 tubes while quadruple and quintuple launchers were researched). The generator draws a launcher for each torpedo mount from
// every launcher the ship may use, so a modern destroyer often got twin or triple mounts.
// Minimum by technology and tonnage: the largest launcher the nation may fit (technology - Ship.IsPartAvailable) times two for
// a destroyer of AiDestroyerTubesTonnage tonnes or more (one launcher each side or two on the centre line - the norm from the
// 1900s on), times one below it; never under 2 tubes.
// When a generated destroyer is below that minimum, its launchers are swapped for larger ones on the same mounts (Mount.Fits,
// Part.CanPlace) - mirror pairs of side mounts together, so the ship stays balanced - until the minimum is met or no mount
// takes a larger launcher. Runs in the generator coroutine at state 13 (parts placed, before the game's weight reduction), so
// the extra weight is paid for by the game's usual reductions. Only generated designs (AI and auto-design); the player's own
// designs keep the player's choice. Switch: AiMinTorpedoTubes.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoTubesMin
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Tonnage;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiMinTorpedoTubes", true, null, "Generated destroyers (AI and auto-design) get larger torpedo launchers on the same mounts when they carry fewer tubes than the norm of their technology and size");
            Tonnage = cat.CreateEntry("AiDestroyerTubesTonnage", 700f, null, "Minimum torpedo tubes: a destroyer of at least this tonnage should carry two of the largest launchers it may use, a smaller one at least one");
        }
        static int logged, fails;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ShipGen] " + m); }
        static int Tubes(PartData d) { return d == null ? 0 : Math.Max(1, d.barrels); }
        internal static int CountTubes(Ship ship)
        {
            int n = 0; var parts = ship.parts;
            for (int i = 0; i < parts.Count; i++) { var q = parts[i]; if (q != null && q.data != null && q.data.isTorpedo) n += Tubes(q.data); }
            return n;
        }

        internal static void Apply(Ship ship)
        {
            if (On == null || !On.Value || ship == null) return;
            string st = null; try { st = ship.shipType != null ? ship.shipType.name : null; } catch { }
            if (st != "dd") return;
            // the launchers this ship may use (nation, technology, hull)
            var avail = new List<PartData>(); int best = 0;
            foreach (var kv in G.GameData.parts)
            {
                var pd = kv.Value; if (pd == null || !pd.isTorpedo) continue;
                bool ok = false; try { ok = ship.IsPartAvailable(pd); } catch { }
                if (!ok) continue;
                avail.Add(pd); if (Tubes(pd) > best) best = Tubes(pd);
            }
            if (best <= 0) return;
            float ton = 0f; try { ton = ship.Tonnage(); } catch { }
            int want = Math.Max(2, best * (ton >= (Tonnage != null ? Tonnage.Value : 700f) ? 2 : 1));
            int have = CountTubes(ship);
            if (have >= want || have == 0) return;   // no launcher at all = the hull has no torpedo mounts in use; left to the game
            avail.Sort((a, b) => Tubes(b).CompareTo(Tubes(a)));
            int before = have, swapped = 0;
            var launchers = new List<Part>();
            var parts = ship.parts;
            for (int i = 0; i < parts.Count; i++) { var q = parts[i]; if (q != null && q.data != null && q.data.isTorpedo && q.mount != null) launchers.Add(q); }
            launchers.Sort((a, b) => Tubes(a.data).CompareTo(Tubes(b.data)));   // smallest first
            var done = new HashSet<IntPtr>();
            foreach (var q in launchers)
            {
                if (have >= want) break;
                if (done.Contains(q.Pointer)) continue;
                done.Add(q.Pointer);
                // the mirror launcher of a side mount (same part, opposite side, same length position) is swapped with it
                Part mirror = null;
                try
                {
                    var lp = q.transform.localPosition;
                    if (Math.Abs(lp.x) > 0.5f)
                        foreach (var r in launchers)
                        {
                            if (r.Pointer == q.Pointer || done.Contains(r.Pointer) || r.data != q.data) continue;
                            var rp = r.transform.localPosition;
                            if (Math.Abs(rp.x + lp.x) < 0.3f && Math.Abs(rp.z - lp.z) < 0.3f) { mirror = r; break; }
                        }
                }
                catch { }
                if (mirror != null) done.Add(mirror.Pointer);
                foreach (var pd in avail)
                {
                    if (Tubes(pd) <= Tubes(q.data)) break;
                    var a = Swap(ship, q, pd);
                    if (a == null) continue;
                    Part b = null;
                    if (mirror != null)
                    {
                        b = Swap(ship, mirror, pd);
                        if (b == null) { Undo(ship, a, q); continue; }   // both sides or neither
                    }
                    ship.RemovePart(q, true, false);
                    if (b != null) ship.RemovePart(mirror, true, false);
                    swapped += b != null ? 2 : 1;
                    have = CountTubes(ship);
                    break;
                }
            }
            if (logged++ < 30)
            {
                string who = ""; try { who = ship.player.data.name + ": hull " + ship.hull.data.name; } catch { }
                Log($"{who} - destroyer with {before} torpedo tube(s), norm {want} (largest launcher {best}, {ton:0} t): {(swapped > 0 ? swapped + " launcher(s) enlarged, now " + have + " tube(s)" : "no mount takes a larger launcher")}");
            }
        }

        // the old launcher leaves its mount, the new one takes it; null (and the old one back) when the mount or the room refuses
        static Part Swap(Ship ship, Part old, PartData pd)
        {
            Mount m = old.mount; if (m == null) return null;
            bool fits = false; try { fits = m.Fits(pd, null, null); } catch { }
            if (!fits) return null;
            Part part = null;
            try
            {
                old.Unmount(false);
                part = Part.Create(pd, ship, ship.partsCont, new Il2CppSystem.Nullable<int>(), true);
                if (part == null) { old.Mount(m, false); return null; }
                ship.AddPart(part);
                try { part.LoadModel(ship, true); } catch { }
                try { part.UpdateCollidersSize(ship); } catch { }
                part.Mount(m, false);
                try { part.transform.localRotation = old.transform.localRotation; } catch { }
                string why = null; bool ok = true;
                try { ok = part.CanPlace(out why); } catch { }
                if (!ok && why != "available") { Undo(ship, part, old, m); return null; }
                return part;
            }
            catch (Exception e)
            {
                if (fails++ < 5) Log("torpedo launcher swap: " + e.Message);
                try { if (part != null) Undo(ship, part, old, m); else old.Mount(m, false); } catch { }
                return null;
            }
        }
        static void Undo(Ship ship, Part neu, Part old) { Undo(ship, neu, old, neu != null ? neu.mount : null); }
        static void Undo(Ship ship, Part neu, Part old, Mount m)
        {
            try { if (neu != null) ship.RemovePart(neu, true, false); } catch { }
            try { if (m != null && old != null) old.Mount(m, false); } catch { }
        }
    }
}
