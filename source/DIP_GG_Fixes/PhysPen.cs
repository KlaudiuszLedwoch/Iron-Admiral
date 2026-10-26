// 1.11.251 (#33, author's decision 25.10: Okun's formulas): penetration from the physics of each shot.
// The game's penetration is a table (penetration.csv: calibre x range, scaled by its modifiers); it does not know the shell's real
// striking velocity and angle of fall, and the 25.10 log showed 2x the belt and 1.5x the deck that the trajectory allowed.
// Ship.GetPenetration cannot be patched (Nullable parameters crash Harmony under Il2Cpp), so the physics enters through the armour:
// for every hit the plate the shell meets is scaled by (game penetration / physical penetration) - the game then compares the same
// numbers it always does, and the outcome is that of the physical shell. HE keeps the game's HE/AP ratio on top of the physical base.
//
// Physical penetration (normal-equivalent mm, from the striking velocity Vs and the angle of fall of the shell's physical arc):
//   vertical plates (belt, turret sides, barbettes, conning tower) - face-hardened armour, Okun: T = C * W^0.2 * V^1.21
//       (navweaps.com, "Defeating Face-Hardened Armor" and "History of armor penetration formulae"), with the fall angle as obliquity
//       (V = Vs * cos(fall)) and the AP shell quality of the firing ship (PhysGun.Q). C = 0.049 (T mm, W kg, V m/s) fitted to the
//       US 8"/55 AP Mk 21 (152 kg) against Class A armour (navweaps.com, 1942 curves): 254 mm 9.9 km, 203 mm 14.1 km, 152 mm 19.0 km,
//       127 mm 22.3 km, 102 mm 26.2 km - the five points lie within 5 % of C = 0.049.
//   horizontal plates (decks, turret tops) - homogeneous armour, Thompson F-formula with Okun's standard F
//       (navweaps.com, "History of armor penetration formulae", section IV): T/D = 1728.04 (W/D^3) [(V/F) cos(Ob)]^2,
//       F = 6 (T/D - 0.45)(Ob^2 + 2000) + 40000 (T, D in, W lb, V ft/s, Ob in degrees from the plate normal = 90 - fall).
//       Check: 8"/55 AP Mk 21 at 16.8 km (about 397 m/s, 22 deg fall) gives 2.03 in; navweaps lists 2.0 in.
// The horizontal angle to the belt stays with Obliquity.cs (line of sight through the plate).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static partial class PhysPen
    {
        // 1.11.253: the plate is scaled by game penetration / physical penetration. The 1.11.251 limits 0.05-50 cut the scaling for
        // small guns (76 mm: game 3-13 mm against physical 37-90 mm, ratio below 0.05), so on plates of about 60-80 mm the game
        // decided against the physics. The limits only guard against a broken value now.
        const float FMin = 0.001f, FMax = 1000f;
        internal static MelonPreferences_Entry<bool> On, HitLog, Replace;
        internal static MelonPreferences_Entry<float> FhConst, BeltC, InfoSize;
        internal static MelonPreferences_Entry<int> HitLogMax;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PhysicalPenetration", true, null, "Battle: penetration from the physics of each shot - striking velocity and angle of fall of the shell's arc; Okun's face-hardened formula for belts and turret sides, Thompson's F-formula with Okun's standard F for decks");
            FhConst = cat.CreateEntry("PhysicalPenetrationFhConst", 0.049f, null, "Okun face-hardened formula T[mm] = C * W[kg]^0.2 * V[m/s]^1.21; C = 0.049 from the US 8\"/55 AP Mk 21 against Class A armour (navweaps.com)");
            BeltC = cat.CreateEntry("PhysicalPenetrationBeltC", 0.2468f, null, "Belt penetration T[mm] = C * W[kg]^0.719 * Vn[m/s]^1.439 * D[mm]^-1.079 (DeMarre type); C = 0.2468 from NavWeaps side-armour data of 9 AP guns 5-16 inch");
            Replace = cat.CreateEntry("PhysicalPenetrationRows", true, null, "Battle: the game's penetration value itself is replaced by the physical one (belt: DeMarre-type law; deck: Thompson F-formula) for the arriving shell, the AI's estimates and the gun window, and the plate is no longer scaled to it - the plate keeps only its quality (iron ... Krupp) and the obliquity. Off: the older method (plate scaled by game / physical penetration)");
        HitLog = cat.CreateEntry("HitLog", true, null, "Battle: one log line per shell hit - calibre, weight, striking velocity, angle of fall, armour zone, plate, physical and game penetration, result and damage");
            HitLogMax = cat.CreateEntry("HitLogMax", 300, null, "Battle: at most this many hit lines per battle (a summary per calibre and result follows at the end)");
            InfoSize = cat.CreateEntry("GunInfoTextSize", 0.78f, null, "Gun window: text size of the gun description (1 = the game's size)");
            PhysGun.CreatePrefs(cat);
            OverPen.CreatePrefs(cat); // 1.11.253
            HitDispatch.CreatePrefs(cat); // 1.11.258
            GunOrders.CreatePrefs(cat); // 1.11.259 (#37)
        DamageDiag.CreatePrefs(cat); // 1.11.276 (#33 step 3c)
        DamageModel.CreatePrefs(cat); // 1.11.278 (#33 steps 3b/3c)
        AimPoint.CreatePrefs(cat); // 1.11.279 (#42)
        }

        internal class Shot { public Ship Shooter; public PartData Pd; public PhysGun.G G; public float Range, Vs, Fall; public Ship.ShellType Raw; public bool Seen; public string Key; }
        // 1.11.268 (author 25.10, 15:08-15:12: enemy BB fired yellow-tracer HE shells while every logged hit read "Ap"): shells fired by
        // nation, calibre and type - the type at creation and the type the shell carries in flight; a difference is logged
        static readonly SortedDictionary<string, int[]> fired = new SortedDictionary<string, int[]>();
        static int typeDiff, hitLinesPl;
        static readonly HashSet<long> armourLogged = new HashSet<long>();
        static void ArmourOnce(Ship v)
        {
            try
            {
                if (v == null || !armourLogged.Add(v.Pointer.ToInt64()) || armourLogged.Count > 80) return;
                var sb = new System.Text.StringBuilder(); var arm = v.armor;
                foreach (Ship.A z in Enum.GetValues(typeof(Ship.A))) { float mm; if (arm != null && arm.TryGetValue(z, out mm) && mm > 0.05f) sb.Append($" {z} {mm:0}"); }
                bool pl = false; try { pl = v.player != null && v.player.isMain; } catch { }
                float t = 0f; try { t = v.Tonnage(); } catch { }
                MelonLogger.Msg($"[DIP GG Fixes][Hit] armour of {(pl ? "player" : "AI")} ship {v.name} ({t:0} t), mm:{(sb.Length > 0 ? sb.ToString() : " none")}");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] armour: " + e.Message); }
        }
        static string Nation(Ship s) { try { var n = s != null ? s.name : null; if (n == null) return "?"; int a = n.LastIndexOf('['), b = n.LastIndexOf(']'); return a >= 0 && b > a ? n.Substring(a + 1, b - a - 1) : "?"; } catch { return "?"; } }
        static int Ix(Ship.ShellType t) { return t == Ship.ShellType.Ap ? 0 : t == Ship.ShellType.He ? 1 : 2; }
        internal static void Seen(Shell s)
        {
            try
            {
                Shot sh; if (s == null || !shots.TryGetValue(s.Pointer.ToInt64(), out sh) || sh.Seen) return;
                sh.Seen = true; var t = s.shellType; int[] v;
                if (sh.Key != null && fired.TryGetValue(sh.Key, out v)) v[3 + Ix(t)]++;
                if (t != sh.Raw) { typeDiff++; if (typeDiff <= 5) MelonLogger.Msg($"[DIP GG Fixes][Hit] shell type differs: {sh.G.CalMm:0.#} mm from {(sh.Shooter != null ? sh.Shooter.name : "?")} - at creation {sh.Raw}, in flight {t} (physics used {sh.G.Type})"); }
            }
            catch { }
        }
        static readonly Dictionary<long, Shot> shots = new Dictionary<long, Shot>();
        [ThreadStatic] internal static Shell Current;
        static int errors, hitLines, adjusted, skipped, rawLines, shellHits, noShot, uiLogged;
        static readonly SortedDictionary<string, float[]> summary = new SortedDictionary<string, float[]>();
        struct Fac { public float F, Phys, Game, Vs, Fall, R; public int Frame; }
        static readonly Dictionary<long, Fac> facCache = new Dictionary<long, Fac>();

        internal static float GunBase(Shot sh) { try { return sh != null && sh.Pd != null && sh.Shooter != null ? Part.WeaponDamageCrude(sh.Pd, sh.Shooter) * Math.Max(0.01f, Part.WeaponDamageTotalBonus(sh.Pd, sh.Shooter)) : 0f; } catch { return 0f; } }
        internal static Shot ShotOf(Shell s) { Shot sh; return s != null && shots.TryGetValue(s.Pointer.ToInt64(), out sh) ? sh : null; }
        internal static void Register(Shell s, Part from, Ship shooter, PhysGun.G g, float range, float vs, float fall)
        {
            if (s == null || !g.Ok) return;
            if (shots.Count > 6000) shots.Clear();
            var raw = Ship.ShellType.Ap; try { raw = s.shellType; } catch { }
            string key = $"{Nation(shooter)} {g.CalMm,6:0.0} mm"; int[] fv; if (!fired.TryGetValue(key, out fv)) { fv = new int[6]; fired[key] = fv; } fv[Ix(raw)]++;
            shots[s.Pointer.ToInt64()] = new Shot { Shooter = shooter, Pd = from != null ? from.data : null, G = g, Range = range, Vs = vs, Fall = fall, Raw = raw, Key = key };
        }
        internal static void BattleStart() { bool ra = ReplaceActive; DamageDiag.BattleStart(); DamageModel.BattleStart(); AimPoint.BattleStart(); rows.Clear(); shots.Clear(); facCache.Clear(); hitLines = 0; adjusted = 0; skipped = 0; rawLines = 0; shellHits = 0; noShot = 0; summary.Clear(); replaced = 0; noneBy.Clear(); fired.Clear(); typeDiff = 0; hitLinesPl = 0; armourLogged.Clear(); PhysGun.ResetCache(); OverPen.ResetCount(); }
        internal static void BattleEnd() { Report("battle end"); }
        internal static void Report(string tag)
        {
            if (summary.Count == 0 && adjusted == 0) return;
            var sb = new System.Text.StringBuilder($"[DIP GG Fixes][Hit] {tag} summary - armour checks with the physical penetration: at firing / AI estimates {adjusted} (left to the game {skipped}), arriving shells {shellHits} (shell not registered {noShot}), penetration values replaced by physics {replaced}; AP pass-through damage raised {OverPen.Raised}x; hits by calibre and result (count | mean plate the shell met mm | mean physical AP belt penetration mm | mean game penetration mm | mean damage):");
            foreach (var kv in summary) { var v = kv.Value; float n = Math.Max(1f, v[0]); sb.Append($"\n   {kv.Key} | {v[0]:0} | {v[1] / n:0} | {v[2] / n:0} | {v[3] / n:0} | {v[4] / n:0.#}"); }
            MelonLogger.Msg(sb.ToString());
            if (noneBy.Count > 0)
            {
                var nb = new System.Text.StringBuilder($"[DIP GG Fixes][Hit] {tag} \"none\" hits by what the shell struck (part type or hull section zone | hits):");
                foreach (var kv in noneBy) nb.Append($"\n   {kv.Key} | {kv.Value}");
                MelonLogger.Msg(nb.ToString());
            }
            if (fired.Count > 0)
            {
                var fb = new System.Text.StringBuilder($"[DIP GG Fixes][Hit] {tag} shells fired by nation, calibre and type (at creation AP / HE / other | in flight AP / HE / other); type changed after creation {typeDiff}x:");
                foreach (var kv in fired) { var v = kv.Value; fb.Append($"\n   {kv.Key} | {v[0]} / {v[1]} / {v[2]} | {v[3]} / {v[4]} / {v[5]}"); }
                MelonLogger.Msg(fb.ToString());
            }
            OverPen.Report();
        }
        static string Where() { return OverPen.LastFrame == Time.frameCount && OverPen.LastLoc != null ? $"; struck {OverPen.LastLoc}" + (Math.Abs(OverPen.LastNew - OverPen.LastRaw) > 0.05f ? $" (game damage {OverPen.LastRaw:0.#} -> {OverPen.LastNew:0.#})" : "") + OverPen.Extra() : ""; }
        static readonly SortedDictionary<string, int> noneBy = new SortedDictionary<string, int>();
        static string StruckText(Shell s)
        {
            var st = AimPoint.StruckOf(s); if (st == null) return "";
            return $"; part {st.PartType}{(st.Hull ? " (hull)" : st.Gun ? " (gun)" : st.Barb ? " (barbette)" : "")}{(st.Section != null ? $", section {st.Section} {st.Zone}{(st.Underwater > 0.01f ? " underwater" : "")}" : "")}";
        }
        static string Band(float cal) { return cal < 100f ? "<4in" : cal < 160f ? "4-6in" : cal < 230f ? "6-9in" : cal < 320f ? "9-12.5in" : ">=12.5in"; }

        // ---- the formulas ----
        // 1.11.264 (#33, author 25.10, 14:38-14:48, priority): the 1.11.251 belt formula (0.049 W^0.2 V^1.21, fitted to the US 8"/55 alone)
        // overstated 5-6 inch guns 1.4-2.6x and understated 12-16 inch guns (0.74-0.88) against NavWeaps. Now a DeMarre-type law with the
        // calibre, T = C W^0.719 Vn^1.439 D^-1.079 (T mm, W kg, Vn m/s normal to the plate, D mm; T^0.695 D^0.75 ~ W^0.5 Vn), C = 0.2468,
        // fitted to 30 side-armour rows of 9 AP guns 5"-16" (US 5"/51, FR 6"/55, JP 8"/50, GER 8"/60, US 8"/55, GER 28 cm Meppen trials,
        // US 12"/50, BR 15"/42, US 16"/45; navweaps.com) with the striking velocity of the mod's own trajectory (which matches the USN
        // tables of the 12"/50 and 16"/45 within 1-3 %): mean 1.02, spread 19 %; the AP quality Q (shell technology) on top.
        // Propellant and barrel technologies enter through the muzzle velocity (technology bonus) for every calibre alike.
        internal static float BeltPen(float kg, float vs, float fallDeg, float q, float calMm)
        {
            float vn = vs * Mathf.Cos(Mathf.Clamp(fallDeg, 0f, 80f) * Mathf.Deg2Rad);
            if (vn <= 1f || kg <= 0f || calMm <= 1f) return 0f;
            float c = BeltC != null ? BeltC.Value : 0.2468f;
            return (float)(c * q * Math.Pow(kg, 0.719) * Math.Pow(vn, 1.439) * Math.Pow(calMm, -1.079));
        }
        internal static float DeckPen(float kg, float calMm, float vs, float fallDeg)
        {
            if (kg <= 0f || calMm <= 1f || vs <= 1f) return 0f;
            double D = calMm / 25.4, W = kg * 2.20462, V = vs * 3.28084, ob = Math.Min(89.5, Math.Max(0.0, 90.0 - fallDeg)); // a nearly flat shell barely scratches a deck (no artificial floor)
            double c = Math.Cos(ob * Math.PI / 180.0), wd = W / (D * D * D), td = 0.3;
            for (int i = 0; i < 40; i++)
            {
                double F = Math.Max(10000.0, 6.0 * (td - 0.45) * (ob * ob + 2000.0) + 40000.0);
                double x = V * c / F, nt = 1728.04 * wd * x * x;
                if (Math.Abs(nt - td) < 1e-5) { td = nt; break; }
                td = 0.5 * (td + nt);
            }
            return (float)(td * D * 25.4);
        }
        // ---- 1.11.272 (#33 step 3, author 25.10, 15:39-15:55): the penetration itself is physical ----
        // Ship.GetPenetration(gun, shooter, range, belt, shellType, pos) is what the game compares with the plate: at the hit (inside
        // Shell.Update, with the shell's own registered flight), in the AI's estimates and in the gun window. Its result becomes the
        // physical penetration: belt from the DeMarre-type law with the striking velocity and the angle of fall, deck from Thompson's
        // F-formula. HE keeps the game's HE/AP ratio on top of the physical AP value (the game's HE table relative to its AP table).
        [ThreadStatic] static bool inPen; static int replLogged, replaced;
        // the replacement is in force only when its patch really sits on Ship.GetPenetration (a method with Il2Cpp nullable
        // parameters may refuse the patch); otherwise the plate scaling of 1.11.251 stays, so a hit is never judged by the game's bare value
        static int patchState; // 0 unknown, 1 active, -1 not patched
        internal static bool ReplaceActive
        {
            get
            {
                if (Replace == null || !Replace.Value) return false;
                if (patchState == 0)
                {
                    patchState = -1;
                    try
                    {
                        var m = AccessTools.Method(typeof(GameData), "PenetrationData");
                        var info = m != null ? HarmonyLib.Harmony.GetPatchInfo(m) : null;
                        if (info != null && info.Postfixes != null) foreach (var p in info.Postfixes) if (p.PatchMethod != null && p.PatchMethod.DeclaringType == typeof(Patch_PenetrationRow_PhysPen)) patchState = 1;
                    }
                    catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Hit] penetration patch check: " + e.Message); }
                    MelonLogger.Msg(patchState == 1 ? "[DIP GG Fixes][Hit] physical penetration replaces the game's value (penetration table rows per gun and ship); plate scaling off" : "[DIP GG Fixes][Hit] GameData.PenetrationData is NOT patched - the plate scaling of 1.11.251 stays in force");
                }
                return patchState == 1;
            }
        }
        internal static void ReplacePen(PartData gun, Ship shooter, float range, bool belt, Il2CppSystem.Nullable<Ship.ShellType> st, ref float result)
        {
            if (On == null || !On.Value || Replace == null || !Replace.Value || inPen || gun == null || shooter == null) return;
            try
            {
                inPen = true;
                Ship.ShellType type = Ship.ShellType.Ap; try { if (st != null && st.HasValue) type = st.Value; } catch { }
                if (type == Ship.ShellType.Auto) type = Ship.ShellType.Ap;
                var g = PhysGun.Get(gun, shooter, Ship.ShellType.Ap); if (!g.Ok) return;
                float vs, fall; bool fromShell = false;
                Shot sh = null; var cur = Current;
                if (cur != null && shots.TryGetValue(cur.Pointer.ToInt64(), out sh) && sh.Pd != null && sh.Pd.Pointer == gun.Pointer) { vs = sh.Vs; fall = sh.Fall; fromShell = true; }
                else
                {
                    float t; bool beyond;
                    Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, Ballistics.CdOf(shooter), Math.Max(50f, range), MountElevation.Elev(Math.Max(1, g.Grade)), out vs, out fall, out t, out beyond);
                    if (beyond) { result = 0f; return; }
                }
                float phys = belt ? BeltPen(g.Kg, vs, fall, g.Q, g.CalMm) : DeckPen(g.Kg, g.CalMm, vs, fall);
                if (type == Ship.ShellType.He)
                {
                    // the game's own HE relative to its AP at this range (the HE table is the same physics with a lighter, weaker shell)
                    float gAp = 0f; try { gAp = Ship.GetPenetration(gun, shooter, range, belt, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>()); } catch { }
                    phys = gAp > 0.01f ? phys * Mathf.Clamp(result / gAp, 0.02f, 1f) : phys * 0.3f;
                }
                if (replLogged < 12 && Math.Abs(result - phys) > 1f) { replLogged++; MelonLogger.Msg($"[DIP GG Fixes][Hit] penetration replaced: {g.CalMm:0.#} mm {type} of {shooter.name}, {(fromShell ? "arriving shell" : "estimate")} at {range:0} m ({vs:0} m/s, fall {fall:0.0} deg), {(belt ? "belt" : "deck")}: game {result:0} mm -> physical {phys:0} mm"); }
                replaced++; result = phys;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] penetration replace: " + e.Message); }
            finally { inPen = false; }
        }
        internal static int Replaced { get { return replaced; } }

        // ---- 1.11.274 (#33 step 3, second route): the row of the penetration table ----
        // The game reads a gun's penetration from a row of penetration.csv (PenetrationData: vert_0..vert_50 side, horiz_5..horiz_50 deck,
        // by range in km) chosen by GameData.PenetrationData(gun, ship), interpolates it by range and applies its modifiers (shell
        // technologies, generation). The 1.11.272 hook of Ship.GetPenetration crashed the game (Il2Cpp nullable arguments), so the row
        // is replaced instead: for each gun and ship a private row with the physical penetration at every column, divided by the game's
        // own modifier (measured once per gun and ship as game / row at 5 km, with the original row in place), so the game's reading
        // yields the physical value for AP; HE keeps the game's HE/AP ratio because its modifier differs by exactly that ratio.
        static readonly int[] colsV = { 0, 1, 5, 10, 15, 20, 25, 30, 35, 40, 50 };
        static readonly int[] colsH = { 5, 10, 15, 20, 25, 30, 35, 40, 50 };
        class RowX { public PenetrationData Row; public int Frame; public float Mv, Mh; public bool Ok; }
        static readonly Dictionary<long, RowX> rows = new Dictionary<long, RowX>();
        [ThreadStatic] static bool rowBusy; static int rowLogged, rowFails;
        static float ColV(PenetrationData r, int km) { switch (km) { case 0: return r.vert_0; case 1: return r.vert_1; case 5: return r.vert_5; case 10: return r.vert_10; case 15: return r.vert_15; case 20: return r.vert_20; case 25: return r.vert_25; case 30: return r.vert_30; case 35: return r.vert_35; case 40: return r.vert_40; default: return r.vert_50; } }
        static float ColH(PenetrationData r, int km) { switch (km) { case 5: return r.horiz_5; case 10: return r.horiz_10; case 15: return r.horiz_15; case 20: return r.horiz_20; case 25: return r.horiz_25; case 30: return r.horiz_30; case 35: return r.horiz_35; case 40: return r.horiz_40; default: return r.horiz_50; } }
        static void SetV(PenetrationData r, int km, float v) { switch (km) { case 0: r.vert_0 = v; break; case 1: r.vert_1 = v; break; case 5: r.vert_5 = v; break; case 10: r.vert_10 = v; break; case 15: r.vert_15 = v; break; case 20: r.vert_20 = v; break; case 25: r.vert_25 = v; break; case 30: r.vert_30 = v; break; case 35: r.vert_35 = v; break; case 40: r.vert_40 = v; break; default: r.vert_50 = v; break; } }
        static void SetH(PenetrationData r, int km, float v) { switch (km) { case 5: r.horiz_5 = v; break; case 10: r.horiz_10 = v; break; case 15: r.horiz_15 = v; break; case 20: r.horiz_20 = v; break; case 25: r.horiz_25 = v; break; case 30: r.horiz_30 = v; break; case 35: r.horiz_35 = v; break; case 40: r.horiz_40 = v; break; default: r.horiz_50 = v; break; } }
        internal static void ReplaceRow(PartData gun, Ship ship, ref PenetrationData result)
        {
            if (On == null || !On.Value || Replace == null || !Replace.Value || rowBusy || gun == null || ship == null || result == null) return;
            try
            {
                long key = (gun.Pointer.ToInt64() * 31) ^ (ship.Pointer.ToInt64() << 3);
                RowX rx;
                if (rows.TryGetValue(key, out rx) && Time.frameCount - rx.Frame < 3600) { if (rx.Ok) { result = rx.Row; replaced++; } return; }
                rx = new RowX { Frame = Time.frameCount, Ok = false }; rows[key] = rx;
                var g = PhysGun.Get(gun, ship, Ship.ShellType.Ap); if (!g.Ok) return;
                float cd = Ballistics.CdOf(ship), elev = MountElevation.Elev(Math.Max(1, g.Grade));
                // the game's modifier on top of the row: its reading at 5 km over the row's 5 km column, original row in place
                float gV = 0f, gH = 0f;
                rowBusy = true;
                try
                {
                    gV = Ship.GetPenetration(gun, ship, 5000f, true, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>());
                    gH = Ship.GetPenetration(gun, ship, 5000f, false, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>());
                }
                finally { rowBusy = false; }
                float rV = ColV(result, 5), rH = ColH(result, 5);
                if (!(gV > 0.01f) || !(rV > 0.01f)) { if (rowFails++ < 5) MelonLogger.Warning($"[DIP GG Fixes][Hit] penetration row of {g.CalMm:0.#} mm on {ship.name}: game reading {gV:0.#} / row {rV:0.#} at 5 km - cannot measure the modifier, row left to the game"); return; }
                rx.Mv = gV / rV; rx.Mh = (gH > 0.01f && rH > 0.01f) ? gH / rH : rx.Mv;
                // 1.11.275 (author 25.10, 17:18: NullReference in Ship.GetPenetration on the new row): the game reads the row's dictionaries
                // verticals / horizontals (range km -> mm), which a fresh object lacks - the original row is cloned and gets its own dictionaries
                var cloned = result.MemberwiseClone(); var row = cloned != null ? cloned.TryCast<PenetrationData>() : null;
                if (row == null) { if (rowFails++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] penetration row: clone failed - row left to the game"); return; }
                var dv = new Il2CppSystem.Collections.Generic.Dictionary<int, float>(); var dh = new Il2CppSystem.Collections.Generic.Dictionary<int, float>();
                var ov = result.verticals; var oh = result.horizontals;
                var sb = rowLogged < 10 ? new System.Text.StringBuilder() : null;
                foreach (int km in colsV)
                {
                    float vs, fall, t; bool beyond; float R = Math.Max(50f, km * 1000f);
                    Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, cd, R, elev, out vs, out fall, out t, out beyond);
                    float p = beyond ? 0f : BeltPen(g.Kg, vs, fall, g.Q, g.CalMm);
                    SetV(row, km, p / rx.Mv); dv[km] = p / rx.Mv;
                    if (sb != null) sb.Append($" {km}km {p:0}/{ColV(result, km) * rx.Mv:0}");
                }
                foreach (int km in colsH)
                {
                    float vs, fall, t; bool beyond; float R = km * 1000f;
                    Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, cd, R, elev, out vs, out fall, out t, out beyond);
                    float p = beyond ? 0f : DeckPen(g.Kg, g.CalMm, vs, fall);
                    SetH(row, km, p / rx.Mh); dh[km] = p / rx.Mh;
                }
                try { if (ov != null) { var e = ov.GetEnumerator(); while (e.MoveNext()) { int k = e.Current.Key; if (!dv.ContainsKey(k)) dv[k] = 0f; } } } catch { }
                try { if (oh != null) { var e = oh.GetEnumerator(); while (e.MoveNext()) { int k = e.Current.Key; if (!dh.ContainsKey(k)) dh[k] = 0f; } } } catch { }
                row.verticals = dv; row.horizontals = dh;
                rx.Row = row; rx.Ok = true; result = row; replaced++;
                if (sb != null) { rowLogged++; MelonLogger.Msg($"[DIP GG Fixes][Hit] penetration row replaced for {g.CalMm:0.#} mm Mk{g.Grade} on {ship.name} (game modifier x{rx.Mv:0.00} belt, x{rx.Mh:0.00} deck): belt physical/game by range:{sb}"); }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] penetration row: " + e.Message); }
        }
        internal static void RowCheck()
        {
            // one check per battle that the game now reads the physical value (gun window / AI): logged for the first player's gun seen
            try
            {
                if (rows.Count == 0) { patchState = -1; MelonLogger.Msg("[DIP GG Fixes][Hit] penetration rows: none replaced yet - plate scaling in force"); return; }
            }
            catch { }
        }
        static bool Horizontal(string zone) { return zone.IndexOf("Deck", StringComparison.OrdinalIgnoreCase) >= 0 || zone.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0; }

        // ---- the armour of each hit ----
        internal static void Adjust(Ship target, Ship.A zone, PartData gun, Ship shooter, ref float armor)
        {
            if (On == null || !On.Value || armor <= 0f) return;
            if (ReplaceActive) return; // 1.11.272: the penetration is physical itself - no plate scaling (step 3)
            // the arriving shell (Shell.Update): the game asks for the plate without the gun - the shell's own physical flight is used
            if ((gun == null || shooter == null) && Current != null)
            {
                try
                {
                    Shot sh; if (!shots.TryGetValue(Current.Pointer.ToInt64(), out sh) || sh.Pd == null || sh.Shooter == null) { noShot++; return; }
                    bool hz2 = Horizontal(zone.ToString());
                    var gAp = sh.G.Type == Ship.ShellType.Ap ? sh.G : PhysGun.Get(sh.Pd, sh.Shooter, Ship.ShellType.Ap);
                    if (!gAp.Ok) return;
                    float phys2 = hz2 ? DeckPen(gAp.Kg, gAp.CalMm, sh.Vs, sh.Fall) : BeltPen(gAp.Kg, sh.Vs, sh.Fall, gAp.Q, gAp.CalMm);
                    float game2 = 0f;
                    try { game2 = Ship.GetPenetration(sh.Pd, sh.Shooter, sh.Range, !hz2, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>()); } catch { }
                    if (game2 <= 0.01f) return;
                    armor *= Mathf.Clamp(phys2 > 0.01f ? game2 / phys2 : FMax, FMin, FMax); shellHits++;
                }
                catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] adjust (shell): " + e.Message); }
                return;
            }
            if (shooter == null) return;
            // The game decides a hit's outcome when the gun fires (Ship.HitPrecalc, before the shell exists) and calls this with the gun
            // (partData) and the firing ship; the AI's penetration estimates call it the same way. The shot is rebuilt from those
            // arguments: the physical gun, the distance between the ships and the striking velocity and fall at that range.
            if (gun == null || target == null) return;
            try
            {
                string zn = zone.ToString(); bool hz = Horizontal(zn);
                Vector3 dv = target.transform.position - shooter.transform.position; dv.y = 0f; float R = dv.magnitude;
                if (R < 50f) { skipped++; return; }
                long key = (gun.Pointer.ToInt64() * 31) ^ (shooter.Pointer.ToInt64() << 3) ^ ((long)Mathf.RoundToInt(R / 100f) << 1) ^ (hz ? 1L : 0L);
                Fac fc;
                if (!facCache.TryGetValue(key, out fc) || Time.frameCount - fc.Frame > 600)
                {
                    var g = PhysGun.Get(gun, shooter, Ship.ShellType.Ap);
                    if (!g.Ok) { skipped++; return; }
                    float vs, fall, t; bool beyond;
                    Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, Ballistics.CdOf(shooter), R, MountElevation.Elev(Math.Max(1, g.Grade)), out vs, out fall, out t, out beyond);
                    float phys = hz ? DeckPen(g.Kg, g.CalMm, vs, fall) : BeltPen(g.Kg, vs, fall, g.Q, g.CalMm);
                    float game = 0f;
                    try { game = Ship.GetPenetration(gun, shooter, R, !hz, new Il2CppSystem.Nullable<Ship.ShellType>(Ship.ShellType.Ap), new Il2CppSystem.Nullable<Vector3>()); } catch { }
                    fc = new Fac { Phys = phys, Game = game, Vs = vs, Fall = fall, R = R, Frame = Time.frameCount, F = game > 0.01f ? Mathf.Clamp(phys > 0.01f ? game / phys : FMax, FMin, FMax) : 1f };
                    if (facCache.Count > 20000) facCache.Clear();
                    facCache[key] = fc;
                }
                if (fc.Game <= 0.01f) { skipped++; return; }
                armor *= fc.F; adjusted++;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] adjust: " + e.Message); }
        }

        // Ui.RegisterTakenDamage(Ship victim, Part from, string hitType, float damage, float armor, float penetration, ...)
        internal static void Taken(Ship victim, Part from, string hitType, float damage, float armor, float penetration)
        {
            try
            {
                // runs inside the arriving shell's Update: its own physical flight (registered at Shell.Create) is known here
                Shot sh = null; var s = Current; if (s != null) shots.TryGetValue(s.Pointer.ToInt64(), out sh);
                float cal = sh != null ? sh.G.CalMm : (from != null && from.data != null ? from.data.caliber : 0f);
                float physB = 0f, physD = 0f;
                if (sh != null) { physB = BeltPen(sh.G.Kg, sh.Vs, sh.Fall, sh.G.Q, sh.G.CalMm); physD = DeckPen(sh.G.Kg, sh.G.CalMm, sh.Vs, sh.Fall); }
                // 1.11.269 (author 25.10, 15:14): the damage model must be the same for every side - hits split by direction, the type the shell
                // carries in flight, every hit on the player's ships logged (own cap) with the armour of each ship at its first hit
                bool onPlayer = false; try { onPlayer = victim != null && victim.player != null && victim.player.isMain; } catch { }
                string ty = "?"; try { if (s != null) ty = s.shellType.ToString(); else if (sh != null) ty = sh.G.Type.ToString(); } catch { }
                string key = $"{(onPlayer ? "AI->player" : "player->AI")} | {cal,6:0.0} mm {ty,-2} | {hitType}";
                ArmourOnce(victim);
                float[] a; if (!summary.TryGetValue(key, out a)) { a = new float[5]; summary[key] = a; }
                a[0]++; a[1] += Math.Max(0f, armor); a[2] += physB; a[3] += Math.Max(0f, penetration); a[4] += damage;
                HitDispatch.Hit(victim, sh != null ? sh.Shooter : (from != null ? from.ship : null), cal, hitType, damage);
                if (hitType == "none") { var st = AimPoint.StruckOf(s); string k = st == null ? "unknown" : $"{(st.Hull ? "hull" : st.Gun ? "gun" : st.Barb ? "barbette" : st.PartType)}{(st.Section != null ? " / " + st.Zone + (st.Underwater > 0.01f ? " underwater" : "") : "")}"; int c; noneBy.TryGetValue(k, out c); noneBy[k] = c + 1; }
                if (hitType != "normal" && hitType != "over" && hitType != "torpedo" && OverPen.LastFrame == Time.frameCount) OverPen.NonPen($"{ty} {Band(cal)} | {OverPen.LastLoc ?? "?"} | {hitType}", damage, OverPen.LastFire, OverPen.LastFlood, OverPen.LastModules != null);
                if (HitLog == null || !HitLog.Value) return;
                if (onPlayer) { if (hitLinesPl >= (HitLogMax != null ? HitLogMax.Value : 300) * 2) return; hitLinesPl++; }
                else
                {
                    if (hitLines >= (HitLogMax != null ? HitLogMax.Value : 300)) return;
                    if (hitType == "none" && rawLines++ >= 40) return; // the game's "none" (no armour met, no damage): the first 40 (1.11.281: with the struck part)
                    hitLines++;
                }
                if (sh != null)
                    MelonLogger.Msg($"[DIP GG Fixes][Hit]{(onPlayer ? "[on player]" : "")} {sh.G.CalMm:0.#} mm {ty} (physics {sh.G.Type}) {sh.G.Kg:0.#} kg ({sh.G.Mv:0} m/s) from {(sh.Shooter != null ? sh.Shooter.name : "?")} -> {(victim != null ? victim.name : "?")}: {sh.Range:0} m, strike {sh.Vs:0} m/s, fall {sh.Fall:0.0} deg; physical AP penetration belt {physB:0.#} mm / deck {physD:0.#} mm; game: plate met {armor:0.#} mm (after quality, obliquity and the physical scaling), penetration {penetration:0.#} mm; result {hitType}, damage {damage:0.#} (gun base {GunBase(sh):0.#}){StruckText(s)}{Where()}");
                else
                    MelonLogger.Msg($"[DIP GG Fixes][Hit]{(onPlayer ? "[on player]" : "")} {cal:0.#} mm {ty} (no physical shell) -> {(victim != null ? victim.name : "?")}: plate met {armor:0.#} mm, penetration {penetration:0.#} mm; result {hitType}, damage {damage:0.#}{Where()}");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] damage path: " + e.Message); }
        }

        // ---- gun window: the values the battle uses ----
        static string StripTags(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length); bool tag = false;
            foreach (char c in s) { if (c == '<') { tag = true; continue; } if (c == '>' && tag) { tag = false; continue; } if (!tag) sb.Append(c); }
            return sb.ToString();
        }
        // numbers directly followed by "mm"
        static List<float> MmNumbers(string s)
        {
            var r = new List<float>();
            for (int i = 0; i + 1 < s.Length; i++)
            {
                if (s[i] != 'm' || s[i + 1] != 'm') continue;
                int j = i - 1; while (j >= 0 && s[j] == ' ') j--;
                int e = j; while (j >= 0 && (char.IsDigit(s[j]) || s[j] == '.')) j--;
                if (e > j) { float v; if (float.TryParse(s.Substring(j + 1, e - j), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) r.Add(v); }
                i++;
            }
            return r;
        }
        // leading range "15,000m - ..." -> 15000
        static bool RowRange(string plain, out float R)
        {
            R = 0f; if (plain.Length == 0 || !char.IsDigit(plain[0])) return false;
            int i = 0; var sb = new System.Text.StringBuilder();
            while (i < plain.Length && (char.IsDigit(plain[i]) || plain[i] == ',' || plain[i] == '.')) { if (plain[i] != ',') sb.Append(plain[i]); i++; }
            while (i < plain.Length && plain[i] == ' ') i++;
            if (i >= plain.Length || plain[i] != 'm' || (i + 1 < plain.Length && plain[i + 1] == 'm')) return false;
            return float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out R) && R > 0f;
        }
        static string VelSrc(PhysGun.G g)
        {
            return g.MvHist > 50f && (PhysGun.HistBase == null || PhysGun.HistBase.Value)
                ? $"hist. {g.MvHist:0} x {g.Bonus:0.00}; game {g.MvGame:0}"
                : $"game {g.MvGame:0}";
        }
        // 1.11.257 (author's 25.10 battle 13:42: 381 mm window "Shell Weight AP: 1444 kg (+73%)" while the shells flew with 1004 kg):
        // the number shown in the game's line is compared, not Part.ShellWeight (the line carries modifiers the call does not)
        static float LineKg(string plain)
        {
            int a = plain.IndexOf(':'); if (a < 0) return 0f;
            var sb = new System.Text.StringBuilder(); int i = a + 1;
            while (i < plain.Length && plain[i] == ' ') i++;
            while (i < plain.Length && (char.IsDigit(plain[i]) || plain[i] == ',' || plain[i] == '.')) { if (plain[i] != ',') sb.Append(plain[i]); i++; }
            float v; return float.TryParse(sb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
        static bool WeightDiffers(string plain, PhysGun.G g) { float shown = LineKg(plain); return shown > 0.05f && g.Kg > 0.05f && Math.Abs(g.Kg / shown - 1f) > 0.01f; }

        internal static string RewriteInfo(string text, PartData pd, Ship ship)
        {
            if (On == null || !On.Value || string.IsNullOrEmpty(text) || pd == null || ship == null) return text;
            // 1.11.284 (#45, player report 26.10 on the French game): the gun window was recognised by its English labels ("Penetration",
            // "Muzzle Velocity AP", "Penetration Belt/Deck"), so in the other nine languages of the game the window kept the game's table.
            // The window is now read by its structure, which is the same in every language: the velocity lines carry "m/s", the weight
            // lines "kg", and the penetration table is a run of rows "<range>m - <HE belt>mm - <AP belt>mm / <HE deck>mm - <AP deck>mm, ...";
            // the line just above the first row is the table header. The player's own labels are kept.
            try
            {
                var ap = PhysGun.Get(pd, ship, Ship.ShellType.Ap, false); if (!ap.Ok) return text;
                var he = PhysGun.Get(pd, ship, Ship.ShellType.He, false);
                int grade = Math.Max(1, ap.Grade); float cd = Ballistics.CdOf(ship), elev = MountElevation.Elev(grade);
                var lines = text.Split('\n'); bool changed = false;
                int firstRow = -1;
                for (int i = 0; i < lines.Length && firstRow < 0; i++) { float R0; string p0 = StripTags(lines[i]).Trim(); if (RowRange(p0, out R0) && MmNumbers(p0).Count >= 4) firstRow = i; }
                if (firstRow < 0 && text.IndexOf("m/s", StringComparison.Ordinal) < 0 && text.IndexOf("м/с", StringComparison.Ordinal) < 0) return text;
                int velN = 0, kgN = 0; bool inTable = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    string plain = StripTags(lines[i]).Trim();
                    // 1.11.254: the velocity line names its source (historical velocity x technology bonus, weight law); the shell weight line
                    // shows the shell of the bore as built when the game's line does not (142.2 mm on a 5" mount: game 34.6 kg, bore 48.6 kg)
                    if (firstRow < 0 || i < firstRow - 1)
                    {
                        int colon = plain.IndexOf(':'); string label = colon > 0 ? plain.Substring(0, colon) : "";
                        bool vel = colon > 0 && (plain.IndexOf("m/s", colon, StringComparison.Ordinal) > 0 || plain.IndexOf("м/с", colon, StringComparison.Ordinal) > 0);
                        bool kgl = !vel && colon > 0 && (plain.IndexOf(" kg", colon, StringComparison.Ordinal) > 0 || plain.IndexOf(" кг", colon, StringComparison.Ordinal) > 0);
                        if (vel && velN < 2) { var gg = velN == 0 ? ap : he; velN++; if (gg.Ok) { lines[i] = $"{label}: {gg.Mv:0} m/s ({VelSrc(gg)})"; changed = true; } continue; }
                        if (kgl && velN >= 1 && kgN < 2) { var gg = kgN == 0 ? ap : he; kgN++; if (gg.Ok && WeightDiffers(plain, gg)) { lines[i] = $"{label}: {gg.Kg:0} kg ({(gg.KgHist ? "hist." : "bore")}; game {LineKg(plain):0})"; changed = true; } continue; }
                        continue;
                    }
                    if (i == firstRow - 1) { lines[i] = lines[i] + " (physics: Okun)"; inTable = true; continue; }
                    if (i == firstRow) inTable = true;
                    if (!inTable) continue;
                    float R;
                    if (!RowRange(plain, out R)) { if (plain.Length > 0 && !char.IsDigit(plain[0])) inTable = false; continue; }
                    var nums = MmNumbers(plain); if (nums.Count < 4) continue;
                    float heB = nums[0], apB = nums[1], heD = nums[2], apD = nums[3];
                    float vs, fall, t; bool beyond;
                    Ballistics.Impact(ap.Mv, ap.CalMm / 25.4f, ap.Kg, cd, R, elev, out vs, out fall, out t, out beyond);
                    if (beyond) { lines[i] = $"{R:#,0}m - beyond the reach of the mount ({elev:0.#} deg)"; changed = true; continue; }
                    float pB = BeltPen(ap.Kg, vs, fall, ap.Q, ap.CalMm), pD = DeckPen(ap.Kg, ap.CalMm, vs, fall);
                    float hB = apB > 0.01f ? pB * heB / apB : 0f, hD = apD > 0.01f ? pD * heD / apD : 0f;
                    int comma = plain.LastIndexOf("mm,"); string acc = comma >= 0 ? plain.Substring(comma + 3).Trim() : "";
                    lines[i] = $"{R:#,0}m - {hB:0}mm - {pB:0}mm / {hD:0}mm - {pD:0}mm, {acc} [{vs:0}m/s {fall:0}°]"; // 1.11.265: shorter (author 25.10, 14:57)
                    changed = true;
                }
                string res = changed ? string.Join("\n", lines) : text;
                float sz = InfoSize != null ? InfoSize.Value : 1f; // 1.11.266: smaller text in the gun window (author 25.10, 15:03)
                if (sz > 0.3f && sz < 0.99f) res = $"<size={sz * 100f:0}%>" + res + "</size>";
                if (changed && uiLogged++ < 2) MelonLogger.Msg("[DIP GG Fixes][Hit] gun window with the physical values (" + (ship != null ? ship.name : "?") + "):\n" + StripTags(res));
                return res;
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Hit] gun window: " + e.Message); return text; }
        }
    }

    [HarmonyPatch(typeof(Shell), "Update")]
    internal static class Patch_ShellUpdate_PhysPen
    {
        static void Prefix(Shell __instance) { PhysPen.Current = __instance; PhysPen.Seen(__instance); }
        static void Postfix() { PhysPen.Current = null; }
    }

    // 1.11.273: the Harmony patch of Ship.GetPenetration / GetPenetrationUI crashed the game at the first AI estimate of a battle
    // (author 25.10, 16:25 and 16:26, twice at battle start on 1.11.272) - the methods take Il2Cpp nullable arguments. The patch classes
    // are no longer marked for Harmony; the plate scaling of 1.11.251 is back in force until the penetration can be replaced another way.
    internal static class Patch_GetPenetration_PhysPen
    {
        static void Postfix(PartData __0, Ship __1, float __2, bool __3, Il2CppSystem.Nullable<Ship.ShellType> __4, ref float __result) { PhysPen.ReplacePen(__0, __1, __2, __3, __4, ref __result); }
    }
    internal static class Patch_GetPenetrationUI_PhysPen
    {
        static void Postfix(PartData __0, Ship __1, float __2, bool __3, Il2CppSystem.Nullable<Ship.ShellType> __4, ref float __result) { PhysPen.ReplacePen(__0, __1, __2, __3, __4, ref __result); }
    }

    [HarmonyPatch(typeof(GameData), "PenetrationData")]
    internal static class Patch_PenetrationRow_PhysPen
    {
        static void Postfix(PartData __0, Ship __1, ref PenetrationData __result) { PhysPen.ReplaceRow(__0, __1, ref __result); }
    }

    [HarmonyPatch(typeof(Ui), "RegisterTakenDamage")]
    internal static class Patch_TakenDamage_PhysPen
    {
        static void Postfix(Ship __0, Part __1, string __2, float __3, float __4, float __5) { PhysPen.Taken(__0, __1, __2, __3, __4, __5); }
    }

    [HarmonyPatch(typeof(Ui), "GetPartInfoText")]
    internal static class Patch_PartInfo_PhysPen
    {
        static void Postfix(PartData __0, Ship __1, ref string __result) { __result = PhysPen.RewriteInfo(__result, __0, __1); }
    }
}
