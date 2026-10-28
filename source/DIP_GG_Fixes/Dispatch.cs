
// 1.11.73: battle dispatches. During a fought battle the key decisions of the add-on's battle AI and the key moments of
// the fight (sightings, identification, charges, massed attacks, ramming, withdrawals, scatter, torpedo tracks, panic,
// surrenders, sinkings, prizes) are also written as short dramatic reports from the people involved - lookouts,
// captains, gunnery and torpedo officers, flagships. They go to MelonLoader\Latest.log as "[Dispatch]" lines and to one
// text file per battle in UserData\IronAdmiral\Dispatches. The technical "[Battle]" lines stay (BattleAiLog switch) - they
// are what problem reports need. Switch: BattleDispatches.
// 1.11.85: a dispatch about a ship is also shown for a few seconds above that ship in battle (own ships and identified enemies
// only - fog of war). Switch: BattleDispatchBubbles.
// 1.11.283: the texts come from the language files (DispatchLang.cs, Mods\IronAdmiralLang) - each navy speaks its own language,
// the player's ships English unless DispatchPlayerOriginalLanguage is on.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using Il2Cpp;
using UnityEngine;

namespace DipGgFixes
{
    internal static class Dispatch
    {
        internal static MelonPreferences_Entry<bool> On;
        static bool inBattle; static float start; static string file; static int sightings;
        static readonly HashSet<string> once = new HashSet<string>();
        static System.Random rng = new System.Random();

        internal static MelonPreferences_Entry<bool> Bubbles;
        internal static MelonPreferences_Entry<float> BubbleScale; // 1.11.102
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            Bubbles = cat.CreateEntry("BattleDispatchBubbles", true, null, "During battle a dispatch about a ship is also shown for a few seconds above that ship (only ships you can identify: your own and enemy ships already identified)");
            BubbleScale = cat.CreateEntry("BattleDispatchBubbleScale", 0.55f, null, "Size of the dispatch bubbles above the ships relative to the mod's panels (1 = panel size)");
            On = cat.CreateEntry("BattleDispatches", true, null, "During fought battles the log also tells the story as dramatic dispatches from the officers and lookouts involved (Latest.log '[Dispatch]' lines and one file per battle in UserData\\IronAdmiral\\Dispatches)");
            DLang.CreatePrefs(cat); // 1.11.283
        }
        static bool Active { get { return On != null && On.Value && inBattle; } }

