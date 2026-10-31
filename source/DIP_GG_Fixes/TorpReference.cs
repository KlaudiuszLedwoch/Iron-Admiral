// 1.11.494 (author 31.10: "add a torpedo table to the penetration table"): ten seconds into every battle, every ship with torpedoes
// is written once per navy, propulsion generation, diameter and range to UserData\IronAdmiral\torpedo_reference.csv: the range and
// speed the battle actually uses on each torpedo setting it has (high speed, long range, extreme range), with the diameter and
// propulsion of that ship. The wiki's torpedo table (Penetration table page) is made from it. Switch: TorpedoReference.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class TorpReference
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TorpedoReference", true, null, "Every battle writes the range and speed of each ship's torpedoes on every setting to UserData\\IronAdmiral\\torpedo_reference.csv - the source of the torpedo table on the wiki");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][TorpRef] " + m); }
        static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "torpedo_reference.csv"); } }
        const string Header = "nation,ship_year,gen,tech_year,diameter,lr_m,lr_kn,hs_m,hs_kn,er_m,er_kn,settings,ship";
        static HashSet<string> keys; static IntPtr battle; static float since = -1f; static bool done; static int errors;

        static void LoadKeys()
        {
            if (keys != null) return; keys = new HashSet<string>();
            try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath)) { var c = l.Split(','); if (c.Length > 6 && c[0] != "nation") keys.Add(c[0] + "|" + c[2] + "|" + c[4] + "|" + c[5]); } } catch { }
        }
        static string Diameter(Ship s)
        {
            try
            {
                var cl = s.components;
                if (cl != null) foreach (var kv in cl)
                    {
                        var cd = kv.Value; if (cd == null || cd.name == null || !cd.name.StartsWith("torpedo_diameter", StringComparison.Ordinal)) continue;
                        string t = null; try { t = LocalizeManager.Localize("$component_name_short_" + cd.name); } catch { }
                        if (string.IsNullOrEmpty(t) || t.StartsWith("$", StringComparison.Ordinal)) t = cd.name;
                        return t.Replace(",", " ").Trim();
                    }
            }
            catch { }
            return "standard";
        }
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool b = false; try { b = GameManager.IsBattle; } catch { }
                if (!b) { battle = IntPtr.Zero; since = -1f; done = false; return; }
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null || cb.Ships == null) return;
                if (cb.Pointer != battle) { battle = cb.Pointer; since = Time.realtimeSinceStartup; done = false; }
                if (done || Time.realtimeSinceStartup - since < 10f) return;
                done = true; LoadKeys();
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new StringBuilder(); int n = 0;
                for (int i = 0; i < cb.Ships.Count; i++)
                {
                    var s = cb.Ships[i]; if (s == null) continue;
                    try
                    {
                        bool tp = false; try { tp = s.haveTorpedoes; } catch { } if (!tp) continue;
                        float rng = BattleAi.TorpedoRange(s); float ms = 0f; try { ms = s.TorpedoSpeed(false); } catch { }
                        if (rng <= 0f || ms <= 0.5f) continue;
                        int gen, cnt, cur; float[] rr, vv;
                        if (!TorpedoSettings.Describe(s, out gen, out cnt, out rr, out vv, out cur)) { gen = 0; cnt = 1; rr = new[] { 1f, 1f, 1f }; vv = new[] { 1f, 1f, 1f }; cur = TorpedoSettings.LR; }
                        float lrM = rng / Mathf.Max(0.01f, rr[cur]), lrKn = ms / 0.5144f / Mathf.Max(0.01f, vv[cur]);
                        string nat = ""; try { nat = s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { }
                        string dia = Diameter(s);
                        string key = $"{nat}|{gen}|{dia}|{Math.Round(lrM / 50f) * 50f:0}";
                        if (!keys.Add(key)) continue;
                        int year = 0; try { year = (int)s.GetYear(s); } catch { }
                        string hs = cnt >= 2 ? $"{(lrM * rr[TorpedoSettings.HS]).ToString("0", ci)},{(lrKn * vv[TorpedoSettings.HS]).ToString("0.0", ci)}" : ",";
                        string er = cnt >= 3 ? $"{(lrM * rr[TorpedoSettings.ER]).ToString("0", ci)},{(lrKn * vv[TorpedoSettings.ER]).ToString("0.0", ci)}" : ",";
                        sb.Append($"{nat},{year},{gen},,{dia},{Math.Round(lrM / 50f) * 50f:0},{lrKn.ToString("0.0", ci)},{hs},{er},{cnt},{(s.name ?? "").Replace(",", " ")}{Environment.NewLine}");
                        n++;
                    }
                    catch (Exception e) { if (errors++ < 5) Log("ship: " + e.Message); }
                }
                if (n == 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
                File.AppendAllText(FilePath, sb.ToString());
                Log($"{n} torpedo row(s) added to the torpedo reference ({keys.Count} in all)");
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
    }
}
