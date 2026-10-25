// 1.11.251 (#33, author's decisions 25.10): one physical gun for the gun window, the shells' flight, the penetration and the range.
// The 25.10 battle log showed three different guns for one mount (gun window 201.6 mm / 164 kg / 945 m/s, ballistics 178 mm / 114 kg /
// 945 m/s, penetration 178 mm) and a muzzle velocity that did not fall with a heavier shell (1.8-2.6x the muzzle energy of the
// historical 7" Mk 3 from the same propellant and barrel generation).
//   calibre   = the bore as built (PartData.GetCaliber(ship): the calibre after the designer's diameter change)
//   weight    = the gun window's shell weight (Part.ShellWeight with the gun's data and generation: shell type, shell choice, technologies)
//   reference = the historical shell of the calibre band and generation (HistoricalGuns, navweaps.com), scaled to the bore with the cube
//               of the calibre (geometrically similar shell) - it leaves the muzzle at the historical velocity of the generation
//   velocity  = the game's velocity with all technology bonuses (barrel length, propellant, AP shell) as extra charge energy
//               (author's decision 1), without the velocity effect of the shell-weight choice, times (weight / reference)^-k:
//               at a fixed charge a heavier shell leaves slower. k = 0.30 from navweaps: US 8"/55 Mk 12/15 with the same 85 lb
//               full charge - AP Mk 19 118 kg at 823 m/s, AP Mk 21 152 kg at 762 m/s (ln(762/823) / ln(152/118) = -0.304).
// AP shell quality for face-hardened armour (shell technology, never the year): the ap_penetration effect of the ship's AP shell
// component relative to the 1915 AP shell (ap_5), read from Mods\technologies.csv.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PhysGun
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> MassExp;
        internal static bool Enabled { get { return On != null && On.Value; } }
        internal static MelonPreferences_Entry<bool> HistBase;
        internal struct G { public bool Ok; public int Grade; public float CalMm, CalBaseMm, Kg, KgGame, KgRef, KgData, KgWindow, Mv, MvGame, MvHist, MvData, Bonus, ChoiceVel, Q; public Ship.ShellType Type; }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            HistBase = cat.CreateEntry("PhysicalGunHistoricalBase", true, null, "Muzzle velocity = historical velocity of the generation x the game's technology bonus (gun velocity over the gun data velocity), not the game's own gun data velocity");
            On = cat.CreateEntry("PhysicalGun", true, null, "Battle: one physical gun - the bore as built, the shell weight of the gun window and a muzzle velocity that falls with a heavier shell at the same charge; used by the shells' flight, the range and the physical penetration");
            MassExp = cat.CreateEntry("PhysicalGunMassExponent", 0.30f, null, "Muzzle velocity ~ (shell weight / reference shell)^-k at the same charge; 0.30 from the US 8\"/55 (AP Mk 19 118 kg 823 m/s vs AP Mk 21 152 kg 762 m/s, same 85 lb charge)");
        }

        static readonly Dictionary<long, G> cache = new Dictionary<long, G>();
        internal static void ResetCache() { cache.Clear(); }
        [ThreadStatic] static bool busy;
        static int errors, logged;

        internal static G Get(PartData pd, Ship ship, Ship.ShellType st, bool useCache = true)
        {
            var g = new G();
            if (pd == null || ship == null || busy) return g;
            if (st == Ship.ShellType.Auto) st = Ship.ShellType.Ap;
            long key = (pd.Pointer.ToInt64() * 31) ^ (ship.Pointer.ToInt64() << 2) ^ (long)st;
            if (useCache && cache.TryGetValue(key, out g)) return g;
            g = new G();
            busy = true;
            try
            {
                g.Type = st;
                g.CalBaseMm = pd.caliber;
                float cal = 0f; try { cal = pd.GetCaliber(ship); } catch { }
                g.CalMm = cal > 1f ? cal : pd.caliber;
                int grade = 0; try { grade = ship.TechGunGrade(pd, false); } catch { }
                if (grade <= 0) grade = 3;
                g.Grade = grade;
                GunData gd = null; try { gd = G_GunData(pd); } catch { }
                var nst = new Il2CppSystem.Nullable<Ship.ShellType>(st);
                float kg = 0f, mv = 0f, kgGd = 0f, kgWin = 0f;
                try { if (gd != null) kgGd = Part.ShellWeight(gd, pd, grade, ship, nst); } catch { }
                try { kgWin = Part.ShellWeight(pd, ship, nst); } catch { }
                // 1.11.253: the gun data's shell weight is the weight at the base calibre (Sperber 201.6 mm: 114.1 kg, gun window 164 kg);
                // the shell of the bore as built is the geometrically similar shell - the cube of the calibre ratio. The gun window's
                // own value is taken when it already holds the bore (within 10 % of the cube rule).
                float cube = g.CalBaseMm > 1f ? (float)Math.Pow(g.CalMm / g.CalBaseMm, 3.0) : 1f;
                if (kgGd > 0.05f)
                {
                    float expect = kgGd * cube;
                    kg = (kgWin > 0.05f && Math.Abs(kgWin / expect - 1f) < 0.10f) ? kgWin : expect;
                }
                else kg = kgWin;
                g.KgData = kgGd; g.KgWindow = kgWin;
                try { if (gd != null) mv = Part.ShellVelocity(gd, pd, grade, ship, nst); } catch { }
                if (!(mv > 50f)) { try { mv = Part.ShellVelocity(pd, ship, nst); } catch { } }
                if (!(kg > 0.05f) || !(mv > 50f)) { busy = false; if (useCache) cache[key] = g; return g; }
                g.KgGame = kg; g.MvGame = mv; g.Kg = kg;
                float mvData = 0f; try { if (gd != null) mvData = gd.ShellVelocity(ship, pd, grade); } catch { }
                g.MvData = mvData;
                // reference shell of the generation at this bore
                float kgH, mvH, nomIn; float kgRef = kg;
                if (HistoricalGuns.Shells != null && HistoricalGuns.Shells.Value && HistoricalGuns.Hist(g.CalBaseMm / 25.4f, grade, out kgH, out mvH, out nomIn))
                {
                    float r = (g.CalMm / 25.4f) / nomIn; kgRef = kgH * r * r * r; g.MvHist = mvH;
                    // the reference is the AP shell; the HE shell of the same gun is compared with it too (lighter HE leaves faster)
                }
                g.KgRef = kgRef;
                float sv = ShellChoiceVelocity(ship); g.ChoiceVel = sv;
                float mvNoChoice = mv / Math.Max(0.5f, 1f + sv / 100f);
                float ratio = Math.Max(0.4f, Math.Min(3f, kg / Math.Max(0.05f, kgRef)));
                float k = MassExp != null ? MassExp.Value : 0.30f;
                // 1.11.253 (author's decision 25.10, 10:08): the historical muzzle velocity of the generation times the game's technology
                // bonus. The bonus is the gun's velocity (gun window, without the shell-choice effect) over the gun data's own velocity
                // for this generation; the gun data velocities themselves are not historical (7" Mk 3: 988 m/s against 864 m/s).
                float bonus = 1f;
                if (mvData > 50f) bonus = Math.Max(0.8f, Math.Min(1.3f, mvNoChoice / mvData));
                g.Bonus = bonus;
                float baseV = (g.MvHist > 50f && (HistBase == null || HistBase.Value)) ? g.MvHist * bonus : mvNoChoice;
                g.Mv = baseV * (float)Math.Pow(ratio, -k);
                g.Q = ApQuality(ship);
                g.Ok = g.Mv > 50f && g.Kg > 0.05f && g.CalMm > 1f;
                if (useCache && logged++ < 30 && g.CalMm >= 100f) MelonLogger.Msg("[DIP GG Fixes][PhysGun] " + ship.name + ": " + Describe(g));
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][PhysGun] " + e.Message); }
            busy = false;
            if (!useCache) return g;
            if (cache.Count > 4000) cache.Clear();
            cache[key] = g; return g;
        }

        static GunData G_GunData(PartData pd) { return Il2Cpp.G.GameData.GunData(pd); }

        internal static string Describe(G g)
        {
            return $"physical gun {g.CalMm:0.#} mm (base {g.CalBaseMm:0.#} mm) Mk{g.Grade} {g.Type}: shell {g.Kg:0.#} kg (gun data {g.KgData:0.#} kg at the base calibre, gun window {g.KgWindow:0.#} kg; reference {g.KgRef:0.#} kg at this bore), muzzle {g.Mv:0} m/s (historical {g.MvHist:0} m/s x technology bonus {g.Bonus:0.000} = gun {g.MvGame:0} m/s without the shell choice / gun data {g.MvData:0} m/s; shell-choice velocity effect {g.ChoiceVel:+0.#;-0.#;0}% removed, weight law ^-{(MassExp != null ? MassExp.Value : 0.30f):0.00}), energy {0.5f * g.Kg * g.Mv * g.Mv / 1e6f:0.0} MJ, AP quality x{g.Q:0.00}";
        }

        // ---- technology effects read from Mods\technologies.csv (component -> effect value) ----
        static Dictionary<string, Dictionary<string, float>> compFx;
        static void LoadFx()
        {
            if (compFx != null) return;
            compFx = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(MelonUtils.GameDirectory, "Mods", "technologies.csv");
                if (!File.Exists(path)) return;
                int iEff = -1, iComp = -1;
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Length == 0) continue;
                    var cols = SplitCsv(line);
                    if (cols.Count > 0 && cols[0] == "@name") { iEff = cols.IndexOf("effect"); iComp = cols.IndexOf("component"); continue; }
                    if (iEff < 0 || iComp < 0 || cols.Count <= Math.Max(iEff, iComp)) continue;
                    string comp = cols[iComp].Trim(); if (comp.Length == 0) continue;
                    var fx = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                    foreach (var part in cols[iEff].Split(','))
                    {
                        string p = part.Trim(); int a = p.IndexOf('('), b = p.LastIndexOf(')');
                        if (a <= 0 || b <= a) continue;
                        string name = p.Substring(0, a).Trim(), val = p.Substring(a + 1, b - a - 1);
                        if (val.Contains(";")) continue;
                        float v; if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) fx[name] = v;
                    }
                    compFx[comp] = fx;
                }
                MelonLogger.Msg($"[DIP GG Fixes][PhysGun] technology effects of {compFx.Count} components read; AP quality reference ap_5 ap_penetration {Fx("ap_5", "ap_penetration"):0.#}");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][PhysGun] technologies.csv: " + e.Message); }
        }
        static List<string> SplitCsv(string line)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (char c in line)
            {
                if (c == '"') { q = !q; continue; }
                if (c == ',' && !q) { r.Add(sb.ToString()); sb.Length = 0; continue; }
                sb.Append(c);
            }
            r.Add(sb.ToString()); return r;
        }
        static float Fx(string comp, string effect)
        {
            LoadFx(); Dictionary<string, float> fx; float v;
            return compFx != null && compFx.TryGetValue(comp, out fx) && fx.TryGetValue(effect, out v) ? v : 0f;
        }
        static string CompOfType(Ship ship, string type)
        {
            try { var comps = ship.components; if (comps != null) foreach (var kv in comps) { var cd = kv.Value; if (cd != null && cd.type == type) return cd.name; } } catch { }
            return null;
        }
        // velocity effect of the light / heavy / super-heavy shell choice (replaced by the weight law)
        static float ShellChoiceVelocity(Ship ship) { var c = CompOfType(ship, "shell"); return c != null ? Fx(c, "shell_velocity") : 0f; }
        // AP shell quality against face-hardened armour, relative to the 1915 AP shell
        static float ApQuality(Ship ship)
        {
            var c = CompOfType(ship, "shell_ap"); if (c == null) return 1f;
            float refPen = Fx("ap_5", "ap_penetration"), pen = Fx(c, "ap_penetration");
            return Math.Max(0.3f, Math.Min(1.3f, (100f + pen) / (100f + refPen)));
        }
    }
}