        // ---- 1.11.209: enemy torpedo launches held until the player's ships sight the torpedoes ----
        class Held { public string Ship, Target; public Vector3 Pos; public float T; }
        static readonly List<Held> held = new List<Held>();
        static float nextHeld;
        static Ship FindShip(string shortName)
        {
            try { var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; var ships = cb != null ? cb.Ships : null; if (ships == null) return null; for (int i = 0; i < ships.Count; i++) { var x = ships[i]; if (x != null && Ship(x.name) == shortName) return x; } } catch { }
            return null;
        }
        static bool IsPlayers(Ship s) { try { return s.player != null && s.player.isMain; } catch { return false; } }
        // the language of the people aboard the ship with this short name
        static string L(string shortName) { return DLang.For(FindShip(shortName)); }
        static void HoldLaunch(string s, string t, Ship src)
        {
            var p = src.transform.position; p.y = 0f;
            held.Add(new Held { Ship = s, Target = t, Pos = p, T = Time.time });
            if (held.Count > 12) held.RemoveAt(0);
        }
        static void HeldTick()
        {
            if (held.Count == 0 || Time.time < nextHeld) return; nextHeld = Time.time + 0.5f;
            try
            {
                held.RemoveAll(h => Time.time - h.T > 240f);
                var all = Torpedo.AllTorpedo; if (all == null || all.Count == 0) return;
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null) return;
                Player me = null; var ships = cb.Ships; if (ships == null) return;
                for (int i = 0; i < ships.Count; i++) if (ships[i] != null && IsPlayers(ships[i])) { me = ships[i].player; break; }
                if (me == null) return;
                var mine = cb.ShipsOf(me, true); if (mine == null) return;
                for (int k = held.Count - 1; k >= 0; k--)
                {
                    var h = held[k]; bool seen = false;
                    for (int i = 0; i < all.Count && !seen; i++)
                    {
                        var tp = all[i]; if (tp == null) continue;
                        var st = tp.torpedoStart; st.y = 0f; if ((st - h.Pos).magnitude > 1500f) continue;
                        for (int j = 0; j < mine.Count; j++) { var m = mine[j]; if (m == null || m.isDead || m.isSinking) continue; bool v = false; try { v = tp.IsVisibleFor(m); } catch { } if (v) { seen = true; break; } }
                    }
                    if (!seen) continue;
                    held.RemoveAt(k);
                    int secs = Mathf.RoundToInt((Time.time - h.T) * (Ballistics.WS));
                    SayT("reload|" + h.Ship, L(h.Ship), "reload.seen", "s", h.Ship, "t", h.Target, "secs", secs.ToString());
                }
            }
            catch { }
        }

        internal static void Tick()
        {
            try { if (inBattle) HeldTick(); } catch { }
            bool b = false; try { b = GameManager.IsBattle; } catch { }
            if (b && !inBattle)
            {
                inBattle = true; start = Time.time; once.Clear(); sightings = 0; file = null; bubbles.Clear(); held.Clear();
                rng = new System.Random(Environment.TickCount);
                try { DLang.BattleStart(); } catch { }
                if (On != null && On.Value) { var t = DLang.T(DLang.Player(), "battle.begin"); if (t != null) Write(t); }
            }
            else if (!b && inBattle)
            {
                if (On != null && On.Value && file != null) { var t = DLang.T(DLang.Player(), "battle.end"); if (t != null) Write(t); }
                inBattle = false;
            }
        }

        // ------------------------------------------------------------------ output
        static string Clock() { float m = Mathf.Max(0f, (Time.time - start) / 60f); return $"{(int)m:00}:{(int)((m - (int)m) * 60f):00}"; }
        static void Write(string text)
        {
            string line = Clock() + "  " + text;
            MelonLogger.Msg("[DIP GG Fixes][Dispatch] " + line);
            try
            {
                if (file == null)
                {
                    var dir = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "Dispatches"); Directory.CreateDirectory(dir);
                    string when = ""; try { var d = CampaignController.Instance.CurrentDate.AsDate(); when = d.ToString("yyyy-MM") + "_"; } catch { }
                    file = Path.Combine(dir, when + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_battle.txt");
                }
                File.AppendAllText(file, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch { }
        }
        static string Pick(params string[] v) { return v[rng.Next(v.Length)]; }
        internal static void SayHit(string key, string text) { Say(key, text); } // 1.11.258 (HitDispatch)
        // 1.11.283: a line from the language files (lang, id, placeholder name/value pairs)
        static void SayT(string key, string lang, string id, params string[] kv)
        {
            if (!Active) return;
            if (key != null && once.Contains(key)) return;
            var text = DLang.T(lang, id, kv); if (text == null) return;
            Say(key, text);
        }
        static void Say(string key, params string[] variants)
        {
            if (!Active) return; if (key != null && !once.Add(key)) return;
            string text = Pick(variants); Write(text);
            // 1.11.89: bubbles timed in real seconds (9 s), not game time - at x4 speed they vanished after 2 s
            // 1.11.85: the subject ship is the second part of the key ("charge|<ship>|..."); its dispatch is also shown above it
            if (key != null) { var k = key.Split('|'); if (k.Length >= 2 && k[1].Length > 0) AddBubble(k[1], text); }
        }

        // ------------------------------------------------------------------ 1.11.85: dispatches above the ships in battle
        class Bubble { public string Ship, Text; public float T; public bool Logged; }
        static readonly List<Bubble> bubbles = new List<Bubble>();
        static GUIStyle sB; static int bErr, bLog;
        static void AddBubble(string ship, string text)
        {
            if (Bubbles == null || !Bubbles.Value) return;
            if (text.Length > 170) text = text.Substring(0, 167) + "...";
            bubbles.RemoveAll(b => b.Ship == ship); bubbles.Add(new Bubble { Ship = ship, Text = text, T = Time.unscaledTime });
            if (bubbles.Count > 6) bubbles.RemoveAt(0);
        }
        internal static void OnGUI()
        {
            if (Bubbles == null || !Bubbles.Value || !inBattle || bubbles.Count == 0 || BattleScreen.Hidden) return;
            try
            {
                bubbles.RemoveAll(b => Time.unscaledTime - b.T > 9f);
                if (bubbles.Count == 0) return;
                var cam = Camera.main; if (cam == null) return;
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null) return;
                var ships = cb.Ships; if (ships == null) return;
                if (sB == null) { sB = new GUIStyle(); sB.fontSize = 14; sB.wordWrap = true; sB.richText = true; sB.alignment = TextAnchor.LowerCenter; sB.normal.textColor = new Color(1f, 0.95f, 0.8f); }
                // 1.11.102: bubbles have their own, smaller scale (22.10: at the panel scale they were far too large in battle)
                float k = UiScale.F * Mathf.Clamp(BubbleScale != null ? BubbleScale.Value : 0.55f, 0.2f, 2f); if (k < 0.5f) k = 0.5f;
                float BW = Screen.width / k, BH = Screen.height / k;
                GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
                foreach (var b in bubbles)
                {
                    Ship ship = null;
                    for (int i = 0; i < ships.Count; i++) { var x = ships[i]; if (x != null && Ship(x.name) == b.Ship) { ship = x; break; } }
                    if (ship == null) continue;
                    try { if (Fog.PlayerLabel(ship) != null) continue; } catch { }   // fog of war: nothing above an unidentified enemy
                    Vector3 wp; try { wp = ship.transform.position; } catch { continue; }
                    var sp = cam.WorldToScreenPoint(wp + Vector3.up * 40f); if (sp.z <= 0f) continue;
                    float w = Mathf.Min(360f, BW * 0.28f), h = sB.fontSize * 4.6f;
                    var r = new Rect(sp.x / k - w / 2f, BH - sp.y / k - h - 8f, w, h);
                    if (r.yMax < 0 || r.y > BH || r.xMax < 0 || r.x > BW) continue;
                    float age = Time.unscaledTime - b.T; float a = age > 7f ? Mathf.Clamp01(1f - (age - 7f) / 2f) : 1f;
                    var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.Box(r, ""); GUI.Box(r, "");
                    GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, r.height - 8f), b.Text, sB);
                    try { AutoTest.BubbleDrawn(b.Ship); } catch { }
                    if (!b.Logged && bLog < 12) { b.Logged = true; bLog++; MelonLogger.Msg($"[DIP GG Fixes][Dispatch] bubble shown above {b.Ship} at screen {r.x:0},{r.y:0}: {b.Text}"); }
                    GUI.color = old;
                }
            }
            catch (Exception e) { if (bErr++ < 3) MelonLogger.Warning("[DIP GG Fixes][Dispatch] bubbles: " + e.Message); }
            finally { GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale.F, UiScale.F, 1f)); }
        }

        // "BB König Albert (Hildebrand (Mar. 1918)) [germany]" -> "König Albert"
        internal static string Ship(string n)
        {
            if (string.IsNullOrEmpty(n)) return "the ship";
            int i = n.IndexOf(" ("); if (i > 0) n = n.Substring(0, i);
            i = n.IndexOf(" ["); if (i > 0) n = n.Substring(0, i);
            n = n.Trim();
            int s = n.IndexOf(' ');
            if (s > 0 && s <= 3) { var p = n.Substring(0, s); bool up = true; foreach (var c in p) if (!char.IsUpper(c)) up = false; if (up) n = n.Substring(s + 1); }
            return n;
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }
        static string Nation(string n)
        {
            switch (n) { case "britain": return "British"; case "germany": return "German"; case "france": return "French"; case "usa": return "American"; case "japan": return "Japanese";
                case "russia": return "Russian"; case "italy": return "Italian"; case "austria": return "Austro-Hungarian"; case "spain": return "Spanish"; case "china": return "Chinese";
                case "scandinavia": return "Scandinavian"; case "byzantine": return "Byzantine"; case "portugal": return "Portuguese"; case "netherlands": return "Dutch"; default: return Cap(n); }
        }
        static string Before(string m, string sep) { int i = m.IndexOf(sep, StringComparison.Ordinal); return i > 0 ? m.Substring(0, i) : null; }
        static string After(string m, string sep) { int i = m.IndexOf(sep, StringComparison.Ordinal); return i >= 0 ? m.Substring(i + sep.Length) : null; }
        static string Subj(string m, string sep) { var x = Before(m, sep); if (x == null) return null; int k = x.LastIndexOf(" ("); return k > 0 ? x.Substring(0, k) : x; }
        static float Num(string s) { float v; float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v); return v; }

        // ------------------------------------------------------------------ from the battle AI log (BattleAi.Log)
        internal static void FromLog(string m)
        {
            if (!Active || string.IsNullOrEmpty(m)) return;
            try
            {
                if (m.StartsWith("[player scout] ")) m = m.Substring(15);
                if (m.StartsWith("massed torpedo attack ON for "))
                {
                    var nat = Before(After(m, " for ") + ":", ":"); string lg = DLang.ForNation(nat);
                    SayT("mass|" + nat, lg, "mass", "nat", DLang.Nat(lg, nat, Nation(nat)));
                    return;
                }
                if (m.StartsWith("massed torpedo attack off for ")) return;
                if (m.Contains(") charges ") && m.Contains("torpedo range"))
                {
                    var s = Ship(Subj(m, ") charges ")); var rest = After(m, ") charges "); var t = Ship(Before(rest, " ("));
                    float d = Num(Before(After(rest, ") at ") ?? "", " m"));
                    string km = d > 0 ? $"{d / 1000f:0.0}" : "?";
                    SayT("charge|" + s, L(s), "charge", "s", s, "t", t, "km", km);
                    return;
                }
                if (m.Contains("massed torpedo attack - charges "))
                {
                    var s = Ship(Subj(m, "): massed torpedo attack")); var t = Ship(Before(After(m, "charges ") ?? "", " from"));
                    SayT("charge|" + s, L(s), "charge.join", "s", s, "t", t);
                    return;
                }
                if (m.Contains(") rams "))
                {
                    var s = Ship(Subj(m, ") rams ")); var t = Ship(Before(After(m, ") rams ") ?? "", " ("));
                    SayT("ram|" + s, L(s), "ram", "s", s, "t", t);
                    return;
                }
                if (m.Contains("escape mission - cannot outrun"))
                {
                    var s = Ship(Subj(m, "): escape")); var p = Ship(Before(After(m, "cannot outrun ") ?? "", " ("));
                    SayT("scatter|" + s, L(s), "scatter", "s", s, "p", p);
                    return;
                }
                if (m.Contains("escape mission - faster than") || m.Contains("escape mission - no pursuer"))
                {
                    var s = Ship(Subj(m, "): escape"));
                    SayT("escape|" + s, L(s), "escape", "s", s);
                    return;
                }
                if (m.Contains("retreat cancelled, fighting"))
                {
                    var s = Ship(Subj(m, " cannot outrun ")); var p = Ship(Before(After(m, "cannot outrun ") ?? "", " ("));
                    SayT("fight|" + s, L(s), "fight", "s", s, "p", p);
                    return;
                }
                if (m.Contains("): enemy force too strong"))
                {
                    var s = Ship(Subj(m, "): enemy force"));
                    SayT("withdraw|" + s, L(s), "withdraw.strong", "s", s);
                    return;
                }
                if (m.Contains("): cannot hurt ") && m.Contains("can survive"))
                {
                    var s = Ship(Subj(m, "): cannot hurt")); var t = Ship(Before(After(m, "cannot hurt ") ?? "", " and can"));
                    SayT("withdraw|" + s, L(s), "withdraw.nohurt", "s", s, "t", t);
                    return;
                }
                if (m.Contains(") closes in on "))
                {
                    var s = Ship(Subj(m, ") closes in on ")); var t = Ship(Before(After(m, ") closes in on ") ?? "", " ("));
                    bool outr = m.Contains("outranged"), nopen = m.Contains("cannot penetrate");
                    SayT("close|" + s, L(s), outr && !nopen ? "close.outranged" : nopen && !outr ? "close.nopen" : "close.both", "s", s, "t", t);
                    return;
                }
                if (m.Contains("torpedo track(s) sighted"))
                {
                    var s = Ship(Subj(m, "): ")); int n = (int)Num(Before(After(m, "): ") ?? "", " enemy torpedo"));
                    string lg = L(s);
                    string cnt = n > 1 ? DLang.T(lg, "torp.count.many", "n", n.ToString()) ?? n.ToString() : DLang.T(lg, "torp.count.one") ?? "1";
                    SayT("torp|" + s + "|" + Clock().Substring(0, 2), lg, "torp", "s", s, "cnt", cnt);
                    return;
                }
                if (m.Contains(": evasive zigzag"))
                {
                    var s = Ship(Before(m, ": evasive"));
                    SayT("zig|" + s, L(s), "zig", "s", s);
                    return;
                }
                // 1.11.89: dispatches for the doctrine rules of 1.11.86-1.11.88 (also shown above the ship)
                if (m.Contains("): all torpedoes away"))
                {
                    var s = Ship(Before(m, ": all torpedoes away")); var t = Ship(Before(After(m, " m from ") ?? "", " - turns"));
                    // 1.11.209 (author, 24.10): an enemy launch is not announced to the player when it happens - the player cannot know it.
                    //           It is held until one of the player's ships sights torpedoes from that position, and is then told from the
                    //           enemy side: "we fired N seconds ago - they have seen the tracks".
                    var src = FindShip(s);
                    if (src != null && !IsPlayers(src)) { HoldLaunch(s, t, src); return; }
                    SayT("reload|" + s, L(s), "reload", "s", s, "t", t);
                    return;
                }
                if (m.Contains("): returns to the attack on "))
                {
                    var s = Ship(Before(m, ": returns to the attack")); var t = Ship(Before(After(m, "attack on ") ?? "", " at "));
                    SayT("return|" + s, L(s), "return", "s", s, "t", t);
                    return;
                }
                if (m.Contains("): torpedoes expended, no reloads"))
                {
                    var s = Ship(Before(m, ": torpedoes expended")); var t = Ship(Before(After(m, "light forces, ") ?? "", " ("));
                    SayT("guns|" + s, L(s), "guns", "s", s, "t", t);
                    return;
                }
                if (m.Contains("): tubes not reloaded after"))
                {
                    var s = Ship(Before(m, ": tubes not reloaded"));
                    SayT("noreload|" + s, L(s), "noreload", "s", s);
                    return;
                }
                if (m.Contains("battle line moves up to effective range") || m.Contains("own flotillas attacking - supports them"))
                {
                    var s = Ship(Before(m, ": ")); var t = Ship(Before(After(m, " and engages ") ?? "", " ("));
                    SayT("lineup|" + s, L(s), m.Contains("supports them") ? "lineup.support" : "lineup.range", "s", s, "t", t);
                    return;
                }
                if (m.Contains(": the battle lines are engaged"))
                {
                    var nat = Before(m, ": the battle lines"); string lg = DLang.ForNation(nat);
                    SayT("engaged|" + nat, lg, "engaged", "nat", DLang.Nat(lg, nat, Nation(nat)));
                    return;
                }
                if (m.Contains("): holding torpedoes until"))
                {
                    var s = Ship(Before(m, ": holding torpedoes"));
                    SayT("hold|" + s, L(s), "hold", "s", s);
                    return;
                }
                if (m.Contains(" - moves to break up the attack"))
                {
                    var s = Ship(Before(m, ": enemy ")); var t = Ship(Before(After(m, ": enemy ") ?? "", " ("));
                    SayT("defend|" + s, L(s), "defend", "s", s, "t", t);
                    return;
                }
                if (m.Contains("threatens the enemy line from the opposite flank"))
                {
                    var s = Ship(Before(m, ": own flotillas"));
                    SayT("pin|" + s, L(s), "pin", "s", s);
                    return;
                }
                if (m.Contains("): supports the flotilla attack"))
                {
                    var s = Ship(Before(m, ": supports"));
                    SayT("support|" + s, L(s), "support", "s", s);
                    return;
                }
                if (m.Contains("): flying squadron"))
                {
                    var s = Ship(Before(m, ": flying squadron")); var t = Ship(Before(After(m, " at ") ?? "", " ("));
                    SayT("flying|" + s, L(s), "flying", "s", s, "t", t);
                    return;
                }
                if (m.Contains("opens the range to avoid its concentrated fire"))
                {
                    var s = Ship(Before(m, ": "));
                    SayT("open|" + s, L(s), "open", "s", s);
                    return;
                }
                if (m.Contains("withdraws while the lighter ships cover it"))
                {
                    var s = Ship(Before(m, ": the attack has failed"));
                    SayT("coverwd|" + s, L(s), "coverwd", "s", s);
                    return;
                }
                if (m.Contains("): covers the withdrawal of "))
                {
                    var s = Ship(Before(m, ": covers")); var mn = Ship(Before(After(m, "withdrawal of ") ?? "", " - "));
                    SayT("cover|" + s, L(s), "cover", "s", s, "mn", mn);
                    return;
                }
                if (m.Contains("): night attack spotted"))
                {
                    var s = Ship(Before(m, ": night attack spotted"));
                    SayT("nightab|" + s, L(s), "nightab", "s", s);
                    return;
                }
                if (m.Contains(" lay a smoke screen"))
                {
                    var s = Ship(Before(m, ": "));
                    SayT("smoke|" + s, L(s), "smoke", "s", s);
                    return;
                }
                if (m.Contains(": charge aborted"))
                {
                    var s = Ship(Before(m, ": charge aborted"));
                    SayT("abort|" + s, L(s), m.Contains("no torpedoes") ? "abort.notorp" : "abort.hit", "s", s);
                    return;
                }
                if (m.StartsWith("PANIC: "))
                {
                    var s = Ship(Before(m.Substring(7), " ("));
                    SayT("panic|" + s, L(s), "panic", "s", s);
                    return;
                }
                if (m.Contains(": friendly ship in the torpedo lane"))
                {
                    var s = Ship(Before(m, ": friendly"));
                    SayT("lane|" + s, L(s), "lane", "s", s);
                    return;
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------ events from the game and the fog of war
        internal static void Sighted(string sizeClass, float meters, bool playerSide)
        {
            if (!Active || !playerSide || sightings >= 4) return; sightings++;
            string lg = DLang.Player();
            SayT(null, lg, "sighted", "km", $"{meters / 1000f:0}", "cls", DLang.Cls(lg, sizeClass));
        }
        internal static void Identified(string name, string actualType, string reportedType, float meters, bool playerSide)
        {
            if (!Active) return;
            string s = Ship(name), km = $"{meters / 1000f:0.0}";
            bool wrong = reportedType != null && reportedType != actualType;
            string lg = DLang.Player();
            string type = DLang.Type(lg, actualType), rep = DLang.Type(lg, reportedType);
            if (playerSide)
                SayT("id|" + s, lg, wrong ? "id.wrong" : "id.ok", "s", s, "type", type, "rep", rep, "km", km);
            else if (once.Count < 200)
                SayT("eid|" + s, lg, wrong ? "eid.wrong" : "eid.ok", "s", s, "rep", rep);
        }
        internal static void Sunk(string name)
        {
            var s = Ship(name);
            SayT("sunk|" + s, DLang.Player(), "sunk", "s", s);
        }
        internal static void Surrendered(string name)
        {
            var s = Ship(name);
            SayT("surr|" + s, DLang.Player(), "surr", "s", s);
        }
        // 1.11.251: a change of the torpedo setting (one dispatch per division and minute, from TorpedoSettings)
        static int tsetN;
        internal static void TorpedoSetting(string name, int set, float range, float kn, int others, bool ordered)
        {
            if (!Active) return;
            string s = Ship(name), km = $"{range / 1000f:0.0}", sp = $"{kn:0}";
            string lg = L(s);
            string div = others > 0 ? " - " + (others > 1 ? DLang.T(lg, "tset.div.many", "n", others.ToString()) : DLang.T(lg, "tset.div.one", "n", "1")) : "";
            string why = DLang.T(lg, ordered ? "tset.why.order" : (set == TorpedoSettings.HS ? "tset.why.close" : set == TorpedoSettings.ER ? "tset.why.far" : "tset.why.short")) ?? "";
            string k = "tset|" + s + "|" + (++tsetN);
            string id = set == TorpedoSettings.HS ? "tset.hs" : set == TorpedoSettings.ER ? "tset.er" : "tset.lr";
            SayT(k, lg, id, "s", s, "km", km, "sp", sp, "why", why, "div", div);
        }
        internal static void Prize(string text)
        {
            if (On == null || !On.Value || file == null) return;
            try
            {
                string body = DLang.T(DLang.Player(), "prize", "text", text) ?? ("After the battle: " + text + ".");
                string l = DateTime.Now.ToString("HH:mm") + "  " + body; MelonLogger.Msg("[DIP GG Fixes][Dispatch] " + l); File.AppendAllText(file, l + Environment.NewLine, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
