// 1.11.459 (author 30.10 19:38: a penetration table for the wiki as a reference when designing - "make the table and update it after
// every test"; asked by A Simple Tako): ten seconds into every battle, every gun on every ship is written once to
// UserData\IronAdmiral\penetration_reference.csv with the physical penetration the battle uses (the same muzzle velocity, AP shell
// weight, shell quality, drag and elevation as the hit): side armour and deck at 1, 5, 10, 15, 20, 25 and 30 km. One row per gun
// mark, calibre, year and shell weight; the file grows with every battle and the wiki table is made from it. Switch:
// PenetrationReference.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class PenReference
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("PenetrationReference", true, null, "Every battle writes the physical penetration of each gun in it (side and deck, 1-30 km) to UserData\\IronAdmiral\\penetration_reference.csv - the source of the penetration table on the wiki");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][PenRef] " + m); }
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "penetration_reference.csv"); } }
        const string Header = "cal_mm,cal_in,mark,year,nation,gun,shell_kg,mv_ms,quality,belt_1,belt_5,belt_10,belt_15,belt_20,belt_25,belt_30,deck_5,deck_10,deck_15,deck_20,deck_25,deck_30,drag";
        static readonly int[] Km = { 1, 5, 10, 15, 20, 25, 30 };
        static HashSet<string> keys; static IntPtr battle; static float since = -1f; static bool done; static int errors;
        // 1.11.460: hits of the battle, merged into penetration_hits.csv at its end
        static string HitsPath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "penetration_hits.csv"); } }
        const string HitsHeader = "cal_mm,mark,decade,range_km,shell_kg,quality,hits,pen_sum,plate_sum,pierced,fall_sum";
        static readonly Dictionary<string, double[]> pending = new Dictionary<string, double[]>();
        internal static void Hit(Ship shooter, PhysGun.G g, float range, float fall, float pen, float plate, string result)
        {
            if (On == null || !On.Value || g.CalMm <= 0f || plate <= 0.5f) return;   // a hit on no armour says nothing of penetration
            int year = 0; try { year = (int)shooter.GetYear(shooter); } catch { }
            int band = Math.Min(30, (int)(range / 5000f) * 5);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string key = $"{g.CalMm:0}|{g.Grade}|{year / 10 * 10}|{band}|{Math.Round(g.Kg).ToString("0", ci)}|{g.Q.ToString("0.##", ci)}";
            if (!pending.TryGetValue(key, out var a)) { a = new double[5]; pending[key] = a; }
            a[0]++; a[1] += Math.Max(0f, pen); a[2] += plate; if (result == "normal" || result == "over") a[3]++; a[4] += fall;
        }
        static void FlushHits()
        {
            if (pending.Count == 0) return;
            try
            {
                var all = new Dictionary<string, double[]>(); var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (File.Exists(HitsPath))
                    foreach (var l in File.ReadAllLines(HitsPath))
                    {
                        var c = l.Split(','); if (c.Length < 11 || c[0] == "cal_mm") continue;
                        var v = new double[5]; for (int i = 0; i < 5; i++) double.TryParse(c[6 + i], System.Globalization.NumberStyles.Float, ci, out v[i]);
                        all[c[0] + "|" + c[1] + "|" + c[2] + "|" + c[3] + "|" + c[4] + "|" + c[5]] = v;
                    }
                int n = 0; foreach (var kv in pending) { if (!all.TryGetValue(kv.Key, out var v)) { v = new double[5]; all[kv.Key] = v; } for (int i = 0; i < 5; i++) v[i] += kv.Value[i]; n += (int)kv.Value[0]; }
                var sb = new StringBuilder(HitsHeader + Environment.NewLine);
                foreach (var kv in all) sb.Append(kv.Key.Replace('|', ',')).Append(',').Append(string.Join(",", Array.ConvertAll(kv.Value, x => x.ToString("0.#", ci)))).Append(Environment.NewLine);
                Directory.CreateDirectory(Path.GetDirectoryName(HitsPath)); File.WriteAllText(HitsPath, sb.ToString());
                Log($"{n} hit(s) on armour added to the measured penetration ({all.Count} calibre / range groups)");
            }
            catch (Exception e) { if (errors++ < 5) Log("hits: " + e.Message); }
            pending.Clear();
        }

        // one row of the reference: the gun 'pd' with the technologies of ship 's'
        static bool Row(PartData pd, Ship s, int year, string nat, StringBuilder sb)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            PhysGun.G g; try { g = PhysGun.Get(pd, s, Ship.ShellType.Ap); } catch { return false; }
            if (!g.Ok || g.Kg <= 0f || g.Mv <= 0f) return false;
            string key = $"{g.CalMm:0}|{g.Grade}|{year}|{g.Kg.ToString("0.#", ci)}|{g.Q.ToString("0.###", ci)}";
            if (!keys.Add(key)) return false;
            float cd = Ballistics.CdOf(s), elev = MountElevation.Elev(Math.Max(1, g.Grade));
            var belt = new List<string>(); var deck = new List<string>();
            foreach (int km in Km)
            {
                float vs, fall, tt; bool beyond;
                Ballistics.Impact(g.Mv, g.CalMm / 25.4f, g.Kg, cd, km * 1000f, elev, out vs, out fall, out tt, out beyond);
                belt.Add(beyond ? "" : PhysPen.BeltPen(g.Kg, vs, fall, g.Q, g.CalMm).ToString("0", ci));
                if (km >= 5) deck.Add(beyond ? "" : PhysPen.DeckPen(g.Kg, g.CalMm, vs, fall).ToString("0", ci));
            }
            sb.Append($"{g.CalMm:0},{(g.CalMm / 25.4f).ToString("0.#", ci)},{g.Grade},{year},{nat},{pd.name},{g.Kg.ToString("0.#", ci)},{g.Mv.ToString("0", ci)},{g.Q.ToString("0.###", ci)},{string.Join(",", belt)},{string.Join(",", deck)},{cd.ToString("0.###", ci)}{Environment.NewLine}");
            return true;
        }
        static void Append(StringBuilder sb, int n, string where)
        {
            if (n == 0) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
            File.AppendAllText(FilePath, sb.ToString());
            Log($"{n} gun(s) added to the penetration reference from {where} ({keys.Count} in all)");
        }
        // 1.11.472 (author 30.10 21:03: "make that table on the wiki while you are at it"): the table should not wait for every gun to
        // fight - in the designer every gun calibre is written once per nation and year with the technologies of the design open
        // there (the same physics the gun window shows), so the reference covers all calibres of the years the player has played
        static readonly HashSet<string> sweptFor = new HashSet<string>(); static float nextSweep;
        static void Sweep()
        {
            if (Time.realtimeSinceStartup < nextSweep) return; nextSweep = Time.realtimeSinceStartup + 5f;
            bool con = false; try { con = GameManager.IsConstructor || GameManager.IsSharedDesignConstructor; } catch { }
            if (!con) return;
            Ship s = null; try { s = G.ui != null ? G.ui.mainShip : null; } catch { }
            if (s == null) return;
            int year = 0; try { year = (int)s.GetYear(s); } catch { }
            string nat = ""; try { nat = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
            if (year <= 0 || !sweptFor.Add(nat + "|" + year)) return;
            LoadKeys();
            var sb = new StringBuilder(); int n = 0;
            foreach (var kv in G.GameData.parts)
            {
                var pd = kv.Value; if (pd == null) continue;
                bool gun = false; try { gun = pd.isGun; } catch { } if (!gun) continue;
                try { if (Row(pd, s, year, nat, sb)) n++; } catch { }
            }
            Append(sb, n, $"the designer ({nat}, {year})");
        }

        static void LoadKeys()
        {
            if (keys != null) return; keys = new HashSet<string>();
            try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath)) { var c = l.Split(','); if (c.Length > 7 && c[0] != "cal_mm") keys.Add(c[0] + "|" + c[2] + "|" + c[3] + "|" + c[6] + "|" + c[8]); } } catch { }
        }
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool b = false; try { b = GameManager.IsBattle; } catch { }
                if (!b) { FlushHits(); battle = IntPtr.Zero; since = -1f; done = false; try { Sweep(); } catch (Exception e) { if (errors++ < 5) Log("designer: " + e.Message); } return; }
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null || cb.Ships == null) return;
                if (cb.Pointer != battle) { FlushHits(); battle = cb.Pointer; since = Time.realtimeSinceStartup; done = false; }
                if (done || Time.realtimeSinceStartup - since < 10f) return;
                done = true; LoadKeys();
                var sb = new StringBuilder(); int n = 0; var ci = System.Globalization.CultureInfo.InvariantCulture;
                for (int i = 0; i < cb.Ships.Count; i++)
                {
                    var s = cb.Ships[i]; if (s == null) continue;
                    int year = 0; try { year = (int)s.GetYear(s); } catch { }
                    string nat = ""; try { nat = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
                    var parts = s.parts; var seen = new HashSet<IntPtr>();
                    for (int k = 0; parts != null && k < parts.Count; k++)
                    {
                        var pd = parts[k] != null ? parts[k].data : null; if (pd == null || !pd.isGun || !seen.Add(pd.Pointer)) continue;
                        if (Row(pd, s, year, nat, sb)) n++;
                    }
                }
                Append(sb, n, "the battle");
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
    }
}
