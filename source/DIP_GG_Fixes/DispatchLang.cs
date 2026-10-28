// 1.11.283 (author 25.10, 19:38 and 20:43): the battle dispatches in the language of the navy that speaks them. Every AI navy
// speaks its own language; the player's own ships always speak English, unless the switch DispatchPlayerOriginalLanguage (F10,
// Battle AI) is on - then the player's navy speaks its own language too. The texts are data files: English is built into the
// mod (LangEn.cs, the master copy of Mods\IronAdmiralLang\en.txt); the other languages are Mods\IronAdmiralLang\<code>.txt;
// a file in UserData\IronAdmiral\Lang\<code>.txt replaces the entries it contains. A line missing in a language falls back to
// English. Switch for the whole feature: DispatchNationalLanguages.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class DLang
    {
        internal static MelonPreferences_Entry<bool> On, PlayerOriginal;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("DispatchNationalLanguages", true, null, "Battle dispatches of the AI navies are written in their own language (German, French, Russian, Japanese ...); the texts are in Mods\\IronAdmiralLang");
            PlayerOriginal = cat.CreateEntry("DispatchPlayerOriginalLanguage", false, null, "Your own ships' dispatches are also written in your navy's language (off: always English)");
        }

        static Dictionary<string, Dictionary<string, List<string>>> tabs;
        static readonly System.Random rng = new System.Random();
        static int loadErr;

        static void Parse(string lang, string text, bool replace)
        {
            Dictionary<string, List<string>> t;
            if (!tabs.TryGetValue(lang, out t)) { t = new Dictionary<string, List<string>>(); tabs[lang] = t; }
            var cleared = new HashSet<string>();
            foreach (var raw in text.Split('\n'))
            {
                var l = raw.TrimEnd('\r');
                var lt = l.TrimStart(); if (lt.Length == 0 || lt[0] == '#') continue;
                int i = l.IndexOf('='); if (i <= 0) continue;
                string id = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                if (id.Length == 0 || v.Length == 0) continue;
                List<string> list;
                if (!t.TryGetValue(id, out list)) { list = new List<string>(); t[id] = list; }
                else if (replace && cleared.Add(id)) list.Clear();
                if (replace) cleared.Add(id);
                list.Add(v);
            }
        }
        static void Load()
        {
            tabs = new Dictionary<string, Dictionary<string, List<string>>>();
            try { Parse("en", LangEn.Text, false); } catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Dispatch] language en: " + e.Message); }
            var sb = new System.Text.StringBuilder("[DIP GG Fixes][Dispatch] languages: en (built in)");
            foreach (var dir in new[] { Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiralLang"), Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "Lang") })
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.txt"))
                    {
                        string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        try { Parse(code, File.ReadAllText(f, System.Text.Encoding.UTF8), true); sb.Append($", {code} ({(dir.Contains("UserData") ? "user" : "mod")} {tabs[code].Count} ids)"); }
                        catch (Exception e) { if (loadErr++ < 5) MelonLogger.Warning($"[DIP GG Fixes][Dispatch] language file {f}: {e.Message}"); }
                    }
                }
                catch { }
            }
            MelonLogger.Msg(sb.ToString());
        }
        internal static void Reset() { tabs = null; } // re-read at the next battle (texts can be edited between battles)

        internal static string OfNation(string nation)
        {
            switch (nation)
            {
                case "britain": case "usa": return "en";
                case "germany": case "austria": return "de";
                case "france": return "fr";
                case "italy": return "it";
                case "russia": return "ru";
                case "japan": return "ja";
                case "spain": return "es";
                case "china": return "zh";
                case "portugal": return "pt";
                case "netherlands": return "nl";
                case "scandinavia": return "sv";
                case "byzantine": return "el";
                default: return "en";
            }
        }
        static string NationOf(Ship s) { try { return s != null && s.player != null && s.player.data != null ? s.player.data.name : null; } catch { return null; } }
        static bool IsMain(Ship s) { try { return s != null && s.player != null && s.player.isMain; } catch { return false; } }
        static string mainNation;
        internal static string MainNation()
        {
            if (mainNation != null) return mainNation;
            try
            {
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; var ships = cb != null ? cb.Ships : null;
                if (ships != null) for (int i = 0; i < ships.Count; i++) if (IsMain(ships[i])) { mainNation = NationOf(ships[i]); break; }
            }
            catch { }
            return mainNation;
        }
        internal static void BattleStart() { mainNation = null; Reset(); }

        // the language a ship's people speak
        internal static string For(Ship s)
        {
            if (s == null) return Player();
            string n = NationOf(s);
            if (IsMain(s) || (n != null && n == MainNation())) return Player();
            return On != null && On.Value ? OfNation(n) : "en";
        }
        // the language of a navy given by its game name ("germany")
        internal static string ForNation(string nation)
        {
            if (nation != null && nation == MainNation()) return Player();
            return On != null && On.Value ? OfNation(nation) : "en";
        }
        // the narrator and the player's own ships
        internal static string Player()
        {
            if (PlayerOriginal == null || !PlayerOriginal.Value || On == null || !On.Value) return "en";
            return OfNation(MainNation());
        }

        static List<string> Variants(string lang, string id)
        {
            if (tabs == null) Load();
            Dictionary<string, List<string>> t; List<string> v;
            if (lang != null && tabs.TryGetValue(lang, out t) && t.TryGetValue(id, out v) && v.Count > 0) return v;
            if (tabs.TryGetValue("en", out t) && t.TryGetValue(id, out v) && v.Count > 0) return v;
            return null;
        }
        internal static bool Has(string lang, string id) { return Variants(lang, id) != null; }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }

        // a random variant of the line with its {placeholders} filled: kv = name, value, name, value ...
        // {name} gives the value, {Name} the value with a capital first letter
        internal static string T(string lang, string id, params string[] kv)
        {
            var v = Variants(lang, id); if (v == null) return null;
            string s = v[rng.Next(v.Count)];
            return Fill(s, kv);
        }
        // a single word or name (no variants expected): the first variant, or the fallback when the id is missing
        internal static string W(string lang, string id, string fallback)
        {
            var v = Variants(lang, id); return v != null ? Fill(v[0], null) : fallback;
        }
        static string Fill(string s, string[] kv)
        {
            if (kv == null) return s;
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                string k = kv[i], val = kv[i + 1] ?? "";
                if (k.Length == 0) continue;
                s = s.Replace("{" + k + "}", val);
                string K = char.ToUpper(k[0]) + k.Substring(1);
                if (K != k) s = s.Replace("{" + K + "}", Cap(val));
            }
            return s;
        }
        // the language's name for a ship type ("light cruiser") and a size class ("medium"), and a navy's adjective / name
        internal static string Type(string lang, string english) { return string.IsNullOrEmpty(english) ? english : W(lang, "type." + english.Replace(' ', '_'), english); }
        internal static string Cls(string lang, string english) { return string.IsNullOrEmpty(english) ? english : W(lang, "cls." + english, english); }
        internal static string Nat(string lang, string nation, string english) { return W(lang, "nat." + nation, english); }
    }
}
