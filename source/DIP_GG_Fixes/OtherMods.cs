// 1.11.137 (beta): check of other mods installed next to Iron Admiral. At start the Mods and Plugins folders are listed; every
// DLL that is not part of Iron Admiral (DIP_GG_Fixes, TweaksAndFixes) is written to the log with what is known about it.
// In the main menu a window lists them: each can be moved to a backup folder (Mods_backup_IronAdmiral\<date time>) or kept;
// kept mods are not asked about again until the file changes. A moved mod is no longer loaded after the game is restarted;
// it can be put back from the backup folder at any time. Switch: CheckOtherMods (F10, Information and updates).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class OtherMods
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<string> Kept;
        class Mod { public string Path, File, Folder, Note, Key; public int Level; public bool Moved; public string Result; }
        static readonly List<Mod> found = new List<Mod>();
        static bool scanned, show, logged;
        internal static bool WindowOpen => show; static string backupDir; static int errors;
        static readonly HashSet<string> ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DIP_GG_Fixes.dll", "TweaksAndFixes.dll" };
        // what is known about mods seen with Iron Admiral: level 0 = works alongside, 1 = overlaps (may conflict), 2 = changes battles/cheats
        static readonly Dictionary<string, (int, string)> known = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            { "UADVanillaPlus.dll", (1, "overlaps with Iron Admiral: campaign map wrap, ships in ports, AI designs and refits, battle balance - double effects or conflicts are likely") },
            { "LilyzDissolution.dll", (2, "cheat: destroys enemy ships on a key press") },
            { "LilyzTTS.dll", (2, "cheat: homing torpedoes for one nation") },
            { "LilyzMod.dll", (2, "cheat: homing torpedoes for one nation (Lilyz TTS)") },
            { "TaskForceNames.dll", (0, "names for your task forces - no known conflict") },
            { "UADCarrierPrototype.dll", (1, "overlaps with Iron Admiral: economy, intelligence, fleet formations, auxiliary/logistics campaign, torpedoes - not tested together, double effects are possible") }, // 1.11.361
            // 1.11.469 (author 30.10 20:51): ChonkE's CV Barebone Modification (MIT) - a second air strike system next to Iron Admiral's
            // carrier and shore strikes, and its genarmordata.csv replaces all armour generation rules of TweaksAndFixes
            { "CarrierMod.dll", (1, "CV Barebone Modification by ChonkE: carrier hulls with air wings - a second air strike system next to Iron Admiral's naval aviation (double strikes in one battle), and its genarmordata.csv replaces the armour generation rules; not tested together") },
        };

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("CheckOtherMods", true, null, "At start, list other mods installed next to Iron Admiral and offer to move them to a backup folder");
            Kept = cat.CreateEntry("OtherModsKept", "", null, "Internal: other mods the player chose to keep (name|size)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Mods] " + m); }

        // 1.11.361 (author 29.10: "can we cover it so that our solutions are on top"): once, 30 s after start (when every mod has
        // patched), list the game methods that Iron Admiral and another mod both patch, with the owners and the order of the
        // prefixes - the basis for deciding where our patch must win (Harmony priority) or the other mod's patch must go
        static bool overlapsDone;
        internal static void ReportOverlapsOnce(HarmonyLib.Harmony ours)
        {
            if (overlapsDone || ours == null || Time.realtimeSinceStartup < 30f) return;
            overlapsDone = true;
            try
            {
                int n = 0; var byOwner = new Dictionary<string, int>();
                foreach (var m in HarmonyLib.Harmony.GetAllPatchedMethods().ToList())
                {
                    var info = HarmonyLib.Harmony.GetPatchInfo(m); if (info == null) continue;
                    var owners = info.Owners.ToList();
                    if (owners.Count < 2 || !owners.Contains(ours.Id)) continue;
                    n++;
                    foreach (var ow in owners) if (ow != ours.Id) { byOwner.TryGetValue(ow, out var c); byOwner[ow] = c + 1; }
                    string pre = string.Join(" > ", info.Prefixes.OrderByDescending(p => p.priority).Select(p => p.owner + "(" + p.priority + ")"));
                    if (n <= 60) Log($"shared method {m.DeclaringType?.Name}.{m.Name}: owners {string.Join(", ", owners)}; prefixes {(pre.Length > 0 ? pre : "-")}; postfixes {info.Postfixes.Count}, transpilers {info.Transpilers.Count}");
                }
                Log(n == 0 ? "no game method is patched by Iron Admiral and another mod" : $"{n} game method(s) patched by Iron Admiral and another mod: {string.Join(", ", byOwner.Select(kv => kv.Key + " " + kv.Value))}");
            }
            catch (Exception e) { Log("overlap report failed: " + e.Message); }
        }

        internal static void Scan()
        {
            if (scanned) return; scanned = true;
            try
            {
                string root = MelonUtils.GameDirectory;
                foreach (var folder in new[] { "Mods", "Plugins" })
                {
                    string dir = System.IO.Path.Combine(root, folder); if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        string name = System.IO.Path.GetFileName(f); if (ours.Contains(name)) continue;
                        long size = 0; try { size = new FileInfo(f).Length; } catch { }
                        var m = new Mod { Path = f, File = name, Folder = folder, Key = name + "|" + size };
                        if (known.TryGetValue(name, out var k)) { m.Level = k.Item1; m.Note = k.Item2; }
                        else if (name.StartsWith("DIP_GG_Fixes", StringComparison.OrdinalIgnoreCase)) { m.Level = 2; m.Note = "an old copy of Iron Admiral - MelonLoader may load it as well, so changes work twice; move it out"; }
                        else { m.Level = 1; m.Note = "not tested with Iron Admiral"; }
                        found.Add(m);
                    }
                }
                if (found.Count == 0) { Log("no other mods next to Iron Admiral"); return; }
                Log($"{found.Count} other mod(s) next to Iron Admiral: " + string.Join("; ", found.Select(x => $"{x.Folder}\\{x.File} - {x.Note}")));
                var kept = new HashSet<string>((Kept.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                show = On.Value && found.Any(x => !kept.Contains(x.Key));
            }
            catch (Exception e) { Log("scan failed: " + e.Message); }
        }

        static string Backup()
        {
            if (backupDir != null) return backupDir;
            backupDir = System.IO.Path.Combine(MelonUtils.GameDirectory, "Mods_backup_IronAdmiral", DateTime.Now.ToString("yyyy-MM-dd_HHmm"));
            Directory.CreateDirectory(backupDir); return backupDir;
        }
        static void Move(Mod m)
        {
            try
            {
                string dir = System.IO.Path.Combine(Backup(), m.Folder); Directory.CreateDirectory(dir);
                string to = System.IO.Path.Combine(dir, m.File);
                if (File.Exists(to)) to = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(m.File) + "_" + DateTime.Now.Ticks + ".dll");
                File.Move(m.Path, to);
                m.Moved = true; m.Result = "moved - not loaded after a restart"; Log($"{m.Folder}\\{m.File} moved to {to}");
            }
            catch (Exception e)
            {
                m.Result = "could not be moved while the game runs - close the game and move it by hand"; Log($"{m.File}: move failed: {e.Message}");
            }
        }
        static void Keep(Mod m)
        {
            var l = (Kept.Value ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (!l.Contains(m.Key)) l.Add(m.Key);
            Kept.Value = string.Join(";", l); try { MelonPreferences.Save(); } catch { }
            m.Result = "kept"; Log($"{m.File} kept by the player");
        }
        static void OpenFolder()
        {
            try { var d = backupDir ?? System.IO.Path.Combine(MelonUtils.GameDirectory, "Mods_backup_IronAdmiral"); Directory.CreateDirectory(d); Application.OpenURL("file:///" + d.Replace('\\', '/')); } catch { }
        }

        // ---------------------------------------------------------------- window (main menu only)
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>(), next = new List<KeyValuePair<Rect, Action>>();
        static GUIStyle sTitle, sText, sBtn;
        static bool Menu()
        {
            try { return !GameManager.IsCampaign && !GameManager.IsBattle && !GameManager.IsConstructor && !GameManager.IsLoadingAny; } catch { return false; }
        }
        internal static void Tick()
        {
            try
            {
                if (!show || !Menu()) return;
                if (Input.GetMouseButtonDown(0)) { var pt = UiScale.Mouse(); foreach (var kv in clicks) if (kv.Key.Contains(pt)) { kv.Value(); break; } }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
        internal static void OnGUI()
        {
            if (!show || !Menu()) { if (clicks.Count > 0) clicks.Clear(); return; }
            try
            {
                if (sTitle == null)
                {
                    sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
                    sText = new GUIStyle(); sText.fontSize = 13; sText.richText = true; sText.wordWrap = true; sText.normal.textColor = new Color(0.88f, 0.88f, 0.88f);
                    sBtn = new GUIStyle(); sBtn.fontSize = 13; sBtn.richText = true; sBtn.wordWrap = true; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.normal.textColor = Color.white;
                }
                if (!logged) { logged = true; Log("other mods window shown in the main menu"); }
                next.Clear();
                float w = Math.Min(720f, UiScale.W - 40f), rowH = 54f, h = 150f + rowH * found.Count;
                var r = new Rect((UiScale.W - w) / 2f, Math.Max(20f, (UiScale.H - h) / 2f), w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 28f, 24f), "<b>Other mods installed next to Iron Admiral</b>", sTitle);
                GUI.Label(new Rect(r.x + 14f, r.y + 38f, w - 28f, 44f), "Iron Admiral is tested only with its own files and TweaksAndFixes. Other mods can change the same parts of the game. A moved mod goes to a backup folder and can be put back at any time.", sText);
                float y = r.y + 88f;
                foreach (var m in found)
                {
                    string col = m.Level == 0 ? "#9fdf9f" : m.Level == 1 ? "#ffd27f" : "#ff9f8f";
                    GUI.Label(new Rect(r.x + 14f, y, w - 250f, rowH - 6f), $"<b><color={col}>{m.File}</color></b> ({m.Folder})\n{(m.Result ?? m.Note)}", sText);
                    if (m.Result == null)
                    {
                        var mm = m;
                        Btn(new Rect(r.xMax - 228f, y + 4f, 110f, 36f), "Move to backup", () => Move(mm));
                        Btn(new Rect(r.xMax - 112f, y + 4f, 98f, 36f), "Keep", () => Keep(mm));
                    }
                    y += rowH;
                }
                float bw = (w - 56f) / 3f, by = r.yMax - 52f;
                Btn(new Rect(r.x + 14f, by, bw, 38f), "<b>Move all not marked green</b>", () => { foreach (var m in found) if (m.Result == null && m.Level > 0) Move(m); });
                Btn(new Rect(r.x + 28f + bw, by, bw, 38f), "Open backup folder", OpenFolder);
                Btn(new Rect(r.x + 42f + 2f * bw, by, bw, 38f), "<b>Close</b>", () => { show = false; if (found.Any(x => x.Moved)) Log("moved mods are unloaded after the game is restarted"); });
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 5) Log("window: " + e.Message); }
        }
        static void Btn(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }
    }
}
