// 1.11.439 (author 30.10 15:57: "a real list of technological advancement is needed - now everyone is advanced - make it relative"):
// the game's World tab labels each nation's technology (Very advanced / Advanced / Average / Behind / Very behind) against the
// year; with the mod's technology years every nation came out "Advanced". The label is now relative: the number of technologies
// each major nation has researched, compared with the other active majors (how far from their mean, in standard deviations),
// with the place in the ranking, e.g. "Advanced (2/8)". Switch: TechLevelRelative.
using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class TechRank
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TechLevelRelative", true, null, "Campaign, World tab: the technology level of a nation is relative to the other major nations (researched technologies against their average, with the place in the ranking) instead of the game's label against the year, which showed every nation as advanced");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][TechRank] " + m); }
        static readonly Dictionary<IntPtr, int> score = new Dictionary<IntPtr, int>();
        static readonly List<int> sorted = new List<int>();
        static float mean, sd, at = -100f; static int logged, errors; static string lastRanking;

        static int Count(Player p)
        {
            int n = 0;
            try
            {
                var typed = p.technologiesResearchedAll.GetEnumerator();
                var untyped = typed.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (untyped.MoveNext()) n++;
            }
            catch { }
            return n;
        }
        static void Refresh()
        {
            if (Time.realtimeSinceStartup - at < 5f && score.Count > 0) return;
            at = Time.realtimeSinceStartup; score.Clear(); sorted.Clear();
            var m = CampaignController.Instance.CampaignData.PlayersMajor;
            for (int i = 0; i < m.Count; i++)
            {
                var p = m[i]; if (p == null) continue;
                bool off = false; try { off = p.isDisabled; } catch { }
                if (off) continue;
                int c = Count(p); score[p.Pointer] = c; sorted.Add(c);
            }
            sorted.Sort((a, b) => b.CompareTo(a));
            mean = 0f; foreach (var c in sorted) mean += c; mean /= Math.Max(1, sorted.Count);
            float v = 0f; foreach (var c in sorted) v += (c - mean) * (c - mean); sd = Mathf.Sqrt(v / Math.Max(1, sorted.Count));
            try
            {
                var names = new List<KeyValuePair<string, int>>();
                for (int i = 0; i < m.Count; i++) { var p = m[i]; if (p != null && score.TryGetValue(p.Pointer, out int c2)) names.Add(new KeyValuePair<string, int>(p.data.name, c2)); }
                names.Sort((a, b) => b.Value.CompareTo(a.Value));
                string line = string.Join(", ", names.ConvertAll(kv => kv.Key + " " + kv.Value));
                if (line != lastRanking) { lastRanking = line; Log("ranking: " + line); }
            }
            catch { }
        }
        static string Label(float z)
        {
            if (z >= 1.0f) return Loc("$Ui_World_VeryAdvanced", "Very advanced", "#7CFC7C");
            if (z >= 0.4f) return Loc("$Ui_World_Advanced", "Advanced", "#B8F0A0");
            if (z > -0.4f) return Loc("$Ui_World_Average", "Average", "#E8E8E8");
            if (z > -1.0f) return Loc("$Ui_World_Behind", "Behind", "#FFB070");
            return Loc("$Ui_World_VeryBehind", "Very behind", "#FF7070");
        }
        static string Loc(string key, string def, string col)
        {
            string t = null; try { t = LocalizeManager.Localize(key); } catch { }
            if (string.IsNullOrEmpty(t) || t.StartsWith("$")) t = def;
            return "<color=" + col + ">" + t + "</color>";
        }

        [HarmonyPatch(typeof(Ui), "TechAdvanceText", new Type[] { typeof(Player), typeof(bool) })]
        static class Patch
        {
            static void Postfix(Player __0, ref string __result)
            {
                if (On == null || !On.Value || __0 == null) return;
                try
                {
                    Refresh();
                    if (!score.TryGetValue(__0.Pointer, out int c) || sorted.Count < 2) return;
                    float z = sd < 1.5f ? 0f : (c - mean) / sd;   // nearly equal nations: all average
                    int place = sorted.IndexOf(c) + 1;
                    string was = __result;
                    __result = Label(z) + " (" + place + "/" + sorted.Count + ", " + c + " tech.)";
                    if (logged++ < 10) Log($"{__0.data.name}: {c} technologies (majors: mean {mean:0.0}, sd {sd:0.0}, z {z:0.00}) - '{was}' -> '{__result}'");
                }
                catch (Exception e) { if (errors++ < 5) Log("label: " + e.Message); }
            }
        }
    }
}
