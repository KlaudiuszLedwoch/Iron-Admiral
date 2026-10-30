// 1.11.60: problem reports and usage statistics - local only.
// Nothing is ever sent anywhere. "Create problem report" in the F10 panel (Tools tab) writes one zip to the desktop with the
// MelonLoader logs, the mod's settings, the version file, a list of the Mods folder (sizes, SHA-1 of the DLLs), system info,
// an error summary and - only when the player agreed - the usage statistics. The player sends that file himself (Nexus,
// Steam, GitHub, e-mail). The Windows user name is replaced by <user> in every text put into the zip.
// Usage statistics (UserData/IronAdmiral/usage.txt) are collected only after the player said yes in the question shown once
// at the first start (UsageStatistics in F10 switches it later): sessions per mod version, campaign turns, battles and battle
// minutes, last nation and year played. Counters only - no names, saves or files of the player.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Diagnostics
    {
        internal static MelonPreferences_Entry<bool> Usage;
        internal static MelonPreferences_Entry<string> Consent;
        static string version = "?";
        static int errors;
        static string Dir { get { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral"); } }
        static string UsageFile { get { return Path.Combine(Dir, "usage.txt"); } }
        static readonly SortedDictionary<string, string> stats = new SortedDictionary<string, string>();
        static bool loaded, dirty; static float nextSave;
        static bool inBattle; static float battleStart; static int lastMonth;
        internal static string LastReport = "";

        internal static void CreatePrefs(MelonPreferences_Category cat, string ver)
        {
            version = ver;
            Usage = cat.CreateEntry("UsageStatistics", false, null, "Keep anonymous usage counters on this PC (sessions, turns, battles, nation and year) and add them to a problem report - nothing is ever sent, you send the report yourself");
            Consent = cat.CreateEntry("UsageStatisticsAsked", "", null, "Internal: answer to the one-time question about usage statistics (yes/no; empty = ask at the next start)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Diagnostics] " + m); }
        // 1.11.70: a preferences file left as zeros by an interrupted write (seen 2026-10-20) is never rewritten by MelonLoader -
        // keep a copy and save the current values so the file is valid again
        internal static void RepairPreferences()
        {
            try
            {
                var cfg = Path.Combine(MelonUtils.UserDataDirectory, "MelonPreferences.cfg");
                if (!File.Exists(cfg)) return;
                var b = File.ReadAllBytes(cfg); if (b.Length == 0) return;
                int zeros = 0; for (int i = 0; i < b.Length; i++) if (b[i] == 0) zeros++;
                string good = cfg + ".lastgood";
                // 1.11.76: a healthy file is kept as .lastgood; a zeroed file is restored from it (the settings survive)
                if (zeros < b.Length / 2) { try { File.Copy(cfg, good, true); } catch { } return; }
                var bak = cfg + ".corrupt_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(cfg, bak, true);
                if (File.Exists(good))
                {
                    File.Copy(good, cfg, true);
                    try { MelonPreferences.Load(); } catch { }
                    Log("MelonPreferences.cfg was unreadable (" + zeros + " zero bytes of " + b.Length + ") - copy kept as " + Path.GetFileName(bak) + ", settings restored from the last good copy");
                    return;
                }
                MelonPreferences.Save();
                Log("MelonPreferences.cfg was unreadable (" + zeros + " zero bytes of " + b.Length + ") - copy kept as " + Path.GetFileName(bak) + ", file rewritten with the current settings");
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Diagnostics] preferences repair failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ usage counters
        static void Load()
        {
            if (loaded) return; loaded = true;
            try { if (File.Exists(UsageFile)) foreach (var l in File.ReadAllLines(UsageFile)) { int i = l.IndexOf('='); if (i > 0) stats[l.Substring(0, i)] = l.Substring(i + 1); } } catch { }
        }
        static void Add(string k, double v = 1) { Load(); double c = 0; string s; if (stats.TryGetValue(k, out s)) double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c); stats[k] = (c + v).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); dirty = true; }
        static void Set(string k, string v) { Load(); stats[k] = v; dirty = true; }
        static bool Collect { get { return Usage != null && Usage.Value && Consent != null && Consent.Value == "yes"; } }
        static bool sessionCounted;
        internal static void Tick()
        {
            try
            {
                ConsentTick();
                if (!Collect) return;
                if (!sessionCounted) { sessionCounted = true; Add("sessions"); Add("sessions_version_" + version); if (!stats.ContainsKey("first_seen")) Set("first_seen", DateTime.UtcNow.ToString("yyyy-MM-dd")); Set("last_version", version); }
                bool b = false; try { b = GameManager.IsBattle; } catch { }
                if (b && !inBattle) { inBattle = true; battleStart = Time.realtimeSinceStartup; Add("battles"); }
                else if (!b && inBattle) { inBattle = false; Add("battle_minutes", Math.Round((Time.realtimeSinceStartup - battleStart) / 60f, 1)); }
                int m = Economy.Month();
                if (m > 0 && m != lastMonth)
                {
                    if (lastMonth > 0 && m > lastMonth) Add("campaign_turns");
                    lastMonth = m; Set("last_campaign_year", (m / 12).ToString());
                    try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) if (ps[i] != null && ps[i].isMain) { Set("last_nation", ps[i].data.name); break; } } catch { }
                }
                if (dirty && Time.realtimeSinceStartup >= nextSave) { nextSave = Time.realtimeSinceStartup + 60f; SaveStats(); }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Diagnostics] tick: " + e.Message); }
        }
        static void SaveStats()
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllLines(UsageFile, stats.Select(kv => kv.Key + "=" + kv.Value)); dirty = false; } catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Diagnostics] save: " + e.Message); }
        }
        internal static void OnQuit() { if (dirty) SaveStats(); }

        // ------------------------------------------------------------------ one-time consent question
        static readonly List<KeyValuePair<Rect, Action>> cclicks = new List<KeyValuePair<Rect, Action>>(), cnext = new List<KeyValuePair<Rect, Action>>();
        static bool ShowConsent { get { return Consent != null && string.IsNullOrEmpty(Consent.Value) && Time.realtimeSinceStartup > 25f && !SafeBattle(); } }
        static bool SafeBattle() { try { return GameManager.IsBattle; } catch { return false; } }
        static void ConsentTick()
        {
            if (!ShowConsent || !Input.GetMouseButtonDown(0)) return;
            var p = UiScale.Mouse();
            foreach (var kv in cclicks) if (kv.Key.Contains(p)) { kv.Value(); break; }
        }
        static void Answer(bool yes)
        {
            Consent.Value = yes ? "yes" : "no"; Usage.Value = yes;
            try { MelonPreferences.Save(); } catch { }
            Log("usage statistics " + (yes ? "allowed" : "declined") + " by the player");
        }
        static GUIStyle sT, sD, sB;
        internal static void OnGUI()
        {
            if (!ShowConsent) { if (cclicks.Count > 0) cclicks.Clear(); return; }
            try
            {
                if (sT == null)
                {
                    sT = new GUIStyle(); sT.fontSize = 16; sT.richText = true; sT.normal.textColor = Color.white;
                    sD = new GUIStyle(); sD.fontSize = 13; sD.wordWrap = true; sD.richText = true; sD.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
                    sB = new GUIStyle(); sB.fontSize = 14; sB.alignment = TextAnchor.MiddleCenter; sB.richText = true; sB.normal.textColor = Color.white;
                }
                cnext.Clear();
                float w = Math.Min(560f, UiScale.W - 40f), h = 250f;
                var r = new Rect((UiScale.W - w) / 2f, (UiScale.H - h) / 2f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 16f, r.y + 12f, w - 32f, 24f), "<b>Iron Admiral - usage statistics</b>", sT);
                GUI.Label(new Rect(r.x + 16f, r.y + 44f, w - 32f, 140f),
                    "May the mod keep simple usage counters on this PC: sessions per mod version, campaign turns, battles and their length, the nation and year you play?\n\n" +
                    "Nothing is sent anywhere. The counters only go into a problem report that you create yourself (F10 > Tools) and send if you want to. No names, saves or other files. You can change this any time in F10 (UsageStatistics).", sD);
                var yes = new Rect(r.x + 16f, r.yMax - 46f, (w - 48f) / 2f, 32f); var no = new Rect(yes.xMax + 16f, yes.y, yes.width, 32f);
                GUI.Box(yes, ""); GUI.Label(yes, "<color=#7CFC7C><b>Yes, keep counters</b></color>", sB); cnext.Add(new KeyValuePair<Rect, Action>(yes, () => Answer(true)));
                GUI.Box(no, ""); GUI.Label(no, "<color=#FF8080><b>No</b></color>", sB); cnext.Add(new KeyValuePair<Rect, Action>(no, () => Answer(false)));
                cclicks.Clear(); cclicks.AddRange(cnext);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Diagnostics] consent window: " + e.Message); }
        }

        // ------------------------------------------------------------------ problem report
        static string Anon(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home)) { s = s.Replace(home, "<home>"); s = s.Replace(home.Replace('\\', '/'), "<home>"); }
                var u = Environment.UserName; if (!string.IsNullOrEmpty(u) && u.Length > 2) s = s.Replace(u, "<user>");
                var m = Environment.MachineName; if (!string.IsNullOrEmpty(m) && m.Length > 2) s = s.Replace(m, "<pc>");
            }
            catch { }
            return s;
        }
        static string ReadShared(string path) { using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var sr = new StreamReader(fs)) return sr.ReadToEnd(); }
        // 1.11.420 (author 30.10 06:45: "add to the help and where the logs are made instructions how to add them on GitHub")
        internal const string GitHubIssues = "https://github.com/KlaudiuszLedwoch/Iron-Admiral/issues";
        internal const string GitHubHowTo =
            "How to add the report on GitHub:\n" +
            "1. Sign in at github.com (a free account is enough).\n" +
            "2. Open " + GitHubIssues + " - pick the issue about your problem, or click 'New issue'.\n" +
            "3. Write what happened: nation, year, what you did and what you expected.\n" +
            "4. Drag the zip from your desktop into the comment box (or click the attach line under it) and wait until the upload has finished - a link to the file appears in the text.\n" +
            "5. Click 'Comment' (or 'Create' for a new issue).\n" +
            "Files up to 25 MB can be attached; a zip or a Latest.log works as it is. Nexus and Steam do not take files - post the link or attach it on GitHub.";
        static void AddText(ZipArchive z, string name, string text)
        {
            var e = z.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(Anon(text));
        }
        internal static string CreateReport()
        {
            try
            {
                string game = MelonUtils.GameDirectory;
                string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) desk = game;
                string file = Path.Combine(desk, "IronAdmiral_report_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".zip");
                if (dirty) SaveStats();
                string latest = Path.Combine(game, "MelonLoader", "Latest.log");
                string log = File.Exists(latest) ? ReadShared(latest) : "(no Latest.log)";
                using (var fs = new FileStream(file, FileMode.Create))
                using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    AddText(z, "README.txt", "Iron Admiral problem report, created " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " by the player from the F10 panel.\n" +
                        "Contents: MelonLoader logs, the mod's settings, version file, Mods folder listing, system info, error summary" + (Collect ? ", usage counters" : "") + ".\n" +
                        "The Windows user name and PC name are replaced by <user>/<pc>. Please describe what happened when you send this file.\n\n" + GitHubHowTo + "\n");
                    AddText(z, "Latest.log", log);
                    try
                    {
                        var ld = Path.Combine(game, "MelonLoader", "Logs");
                        if (Directory.Exists(ld)) foreach (var f in new DirectoryInfo(ld).GetFiles("*.log").OrderByDescending(x => x.LastWriteTimeUtc).Take(3))
                            AddText(z, "older_logs/" + f.Name, ReadShared(f.FullName));
                    }
                    catch { }
                    try
                    {
                        var cfg = Path.Combine(MelonUtils.UserDataDirectory, "MelonPreferences.cfg");
                        if (File.Exists(cfg))
                        {
                            var sb = new StringBuilder(); bool on = false;
                            foreach (var l in ReadShared(cfg).Split('\n')) { var t = l.TrimEnd('\r'); if (t.StartsWith("[")) on = t.StartsWith("[DIP_GG_Fixes]"); if (on) sb.AppendLine(t); }
                            AddText(z, "settings_DIP_GG_Fixes.cfg", sb.ToString());
                        }
                    }
                    catch { }
                    try { var vf = Path.Combine(game, "Mods", "IRON_ADMIRAL_VERSION.txt"); if (File.Exists(vf)) AddText(z, "IRON_ADMIRAL_VERSION.txt", ReadShared(vf)); } catch { }
                    AddText(z, "mods_folder.txt", ModsListing(Path.Combine(game, "Mods")));
                    AddText(z, "system.txt", SystemInfo());
                    AddText(z, "error_summary.txt", ErrorSummary(log));
                    if (Collect && File.Exists(UsageFile)) AddText(z, "usage.txt", ReadShared(UsageFile));
                }
                if (Collect) Add("problem_reports");
                Log("problem report written: " + Anon(file));
                LastReport = file;
                return file;
            }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Diagnostics] report failed: " + e.Message); return null; }
        }
        static string ModsListing(string mods)
        {
            var sb = new StringBuilder();
            try
            {
                if (!Directory.Exists(mods)) return "(no Mods folder)";
                foreach (var f in new DirectoryInfo(mods).GetFiles("*", SearchOption.AllDirectories).OrderBy(x => x.FullName))
                {
                    string rel = f.FullName.Substring(mods.Length).TrimStart('\\', '/');
                    if (rel.Split('\\', '/').Length > 2) continue; // skip deep asset folders
                    string sha = "";
                    if (f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        try { using (var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var h = System.Security.Cryptography.SHA1.Create()) sha = "  sha1 " + BitConverter.ToString(h.ComputeHash(s)).Replace("-", ""); } catch { }
                    sb.AppendLine($"{rel}  {f.Length} B  {f.LastWriteTime:yyyy-MM-dd HH:mm}{sha}");
                }
            }
            catch (Exception e) { sb.AppendLine("listing failed: " + e.Message); }
            return sb.ToString();
        }
        static string SystemInfo()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DIP_GG_Fixes: " + version);
            try { sb.AppendLine("Game: " + Application.version + " (Unity " + Application.unityVersion + ")"); } catch { }
            try { sb.AppendLine("OS: " + SystemInfo_("os")); } catch { }
            try { sb.AppendLine(".NET: " + Environment.Version); } catch { }
            try { sb.AppendLine("Screen: " + Screen.width + "x" + Screen.height + ", mod UI scale " + UiScale.F.ToString("0.00")); } catch { }
            try { sb.AppendLine("CPU/GPU/RAM: " + SystemInfo_("hw")); } catch { }
            try { sb.AppendLine("In battle: " + SafeBattle() + ", campaign month: " + Economy.Month()); } catch { }
            return sb.ToString();
        }
        static string SystemInfo_(string what)
        {
            if (what == "os") return UnityEngine.SystemInfo.operatingSystem;
            return UnityEngine.SystemInfo.processorType + " / " + UnityEngine.SystemInfo.graphicsDeviceName + " / " + UnityEngine.SystemInfo.systemMemorySize + " MB";
        }
        static string ErrorSummary(string log)
        {
            var sb = new StringBuilder();
            var lines = log.Split('\n');
            int warn = 0, err = 0, ours = 0; var top = new Dictionary<string, int>();
            foreach (var raw in lines)
            {
                var l = raw.TrimEnd('\r');
                bool w = l.Contains("[WARNING]") || l.Contains(" Warning"), e = l.Contains("[ERROR]") || l.Contains("Exception");
                if (!w && !e) continue;
                if (w) warn++; if (e) err++; if (l.Contains("DIP GG Fixes")) ours++;
                string k = l.Length > 14 && l[0] == '[' ? l.Substring(l.IndexOf(']') + 1).Trim() : l.Trim();
                if (k.Length > 200) k = k.Substring(0, 200);
                int c; top.TryGetValue(k, out c); top[k] = c + 1;
            }
            sb.AppendLine($"Lines: {lines.Length}, warnings: {warn}, errors/exceptions: {err}, of them from DIP GG Fixes: {ours}");
            sb.AppendLine("Most frequent:");
            foreach (var kv in top.OrderByDescending(x => x.Value).Take(25)) sb.AppendLine($"{kv.Value,6} x {kv.Key}");
            return sb.ToString();
        }
    }
}
