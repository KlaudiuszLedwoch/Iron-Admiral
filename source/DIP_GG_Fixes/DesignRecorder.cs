// 1.11.310 (#64, author 27.10, 6:29: "log how I design ships and do refits, and build the algorithm from it"): every step of
// designing or refitting a ship in the constructor is written to UserData\IronAdmiral\design_log.csv - hull, speed, tonnage,
// range, beam, draught, bulkheads (survivability), crew quarters, every component, the armour of every zone and the parts
// (towers, funnels, guns, barbettes, torpedo tubes) with their place fore / midships / aft. The designed ship is read twice a
// second; a change is written when the design has stayed the same for one second (a slider drag gives one step with its final
// value). Each step carries the weight, the tonnage and the tonnage reserve after it. Local file only, nothing is sent anywhere.
// Switch: DesignRecorder (F10, Interface).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DesignRecorder
    {
        internal static MelonPreferences_Entry<bool> On;
        static IntPtr shipPtr = IntPtr.Zero; static Dictionary<string, string> committed, pending;
        static float nextRead, pendingSince; static int step, errors, session; static bool inDesigner; static string designKey = "";
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][DesignRec] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DesignRecorder", true, null, "Constructor: every step of designing or refitting a ship (hull, sliders, components, armour, parts, tonnage reserve) is written to UserData\\IronAdmiral\\design_log.csv - local file only");
        }

        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "design_log.csv"); } }
        static string Q(string s) { s = s ?? ""; return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool d = false; try { d = GameManager.IsConstructor || GameManager.IsSharedDesignConstructor; } catch { }
                if (!d) { if (inDesigner) { Flush(true); Write("left the constructor", "", "", null); } inDesigner = false; shipPtr = IntPtr.Zero; committed = null; pending = null; return; }
                if (!inDesigner) { inDesigner = true; session++; }
                if (Time.realtimeSinceStartup < nextRead) return;
                nextRead = Time.realtimeSinceStartup + 0.5f;
                bool loading = false; try { loading = GameManager.IsLoadingAny; } catch { }
                if (loading) return;
                Ship s = null; try { s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
                if (s == null) return;
                var snap = Snap(s);
                if (s.Pointer != shipPtr)
                {
                    Flush(true);
                    shipPtr = s.Pointer; committed = snap; pending = null; step = 0;
                    bool refit = false; try { refit = s.isRefitDesign; } catch { }
                    designKey = Get(snap, "design");
                    Write(refit ? "refit opened" : "design opened", "", Describe(snap), s);
                    return;
                }
                if (Same(snap, committed)) { pending = null; return; }
                if (pending == null || !Same(snap, pending)) { pending = snap; pendingSince = Time.realtimeSinceStartup; return; }
                if (Time.realtimeSinceStartup - pendingSince >= 1f) Commit(s);
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }

        static void Flush(bool force)
        {
            if (pending != null && committed != null && !Same(pending, committed))
            {
                Ship s = null; try { s = PlayerController.Instance != null ? PlayerController.Instance.Ship : null; } catch { }
                Commit(s != null && s.Pointer == shipPtr ? s : null);
            }
        }

        static void Commit(Ship s)
        {
            if (pending == null || committed == null) return;
            var keys = new SortedSet<string>(committed.Keys); foreach (var k in pending.Keys) keys.Add(k);
            step++;
            foreach (var k in keys)
            {
                string a = Get(committed, k), b = Get(pending, k);
                if (a == b || k == "weight" || k == "reserve") continue;
                Write("change", k, a + " -> " + b, s, pending);
            }
            committed = pending; pending = null;
        }

        static string Get(Dictionary<string, string> d, string k) { string v; return d != null && d.TryGetValue(k, out v) ? v : ""; }
        static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var kv in a) { if (kv.Key == "weight" || kv.Key == "reserve") continue; string v; if (!b.TryGetValue(kv.Key, out v) || v != kv.Value) return false; }
            return true;
        }
        static string Describe(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d) { if (sb.Length > 0) sb.Append("; "); sb.Append(kv.Key).Append('=').Append(kv.Value); }
            return sb.ToString();
        }

        static Dictionary<string, string> Snap(Ship s)
        {
            var d = new Dictionary<string, string>();
            try { d["design"] = s.name ?? ""; } catch { }
            try { d["type"] = s.shipType != null ? s.shipType.name : ""; } catch { }
            try { d["hull"] = s.hull != null && s.hull.data != null ? s.hull.data.name : ""; } catch { }
            try { d["speed kn"] = (s.speedMax * 1.94384f).ToString("0.0"); } catch { }
            try { d["tonnage t"] = s.Tonnage().ToString("0"); } catch { }
            try { d["range"] = s.opRange.ToString(); } catch { }
            try { d["beam"] = s.beam.ToString("0.0"); } catch { }
            try { d["draught"] = s.draught.ToString("0.0"); } catch { }
            try { d["bulkheads"] = s.survivability.ToString(); } catch { }
            try { d["crew quarters"] = s.CurrentCrewQuarters.ToString(); } catch { }
            try { float w = s.Weight(true, true), t = s.Tonnage(); d["weight"] = w.ToString("0"); d["reserve"] = (t - w).ToString("0"); } catch { }
            try
            {
                var comps = s.components;
                if (comps != null) foreach (var kv in comps) { try { var cd = kv.Value; if (cd != null) d["component " + cd.type] = cd.name; } catch { } }
            }
            catch { }
            try
            {
                var arm = s.armor;
                if (arm != null) foreach (var kv in arm) { try { if (kv.Value > 0.05f) d["armour " + kv.Key.ToString()] = kv.Value.ToString("0.0"); } catch { } }
            }
            catch { }
            try
            {
                var counts = new SortedDictionary<string, int>();
                var parts = s.parts;
                if (parts != null)
                    for (int i = 0; i < parts.Count; i++)
                    {
                        var p = parts[i]; if (p == null || p.data == null) continue;
                        bool hull = false; try { hull = p.data.isHull; } catch { }
                        if (hull) continue;
                        string where = "";
                        try { float z = s.transform.InverseTransformPoint(p.transform.position).z; where = z > 15f ? "fore" : z < -15f ? "aft" : "mid"; } catch { }
                        string k = "part " + p.data.name + (where.Length > 0 ? " @" + where : "");
                        int c; counts.TryGetValue(k, out c); counts[k] = c + 1;
                    }
                foreach (var kv in counts) d[kv.Key] = kv.Value.ToString();
            }
            catch { }
            return d;
        }

        static void Write(string what, string key, string change, Ship s, Dictionary<string, string> snap = null)
        {
            try
            {
                var p = FilePath; Directory.CreateDirectory(Path.GetDirectoryName(p));
                bool header = !File.Exists(p);
                string nation = "", year = "", tons = "", weight = "", reserve = "";
                try { if (s != null && s.player != null && s.player.data != null) nation = s.player.data.name; } catch { }
                try { var cc = CampaignController.Instance; if (cc != null && GameManager.IsCampaign) year = cc.CurrentDate.AsDate().Year.ToString(); } catch { }
                if (snap != null) { tons = Get(snap, "tonnage t"); weight = Get(snap, "weight"); reserve = Get(snap, "reserve"); }
                else if (s != null) { try { float w = s.Weight(true, true), t = s.Tonnage(); tons = t.ToString("0"); weight = w.ToString("0"); reserve = (t - w).ToString("0"); } catch { } }
                string type = ""; try { if (s != null && s.shipType != null) type = s.shipType.name; } catch { }
                var line = string.Join(",", new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), session.ToString(), Q(designKey), type, nation, year, step.ToString(), what, Q(key), Q(change), tons, weight, reserve });
                using (var w2 = new StreamWriter(p, true, new UTF8Encoding(false)))
                {
                    if (header) w2.WriteLine("time,session,design,type,nation,year,step,event,item,change,tonnage_t,weight_t,reserve_t");
                    w2.WriteLine(line);
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("write: " + e.Message); }
        }
    }
}
