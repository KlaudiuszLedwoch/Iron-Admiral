// 1.11.139 (beta): log sharing for testers. With the player's consent the mod sends parts of the game log to the author's
// private log folder: the log of each battle (from the battle decision to the return to the map), the last lines after an error
// of the mod, and the whole session log when the game is closed. The Windows user name is removed from the text; logs are kept
// for 90 days. Nothing is sent without consent (a question in the main menu once; F10 "Share logs" at any time).
// Reward: the "Beta updates" switch works only while log sharing is on (testers get the beta versions through the update check).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class LogShare
    {
        // web app of the log reception (Google Apps Script); empty = feature dormant
        const string DefaultUrl = "";
        const string Token = "ia-logs-v1";
        internal static MelonPreferences_Entry<bool> On, Asked;
        internal static MelonPreferences_Entry<string> Id, UrlPref;
        static string Url { get { try { var u = UrlPref?.Value; if (!string.IsNullOrWhiteSpace(u)) return u.Trim(); } catch { } return DefaultUrl; } }
        internal static bool Available => !string.IsNullOrEmpty(Url);
        internal static bool Active => On != null && On.Value && !string.IsNullOrEmpty(Url);
        static int battles, errorsSent, failures; static long battleStart = -1, scanPos; static bool inBattle, quitSent;
        static float nextScan, lastError = -9999f;
        static string LogPath => Path.Combine(MelonUtils.GameDirectory, "MelonLoader", "Latest.log");

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ShareLogs", false, null, "Share logs with the author: battle logs, errors and the session log are sent to help fix and balance the mod (the Windows user name is removed). Required for Beta updates");
            Asked = cat.CreateEntry("ShareLogsAsked", false, null, "internal: the log sharing question was answered", true);
            Id = cat.CreateEntry("ShareLogsId", "", null, "internal: anonymous tester id", true);
            UrlPref = cat.CreateEntry("ShareLogsUrl", "", null, "internal: log reception address (empty = built-in)", true);
            try { if (string.IsNullOrEmpty(Id.Value)) { Id.Value = Guid.NewGuid().ToString("N").Substring(0, 16); MelonPreferences.Save(); } } catch { }
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Logs] " + m); }

        // ---------------------------------------------------------------- reading and sending
        static string Read(long from, long maxBytes, out long end)
        {
            end = from;
            try
            {
                using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length; end = len;
                    if (from < 0 || from > len) from = 0;
                    if (len - from > maxBytes) from = len - maxBytes;
                    fs.Seek(from, SeekOrigin.Begin);
                    var buf = new byte[len - from]; int n = 0; while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
                    return Encoding.UTF8.GetString(buf, 0, n);
                }
            }
            catch { return null; }
        }
        static long Length() { try { return new FileInfo(LogPath).Length; } catch { return 0; } }

        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            try
            {
                // C:\Users\<name>\... -> C:\Users\<user>\...
                var sb = new StringBuilder(s.Length); int i = 0; const string key = "\\Users\\";
                while (i < s.Length)
                {
                    int k = s.IndexOf(key, i, StringComparison.OrdinalIgnoreCase);
                    if (k < 0) { sb.Append(s, i, s.Length - i); break; }
                    k += key.Length; sb.Append(s, i, k - i);
                    int e = k; while (e < s.Length && s[e] != '\\' && s[e] != '/' && s[e] != '"' && s[e] != '\r' && s[e] != '\n') e++;
                    sb.Append("<user>"); i = e;
                }
                s = sb.ToString();
                var u = Environment.UserName;
                if (!string.IsNullOrEmpty(u) && u.Length >= 3)
                {
                    var sb2 = new StringBuilder(s.Length); i = 0;
                    while (i < s.Length) { int k = s.IndexOf(u, i, StringComparison.OrdinalIgnoreCase); if (k < 0) { sb2.Append(s, i, s.Length - i); break; } sb2.Append(s, i, k - i).Append("<user>"); i = k + u.Length; }
                    s = sb2.ToString();
                }
            }
            catch { }
            return s;
        }

        static System.Threading.Tasks.Task Send(string kind, string text)
        {
            if (!Active || string.IsNullOrEmpty(text)) return null;
            string ver = "?"; try { ver = VersionLabel.Package(); } catch { }
            string url = Url, id = Id?.Value ?? "anon";
            text = Clean($"Iron Admiral {ver} | DIP_GG_Fixes {Info()} | kind {kind}\n") + Clean(text);
            return System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string gz;
                    using (var ms = new MemoryStream())
                    {
                        using (var z = new GZipStream(ms, CompressionLevel.Optimal, true)) { var b = Encoding.UTF8.GetBytes(text); z.Write(b, 0, b.Length); }
                        gz = Convert.ToBase64String(ms.ToArray());
                    }
                    string json = "{\"token\":\"" + Token + "\",\"id\":\"" + id + "\",\"kind\":\"" + kind + "\",\"ver\":\"" + ver.Replace("\"", "") + "\",\"gz\":\"" + gz + "\"}";
                    using (var hc = new System.Net.Http.HttpClient())
                    {
                        hc.Timeout = TimeSpan.FromSeconds(40);
                        var resp = await hc.PostAsync(url, new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"));
                        string body = ""; try { body = await resp.Content.ReadAsStringAsync(); } catch { }
                        Log($"{kind} log sent ({text.Length / 1024} KB): {(int)resp.StatusCode} {(body.Length > 60 ? body.Substring(0, 60) : body)}");
                    }
                }
                catch (Exception e) { if (failures++ < 5) Log($"{kind} log not sent: {e.Message}"); }
            });
        }
        static string Info() { try { return typeof(LogShare).Assembly.GetName().Version.ToString(); } catch { return "?"; } }

        // ---------------------------------------------------------------- triggers
        internal static void Tick()
        {
            if (!Active) return;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle && !inBattle) { inBattle = true; battleStart = Math.Max(0, Length() - 20000); } // a little before: the battle decision
                else if (!battle && inBattle)
                {
                    inBattle = false;
                    if (battleStart >= 0 && battles < 30) { battles++; long end; var t = Read(battleStart, 3_000_000, out end); Send("battle", t); }
                    battleStart = -1;
                }
                float now = Time.realtimeSinceStartup;
                if (now >= nextScan)
                {
                    nextScan = now + 20f;
                    long end; var t = Read(scanPos, 400_000, out end);
                    if (scanPos == 0) { scanPos = end; return; } // first pass: start from here
                    scanPos = end;
                    if (t != null && t.IndexOf("Exception", StringComparison.Ordinal) >= 0 && errorsSent < 3 && now - lastError > 600f)
                    {
                        errorsSent++; lastError = now;
                        long e2; var tail = Read(Math.Max(0, end - 120_000), 120_000, out e2);
                        Send("error", tail);
                    }
                }
            }
            catch (Exception e) { if (failures++ < 5) Log("tick: " + e.Message); }
        }

        internal static void OnQuit()
        {
            if (!Active || quitSent) return; quitSent = true;
            try { long end; var t = Read(0, 6_000_000, out end); var task = Send("session", t); task?.Wait(TimeSpan.FromSeconds(8)); } catch { }
        }

        // ---------------------------------------------------------------- consent window (main menu, once)
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>(), next = new List<KeyValuePair<Rect, Action>>();
        static GUIStyle sTitle, sText, sBtn; static int werr;
        static bool Show()
        {
            if (Asked == null || Asked.Value || string.IsNullOrEmpty(Url) || OtherMods.WindowOpen) return false;
            try { return !GameManager.IsCampaign && !GameManager.IsBattle && !GameManager.IsConstructor && !GameManager.IsLoadingAny; } catch { return false; }
        }
        static void Answer(bool yes)
        {
            On.Value = yes; Asked.Value = true;
            if (yes && UpdateCheck.BetaSwitch != null) UpdateCheck.BetaSwitch.Value = true;
            try { MelonPreferences.Save(); } catch { }
            Log(yes ? "log sharing switched on by the player (Beta updates available)" : "log sharing declined by the player");
        }
        internal static void TickWindow()
        {
            try { if (Show() && Input.GetMouseButtonDown(0)) { var pt = UiScale.Mouse(); foreach (var kv in clicks) if (kv.Key.Contains(pt)) { kv.Value(); break; } } } catch { }
        }
        internal static void OnGUI()
        {
            if (!Show()) { if (clicks.Count > 0) clicks.Clear(); return; }
            try
            {
                if (sTitle == null)
                {
                    sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
                    sText = new GUIStyle(); sText.fontSize = 13; sText.richText = true; sText.wordWrap = true; sText.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
                    sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.richText = true; sBtn.wordWrap = true; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.normal.textColor = Color.white;
                }
                next.Clear();
                float w = Math.Min(640f, UiScale.W - 40f), h = 300f;
                var r = new Rect((UiScale.W - w) / 2f, (UiScale.H - h) / 2f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 28f, 24f), "<b>Become an Iron Admiral tester?</b>", sTitle);
                GUI.Label(new Rect(r.x + 14f, r.y + 40f, w - 28f, 180f),
                    "Logs from real campaigns are the best help in fixing errors and balancing the battle AI. If you agree, the mod sends parts of the game log to the author:\n" +
                    "• the log of each battle, • the last lines after an error of the mod, • the session log when the game is closed.\n" +
                    "Only the game log is sent (no saves, no other files); your Windows user name is removed and the logs are deleted after 90 days.\n\n" +
                    "<b>Testers get the beta versions:</b> the Beta updates switch (F10) works while log sharing is on. You can change your mind at any time in F10.", sText);
                float bw = (w - 42f) / 2f;
                Btn(new Rect(r.x + 14f, r.yMax - 60f, bw, 44f), "<b>Yes - share logs and get betas</b>", () => Answer(true));
                Btn(new Rect(r.x + 28f + bw, r.yMax - 60f, bw, 44f), "No, thanks", () => Answer(false));
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (werr++ < 5) Log("window: " + e.Message); }
        }
        static void Btn(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }
    }
}
