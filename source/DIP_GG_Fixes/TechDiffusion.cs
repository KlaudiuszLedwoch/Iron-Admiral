// 1.11.228 (author's decision 24.10): technology spreads. In the 1922 campaign the AI powers were 20-30 years behind the player
// (newest hull technologies 1895-1919 against 1936) and fought with pre-dreadnoughts. Two rules, as every navy of the period:
//  1. A technology that other great powers already have is researched faster - the more of them know it and the further behind
//     the researcher is, the faster (bought, copied, read in the journals). A technology nobody else has, whose year lies more
//     than TechAheadYears beyond the frontier of the other powers in that field, is researched slower (nothing to copy).
//  2. Catch-up: a power the game rates "Very Behind" receives every turn one technology that all the other great powers already
//     know (the earliest first - the prerequisites come in order); a power rated "Behind" one every second turn.
// Both apply to the AI and the player alike. Switches: TechDiffusion, TechCatchUp (F10, Campaign).
// Hooks: CampaignController.GetResearchSpeed(Player, Technology) (speed multiplier); Player.techLevel (level per field) for the
// standing of each power, Player.technologies / technologiesResearchedAll for what is known.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TechDiffusion
    {
        internal static MelonPreferences_Entry<bool> On, CatchUp;
        internal static MelonPreferences_Entry<float> KnownBonus, AheadPenalty, BehindAt, VeryBehindAt;
        internal static MelonPreferences_Entry<int> AheadYears;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Tech] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("TechDiffusion", true, null, "Campaign: technologies the other great powers already have are researched faster (the more of them know it and the further behind the researcher is, the faster); a technology nobody has, years ahead of the others, is researched slower");
            KnownBonus = cat.CreateEntry("TechKnownBonus", 2f, null, "Research speed added when every other great power knows the technology (x1 + this; halved when the researcher is not behind at all)");
            AheadYears = cat.CreateEntry("TechAheadYears", 10, null, "A technology nobody else has, whose year is more than this many years beyond the other powers' frontier in that field, counts as ahead of its time");
            AheadPenalty = cat.CreateEntry("TechAheadSpeed", 0.5f, null, "Research speed multiplier for a technology ahead of its time");
            CatchUp = cat.CreateEntry("TechCatchUp", true, null, "Campaign: a great power far behind receives technologies that all the other great powers already know - Very Behind: one every turn, Behind: one every second turn (AI and player alike)");
            BehindAt = cat.CreateEntry("TechBehindAt", 0.85f, null, "A power counts as Behind when its technology level (sum of field levels) is below this share of the other great powers' average");
            VeryBehindAt = cat.CreateEntry("TechVeryBehindAt", 0.7f, null, "A power counts as Very Behind below this share of the other great powers' average");
        }

        // ---- standing of the powers, refreshed once a month ----
        class Snap
        {
            public int Month; public List<Player> Majors = new List<Player>();
            public Dictionary<IntPtr, HashSet<string>> Known = new Dictionary<IntPtr, HashSet<string>>();   // player -> researched tech names
            public Dictionary<IntPtr, float> Score = new Dictionary<IntPtr, float>();                       // player -> sum of field levels
            public Dictionary<string, int> Frontier = new Dictionary<string, int>();                        // field -> newest year known by anyone
            public Dictionary<string, int> KnownBy = new Dictionary<string, int>();                         // tech -> how many majors know it
        }
        static Snap snap; static int errors;
        static int MonthIndex() { try { var d = CampaignController.Instance.CurrentDate.AsDate(); return d.Year * 12 + d.Month; } catch { return -1; } }
        static Snap Current()
        {
            int m = MonthIndex(); if (snap != null && snap.Month == m) return snap;
            var s = new Snap { Month = m };
            try
            {
                var cc = CampaignController.Instance; var players = cc != null && cc.CampaignData != null ? cc.CampaignData.Players : null; if (players == null) return snap = s;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i]; if (p == null) continue;
                    bool ok = false; try { ok = p.isMajor && !p.isDisabled; } catch { }
                    if (!ok) continue;
                    s.Majors.Add(p);
                    var known = new HashSet<string>();
                    try
                    {
                        var e = p.technologiesResearchedAll.GetEnumerator(); var ue = e.Cast<Il2CppSystem.Collections.IEnumerator>();
                        while (ue.MoveNext()) { var t = e.Current; if (t == null || t.data == null) continue; known.Add(t.data.name); int y = t.data.year; string f = t.data.type ?? ""; int fy; if (!s.Frontier.TryGetValue(f, out fy) || y > fy) s.Frontier[f] = y; }
                    }
                    catch { }
                    s.Known[p.Pointer] = known;
                    foreach (var n in known) { int c; s.KnownBy.TryGetValue(n, out c); s.KnownBy[n] = c + 1; }
                    float score = 0f; try { var tl = p.techLevel; if (tl != null) foreach (var kv in tl) score += kv.Value; } catch { }
                    if (score <= 0f) score = known.Count;
                    s.Score[p.Pointer] = score;
                }
            }
            catch (Exception e) { if (errors++ < 3) Log("snapshot: " + e.Message); }
            return snap = s;
        }
        // own level as a share of the other great powers' average (1 = level with them)
        static float Standing(Snap s, Player p)
        {
            float own; if (!s.Score.TryGetValue(p.Pointer, out own)) return 1f;
            float sum = 0f; int n = 0; foreach (var kv in s.Score) if (kv.Key != p.Pointer) { sum += kv.Value; n++; }
            if (n == 0 || sum <= 0f) return 1f;
            return own / (sum / n);
        }

        // ---- 1. research speed ----
        internal static float SpeedMult(Player p, Technology t)
        {
            if (On == null || !On.Value || p == null || t == null || t.data == null) return 1f;
            try
            {
                var s = Current(); if (s.Majors.Count < 2) return 1f;
                string name = t.data.name; int knownBy; s.KnownBy.TryGetValue(name, out knownBy);
                HashSet<string> own; if (s.Known.TryGetValue(p.Pointer, out own) && own.Contains(name)) knownBy--;
                int others = s.Majors.Count - 1; float share = others > 0 ? Math.Max(0f, Math.Min(1f, knownBy / (float)others)) : 0f;
                float standing = Standing(s, p); float behind = Math.Max(0f, Math.Min(1f, (1f - standing) / 0.3f)); // 1 when 30 % or more below the others
                float mult = 1f;
                if (share > 0f) mult *= 1f + (KnownBonus != null ? KnownBonus.Value : 2f) * share * (0.5f + 0.5f * behind);
                else
                {
                    int fy; string f = t.data.type ?? "";
                    if (s.Frontier.TryGetValue(f, out fy) && t.data.year > fy + (AheadYears != null ? AheadYears.Value : 10)) mult *= Math.Max(0.1f, AheadPenalty != null ? AheadPenalty.Value : 0.5f);
                }
                return mult;
            }
            catch (Exception e) { if (errors++ < 3) Log("speed: " + e.Message); return 1f; }
        }

        // ---- 2. catch-up grants, once a month after the turn ----
        static int grantMonth = -1; static readonly Dictionary<IntPtr, int> lastGrant = new Dictionary<IntPtr, int>();
        internal static void Tick()
        {
            if (CatchUp == null || !CatchUp.Value) return;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle) return;
                int m = MonthIndex(); if (m < 0 || m == grantMonth) return; grantMonth = m;
                var s = Current(); if (s.Majors.Count < 3) return;
                foreach (var p in s.Majors)
                {
                    float st = Standing(s, p);
                    bool very = st < (VeryBehindAt != null ? VeryBehindAt.Value : 0.7f), behind = st < (BehindAt != null ? BehindAt.Value : 0.85f);
                    if (!behind) continue;
                    int last; lastGrant.TryGetValue(p.Pointer, out last);
                    if (!very && last > 0 && m - last < 2) continue;     // Behind: every second turn
                    // candidates: known by every other major, not by this one; earliest year first (prerequisites first)
                    HashSet<string> own; s.Known.TryGetValue(p.Pointer, out own); own = own ?? new HashSet<string>();
                    Technology pick = null; int bestYear = int.MaxValue;
                    var techs = p.technologies; if (techs == null) continue;
                    for (int i = 0; i < techs.Count; i++)
                    {
                        var t = techs[i]; if (t == null || t.data == null) continue;
                        string n = t.data.name; if (own.Contains(n)) continue;
                        bool done = false; try { done = t.isResearched; } catch { }
                        if (done) continue;
                        int kb; s.KnownBy.TryGetValue(n, out kb); if (kb < s.Majors.Count - 1) continue;
                        int y = t.data.year; if (y < bestYear) { bestYear = y; pick = t; }
                    }
                    if (pick == null) continue;
                    try { pick.progress = 1f; } catch { }
                    bool now = false; try { now = pick.isResearched; } catch { }
                    if (!now) { try { pick.progress = 1.0001f; now = pick.isResearched; } catch { } }
                    try { p.OnTechnologiesChanged(); } catch { }
                    lastGrant[p.Pointer] = m; own.Add(pick.data.name);
                    string nice = pick.data.name; try { nice = LocalizeManager.Localize(pick.data.nameUi) ?? nice; } catch { }
                    Log($"{Economy.Name(p)} ({(very ? "very behind" : "behind")}, level {st:P0} of the others): receives {nice} ({pick.data.type}, {pick.data.year}) - known to every other great power{(now ? "" : " (progress set, not yet marked researched)")}");
                    bool main = false; try { main = p.isMain; } catch { }
                    if (main) Log($"player: {nice} received as common knowledge abroad");
                }
            }
            catch (Exception e) { if (errors++ < 3) Log("catch-up: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CampaignController), "GetResearchSpeed")]
    internal static class Patch_ResearchSpeed_Diffusion
    {
        static void Postfix(Player __0, Technology __1, ref float __result) { try { __result *= TechDiffusion.SpeedMult(__0, __1); } catch { } }
    }
}
