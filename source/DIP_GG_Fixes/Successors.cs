// 1.11.149 (beta): no land without a government. Historically almost no territory stayed without a state; in the game a
// province of a dissolved nation can stay "Ungoverned Territory" (player id neutral) for years. Every month (and right after a
// campaign is loaded) each ungoverned province goes to its claimant from provinces.csv (column claim: the historical successor,
// e.g. austria -> Austria, hungary -> Hungary, bohemia-moravia -> Czechoslovakia, croatia -> Croatia); the game switches the
// claimant on when it receives the province. Switch: UngovernedToSuccessors (F10, Campaign AI).
// Reunification: a dissolved great power whose national core provinces are all held by small successor states at peace may
// unite again - a small chance each month (ReunificationChance, % per month).
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Successors
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> Reunify;
        static Dictionary<string, string> claims;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Independence] " + m); }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("UngovernedToSuccessors", true, null, "Campaign: a province left without a government goes at once to its historical successor state (claim in provinces.csv); a dissolved great power may later unite again from its successor states");
            Reunify = cat.CreateEntry("ReunificationChance", 5.0f, null, "Campaign: chance per month (%) that a dissolved great power unites again when all its core provinces are held by small successor states at peace");
        }

        // provinces.csv: @name ... claim (column found by header name); quoted fields allowed
        static Dictionary<string, string> Claims()
        {
            if (claims != null) return claims;
            claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var f = Path.Combine(MelonUtils.GameDirectory, "Mods", "provinces.csv");
                if (!File.Exists(f)) { Log("provinces.csv not found in Mods - successor states unknown"); return claims; }
                var lines = File.ReadAllLines(f); if (lines.Length < 2) return claims;
                var head = Split(lines[0]); int ci = head.IndexOf("claim"); if (ci < 0) { Log("provinces.csv has no claim column"); return claims; }
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("#")) continue;
                    var a = Split(lines[i]); if (a.Count <= ci) continue;
                    string id = a[0].Trim(), c = a[ci].Trim(); if (id.Length > 0 && c.Length > 0) claims[id] = c;
                }
                Log($"successor states of {claims.Count} provinces read from provinces.csv");
            }
            catch (Exception e) { Log("provinces.csv: " + e.Message); }
            return claims;
        }
        // 1.11.153: the historical successor state of a province (claim column), null if none
        internal static string ClaimOf(string id) { try { var c = Claims(); string v; return c.TryGetValue(id, out v) ? v : null; } catch { return null; } }
        static List<string> Split(string s)
        {
            var r = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (var ch in s) { if (ch == '"') q = !q; else if (ch == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch); }
            r.Add(sb.ToString()); return r;
        }

        static bool Disabled(Player p) { try { return p.isDisabled; } catch { return false; } }
        static bool AtWar(Player p) { try { return p.IsAtWar(); } catch { return false; } }

        // called once a month by Independence.Tick (and on the first tick after a load)
        internal static void Run(CampaignController cc, Dictionary<string, Player> players, Il2CppSystem.Collections.Generic.Dictionary<string, Province> byId)
        {
            if (On == null || !On.Value) return;
            var cl = Claims(); if (cl.Count == 0) return;
            int moved = 0; var sb = new System.Text.StringBuilder();
            var ids = new List<string>(); foreach (var kv in byId) ids.Add(kv.Key);
            foreach (var id in ids)
            {
                try
                {
                    Province pr; if (!byId.TryGetValue(id, out pr) || pr == null) continue;
                    Player occ = null; try { occ = pr.ControllerPlayer; } catch { }
                    if (occ == null || occ.data == null || occ.data.name != "neutral") continue;
                    string c; if (!cl.TryGetValue(id, out c)) continue;
                    Player to; if (!players.TryGetValue(c, out to) || to == null) { if (sb.Length < 1200) sb.Append($"\n   {id}: successor {c} is not in this campaign - stays ungoverned"); continue; }
                    bool wasOff = Disabled(to);
                    cc.TransferProvinceToNewOwner(to, id, true, true, "independence");
                    moved++;
                    if (sb.Length < 1200) sb.Append($"\n   {id} -> {Economy.Name(to)}{(wasOff ? " (state founded)" : "")}");
                }
                catch (Exception e) { if (sb.Length < 1200) sb.Append($"\n   {id}: {e.Message}"); }
            }
            if (moved > 0 || sb.Length > 0) Log($"land without a government: {moved} province(s) went to their successor states:{sb}");
            Reunification(cc, players, byId, cl);
        }

        static void Reunification(CampaignController cc, Dictionary<string, Player> players, Il2CppSystem.Collections.Generic.Dictionary<string, Province> byId, Dictionary<string, string> cl)
        {
            if (Reunify == null || Reunify.Value <= 0f) return;
            // national cores per great power (Independence.Cores: province -> nation)
            var cores = new Dictionary<string, List<string>>();
            foreach (var kv in Independence.Cores) { if (!cores.TryGetValue(kv.Value, out var l)) { l = new List<string>(); cores[kv.Value] = l; } l.Add(kv.Key); }
            foreach (var kv in cores)
            {
                Player great; if (!players.TryGetValue(kv.Key, out great) || great == null || !Disabled(great)) continue;
                bool ok = true; var holders = new HashSet<string>();
                foreach (var id in kv.Value)
                {
                    Province pr; if (!byId.TryGetValue(id, out pr) || pr == null) { ok = false; break; }
                    Player h = null; try { h = pr.ControllerPlayer; } catch { }
                    if (h == null || h.data == null) { ok = false; break; }
                    string hn = h.data.name; string c; cl.TryGetValue(id, out c);
                    // held by its own successor (claim) state that is a small state at peace
                    bool small = false; try { small = !h.isMajor; } catch { small = hn != kv.Key; }
                    if (hn != c || !small || AtWar(h)) { ok = false; break; }
                    holders.Add(hn);
                }
                if (!ok || holders.Count == 0) continue;
                float roll = UnityEngine.Random.value * 100f;
                bool unite = roll < Reunify.Value;
                Log($"{Economy.Name(great)} (dissolved): all {kv.Value.Count} core provinces held by successor states at peace ({string.Join(", ", holders)}) - reunification chance {Reunify.Value:0.#}% - {(unite ? "the nation unites again" : "not this month")}");
                if (!unite) continue;
                int done = 0;
                foreach (var id in kv.Value) { try { cc.TransferProvinceToNewOwner(great, id, true, true, "reunification"); done++; } catch (Exception e) { Log($"{id}: {e.Message}"); } }
                Log($"{Economy.Name(great)} united again: {done} province(s), nation active {!Disabled(great)}");
            }
        }
    }
}
