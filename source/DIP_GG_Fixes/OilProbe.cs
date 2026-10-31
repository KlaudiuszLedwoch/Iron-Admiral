// 1.11.501 (#118, author 31.10, 10:24: "oil production is already in the game - check it"): a log-only probe of the game's own oil
// mechanics before the oil supply model is built on them. Once a month (and once after loading) every great power gets one line
// with whatever the game's Player object exposes about oil and fuel - read by reflection, so a missing member only drops its value:
// GetOilPerCapita / playerOilEffect / OilProductionRating / FinancialInfoOil / provinces with oil, the fuel expense of the month,
// and the share of the nation's ships whose design burns oil (fuel_oil_1..4) against coal (fuel_coal).
// No effect on the game. Log tag [Oil].
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using MelonLoader;

namespace DipGgFixes
{
    internal static class OilProbe
    {
        static int lastMonth = -1; static int errors, lines;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Oil] " + m); }
        static string Read(object o, string name)
        {
            try
            {
                var t = o.GetType();
                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.GetIndexParameters().Length == 0) return Fmt(p.GetValue(o));
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return Fmt(f.GetValue(o));
                var m = t.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == 0);
                if (m != null) return Fmt(m.Invoke(o, null));
            }
            catch (Exception e) { return "err:" + (e.InnerException ?? e).GetType().Name; }
            return "n/a";
        }
        static string Fmt(object v)
        {
            if (v == null) return "null";
            if (v is float f) return f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            string s = v.ToString(); return s.Length > 60 ? s.Substring(0, 60) : s;
        }
        // 1.11.503 (#120, author 31.10, 11:30: let the AI design the ships it plans to build while the player thinks on the map): the
        // signatures of the game's design generation, logged once, to see how a design pass can be started outside the end of turn
        static bool sigLogged;
        static void Signatures()
        {
            if (sigLogged) return; sigLogged = true;
            try
            {
                foreach (var m in typeof(CampaignController).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.Name.Contains("GenerateRandomDesign") || m.Name.Contains("BuildNewShips") || m.Name.Contains("Prewarm") || m.Name.Contains("AiManage") || m.Name.StartsWith("NextTurn") || m.Name.Contains("EndTurn"))
                        MelonLogger.Msg("[DIP GG Fixes][DesignAhead] CampaignController." + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ") -> " + m.ReturnType.Name);
                var it = typeof(CampaignController).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(t => t.Name.Contains("GenerateRandomDesigns"));
                if (it != null) MelonLogger.Msg("[DIP GG Fixes][DesignAhead] " + it.Name + " members: " + string.Join(", ", it.GetProperties().Select(p => p.PropertyType.Name + " " + p.Name)));
            }
            catch (Exception e) { MelonLogger.Msg("[DIP GG Fixes][DesignAhead] signatures: " + e.Message); }
        }
        internal static void Tick()
        {
            try
            {
                if (!GameManager.IsCampaign) return;
                Signatures();
                var cc = CampaignController.Instance; if (cc == null) return;
                int m; try { var d = cc.CurrentDate.AsDate(); m = d.Year * 12 + d.Month; } catch { return; }
                if (m == lastMonth) return; lastMonth = m;
                if (lines > 400) return;
                var majors = Economy.MajorsPublic(); if (majors == null) return;
                foreach (var p in majors)
                {
                    if (p == null) continue;
                    string name = "?"; try { name = p.data.name; } catch { }
                    int oil = 0, coal = 0, other = 0;
                    try
                    {
                        var ships = new List<Ship>(); try { var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>(); while (un.MoveNext()) { var s0 = en.Current; if (s0 != null) ships.Add(s0); } } catch { }
                        for (int i = 0; i < ships.Count; i++)
                            {
                                var s = ships[i]; if (s == null) continue; string fuel = null;
                                try { foreach (var kv in s.components) { var cd = kv.Value; if (cd != null && cd.name != null && cd.name.StartsWith("fuel_", StringComparison.Ordinal)) { fuel = cd.name; break; } } } catch { }
                                if (fuel == null) other++; else if (fuel == "fuel_coal") coal++; else oil++;
                            }
                    }
                    catch { }
                    lines++;
                    Log($"{name} {m / 12}-{m % 12:00}: OilPerCapita {Read(p, "GetOilPerCapita")}, playerOilEffect {Read(p, "playerOilEffect")}, OilProductionRating {Read(p, "OilProductionRating")}, FinancialInfoOil {Read(p, "FinancialInfoOil")}, provinces with oil {Read(p, "provinceWhitOilCount")}, fuel expense {Read(p, "ExpensesFuel")}; ships oil {oil} / coal {coal} / unknown {other}");
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("probe: " + e.Message); }
        }
    }
}
