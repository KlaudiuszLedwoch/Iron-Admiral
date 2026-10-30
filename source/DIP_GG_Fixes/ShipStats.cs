// 1.11.457 (author 30.10 19:22: "I also want statistics per individual ship"): a service record for every ship, kept across games in
// UserData\IronAdmiral\ship_stats.csv - battles, won / drawn / lost, damage dealt, whether she was sunk - and her kills from
// sinkings.csv (the ships she sank, with their tonnage). The designer's ranking panel has a "Ships" list: the best ships first
// (kills, tonnage sunk, damage dealt). Switch: DesignStats.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class ShipStats
    {
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][ShipStats] " + m); }
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "ship_stats.csv"); } }
        internal class Rec { public string Id, Name, Class, Nation, Type; public int Battles, Wins, Draws, Losses, Sunk; public float Damage; }
        static Dictionary<string, Rec> recs;
        static readonly Dictionary<IntPtr, float> dealt = new Dictionary<IntPtr, float>();
        static IntPtr battle;
        static string Clean(string s) { return (s ?? "").Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim(); }

        internal static Dictionary<string, Rec> All()
        {
            if (recs != null) return recs;
            recs = new Dictionary<string, Rec>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                    {
                        var c = l.Split(','); if (c.Length < 11 || c[0] == "id") continue;
                        int I(string s) { int.TryParse(s, out int v); return v; }
                        float.TryParse(c[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dmg);
                        recs[c[0]] = new Rec { Id = c[0], Name = c[1], Class = c[2], Nation = c[3], Type = c[4], Battles = I(c[5]), Wins = I(c[6]), Draws = I(c[7]), Losses = I(c[8]), Sunk = I(c[9]), Damage = dmg };
                    }
            }
            catch (Exception e) { Log("read: " + e.Message); }
            return recs;
        }
        static void Save()
        {
            try
            {
                var sb = new StringBuilder("id,name,class,nation,type,battles,wins,draws,losses,sunk,damage" + Environment.NewLine);
                foreach (var r in recs.Values) sb.Append($"{r.Id},{r.Name},{r.Class},{r.Nation},{r.Type},{r.Battles},{r.Wins},{r.Draws},{r.Losses},{r.Sunk},{r.Damage.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}{Environment.NewLine}");
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { Log("write: " + e.Message); }
        }

        // from SinkLog.Credit: damage a ship deals in the battle
        internal static void Dealt(Ship by, float dmg)
        {
            if (by == null || !(dmg > 0f)) return;
            try
            {
                IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; b = cb != null ? cb.Pointer : IntPtr.Zero; } catch { }
                if (b != battle) { battle = b; dealt.Clear(); }
                dealt.TryGetValue(by.Pointer, out float v); dealt[by.Pointer] = v + dmg;
            }
            catch { }
        }
        // from DesignStats.Record: one ship's battle, its outcome (+1 won, 0 draw, -1 lost) and whether she was sunk
        internal static void Add(Ship s, int outcome, bool sunk)
        {
            try
            {
                string id = s.id.ToString(); if (id.Length < 30) return;
                var all = All();
                if (!all.TryGetValue(id, out var r))
                {
                    string cls = ""; try { var d = s.design; cls = Clean(d != null ? d.vesselName : ""); } catch { }
                    string nat = ""; try { nat = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
                    string type = ""; try { type = s.shipType != null ? s.shipType.name : ""; } catch { }
                    r = new Rec { Id = id, Class = cls, Nation = nat, Type = type }; all[id] = r;
                }
                try { r.Name = Clean(s.vesselName); } catch { }
                if (string.IsNullOrEmpty(r.Name)) try { r.Name = Clean(s.name); } catch { }
                r.Battles++; if (outcome > 0) r.Wins++; else if (outcome < 0) r.Losses++; else r.Draws++;
                if (sunk) r.Sunk = 1;
                if (dealt.TryGetValue(s.Pointer, out float dmg)) r.Damage += dmg;
            }
            catch (Exception e) { Log("add: " + e.Message); }
        }
        internal static void Flush() { if (recs != null) Save(); dealt.Clear(); }

        // kills of a ship (from sinkings.csv, by her id)
        internal static void Kills(string id, out int n, out float tons)
        {
            n = 0; tons = 0f; if (string.IsNullOrEmpty(id)) return;
            foreach (var r in SinkLog.Rows()) if (r.ByShip == id) { n++; tons += r.Tons; }
        }
        internal static List<Rec> Best(int max)
        {
            var l = new List<Rec>(); foreach (var r in All().Values) if (r.Battles > 0) l.Add(r);
            var k = new Dictionary<string, (int n, float t)>(); foreach (var r in l) { Kills(r.Id, out int n, out float t); k[r.Id] = (n, t); }
            l.Sort((a, b) => { int c = k[b.Id].n.CompareTo(k[a.Id].n); if (c != 0) return c; c = k[b.Id].t.CompareTo(k[a.Id].t); if (c != 0) return c; return b.Damage.CompareTo(a.Damage); });
            if (l.Count > max) l.RemoveRange(max, l.Count - max);
            return l;
        }
    }
}
