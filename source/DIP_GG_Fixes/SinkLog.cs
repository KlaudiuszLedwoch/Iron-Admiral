// 1.11.456 (author 30.10 18:55: "I also want statistics - sunk ships with names etc."): every ship sunk in a battle (campaign or
// custom) is written to UserData\IronAdmiral\sinkings.csv - date, the ship with her class, type, tonnage and nation, and the ship
// that sank her (the one that did her the most damage in the battle, by gunfire or torpedo; "aircraft" for aerial torpedoes) with
// her class. The designer's ranking shows the kills of every design and a list of the latest sinkings. Switch: DesignStats.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class SinkLog
    {
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Sinkings] " + m); }
        internal static string FilePath { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "sinkings.csv"); } }
        const string Header = "date,battle,ship,class,type,tons,nation,sunk_by,by_class,by_nation,weapon,by_design_id";
        class Hit { public Ship By; public float Dmg; public string Weapon; }
        static readonly Dictionary<IntPtr, Dictionary<IntPtr, Hit>> dealt = new Dictionary<IntPtr, Dictionary<IntPtr, Hit>>();
        static readonly Dictionary<IntPtr, Ship> torpOwner = new Dictionary<IntPtr, Ship>();
        static readonly HashSet<IntPtr> written = new HashSet<IntPtr>();
        static IntPtr battle; static int errors;

        static bool On { get { return DesignStats.On != null && DesignStats.On.Value; } }
        static void CheckBattle()
        {
            IntPtr b = IntPtr.Zero; try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; b = cb != null ? cb.Pointer : IntPtr.Zero; } catch { }
            if (b != battle) { battle = b; dealt.Clear(); torpOwner.Clear(); written.Clear(); }
        }

        // from the damage path (LightShipDamage.Apply): who hit whom, how hard
        internal static void Credit(Ship target, float dmg)
        {
            if (!On || target == null || !(dmg > 0f)) return;
            try
            {
                CheckBattle();
                Ship by = null; string weapon = "gunfire";
                if (LightShipDamage.InTorpedo > 0)
                {
                    weapon = "torpedo";
                    if (AirStrike.IsAerial(LightShipDamage.CurTorp)) weapon = "aerial torpedo";
                    else torpOwner.TryGetValue(LightShipDamage.CurTorp, out by);
                }
                else { var sh = PhysPen.ShotOf(PhysPen.Current); if (sh != null) { by = sh.Shooter; try { weapon = $"{sh.G.CalMm:0} mm gunfire"; } catch { } } }
                IntPtr key = by != null ? by.Pointer : (weapon == "aerial torpedo" ? new IntPtr(1) : IntPtr.Zero);
                if (key == IntPtr.Zero) return;
                if (!dealt.TryGetValue(target.Pointer, out var m)) { m = new Dictionary<IntPtr, Hit>(); dealt[target.Pointer] = m; }
                if (!m.TryGetValue(key, out var h)) { h = new Hit { By = by, Weapon = weapon }; m[key] = h; }
                h.Dmg += dmg; h.Weapon = weapon;
            }
            catch (Exception e) { if (errors++ < 5) Log("credit: " + e.Message); }
        }

        static string Clean(string s) { return (s ?? "").Replace(',', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim(); }
        static string Nation(Ship s) { try { return s.player != null && s.player.data != null ? s.player.data.name : ""; } catch { return ""; } }
        static Ship DesignOf(Ship s) { Ship d = null; try { d = s.design; } catch { } return d ?? s; }
        static string ClassOf(Ship s) { try { var d = DesignOf(s); return Clean(d.vesselName); } catch { return ""; } }
        static string TypeOf(Ship s) { try { return s.shipType != null ? s.shipType.name : ""; } catch { return ""; } }
        static string Date()
        {
            try { var cc = CampaignController.Instance; bool custom = false; try { custom = GameManager.IsCustomBattle; } catch { } if (cc != null && !custom) { var d = cc.CurrentDate.AsDate(); return $"{d.Year}-{d.Month:00}"; } } catch { }
            return "custom " + DateTime.Now.ToString("yyyy-MM-dd");
        }

        internal static void OnSunk(Ship victim)
        {
            if (!On || victim == null) return;
            try
            {
                CheckBattle();
                if (!written.Add(victim.Pointer)) return;
                Hit best = null;
                if (dealt.TryGetValue(victim.Pointer, out var m)) foreach (var h in m.Values) if (best == null || h.Dmg > best.Dmg) best = h;
                float tons = 0f; try { tons = victim.Tonnage(); } catch { }
                bool custom = false; try { custom = GameManager.IsCustomBattle; } catch { }
                string by = best == null ? "unknown" : best.By != null ? Clean(best.By.name) : "aircraft";
                string byClass = best != null && best.By != null ? ClassOf(best.By) : "", byNation = best != null && best.By != null ? Nation(best.By) : "";
                string byId = ""; try { if (best != null && best.By != null) byId = DesignOf(best.By).id.ToString(); } catch { }
                string line = $"{Date()},{(custom ? "custom" : "campaign")},{Clean(victim.name)},{ClassOf(victim)},{TypeOf(victim)},{tons:0},{Nation(victim)},{by},{byClass},{byNation},{(best != null ? best.Weapon : "")},{byId}";
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, Header + Environment.NewLine);
                File.AppendAllText(FilePath, line + Environment.NewLine);
                rows = null;
                Log($"{victim.name} ({tons:0} t) sunk by {by}{(byClass.Length > 0 ? $" ({byClass})" : "")}{(best != null ? ", " + best.Weapon : "")}");
            }
            catch (Exception e) { if (errors++ < 5) Log("sunk: " + e.Message); }
        }

        // the rows for the designer
        internal class Row { public string Date, Ship, Class, Type, Nation, By, ByClass, Weapon, ById; public float Tons; }
        static List<Row> rows; static DateTime rowsAt;
        internal static List<Row> Rows()
        {
            try
            {
                var t = File.Exists(FilePath) ? File.GetLastWriteTime(FilePath) : DateTime.MinValue;
                if (rows != null && t == rowsAt) return rows;
                rows = new List<Row>(); rowsAt = t;
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                    {
                        var c = l.Split(','); if (c.Length < 12 || c[0] == "date") continue;
                        float.TryParse(c[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tn);
                        rows.Add(new Row { Date = c[0], Ship = c[2], Class = c[3], Type = c[4], Tons = tn, Nation = c[6], By = c[7], ByClass = c[8], Weapon = c[10], ById = c[11] });
                    }
            }
            catch { rows = rows ?? new List<Row>(); }
            return rows;
        }
        internal static int KillsOf(string designId) { int n = 0; if (string.IsNullOrEmpty(designId)) return 0; foreach (var r in Rows()) if (r.ById == designId) n++; return n; }

        [HarmonyPatch(typeof(Torpedo), "Create", new Type[] { typeof(Part), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float) })]
        static class TorpPatch
        {
            static void Postfix(Part __0, Torpedo __result)
            {
                try { if (__result != null && __0 != null && __0.ship != null) { CheckBattle(); torpOwner[__result.Pointer] = __0.ship; } } catch { }
            }
        }
    }
}
