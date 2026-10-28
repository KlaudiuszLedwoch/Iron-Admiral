// 1.11.251 (beta): torpedo target priorities for the player's divisions. The player clicks ship classes in the order of importance
// (for example BB, then BC, then CA); the torpedo tubes of the division aim at the nearest enemy of the most important class within
// their range, and a division under the "Torpedo attack" order charges the nearest enemy of the most important class present.
// A listed class the enemy lacks falls through to the next class down (1.11.255); when nothing is in reach the game's choice stands. Classes follow what the player knows: an
// unidentified ship counts as the class in the contact report (fog of war), which can be wrong. A target the player gives a ship
// himself is never overridden. Panel: strip below the torpedo settings (division; Shift = this ship only). AI ships keep their
// own rules. Switch: TorpedoTargetPriority (F10, Battle AI).
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class TorpedoPriority
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<string> TestList;   // author's tool: a list for every player division (autotest), e.g. "bb,ca"
        static string testRaw; static List<string> testList;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            TestList = cat.CreateEntry("TorpedoTargetPriorityTest", "", null, "Author's tool: torpedo target priorities for every player division when none is set (classes in order, e.g. bb,ca; empty = off)");
            On = cat.CreateEntry("TorpedoTargetPriority", true, null, "Battle: torpedo target priorities for the player's divisions - ship classes clicked in order of importance; the tubes aim at, and a division under the Torpedo attack order charges, the nearest enemy of the most important class (panel below the torpedo settings)");
            AiOn = cat.CreateEntry("AiTorpedoCapitalsFirst", true, null, "Battle AI: AI torpedo boats, destroyers and light cruisers aim their tubes at the nearest enemy battleship, battlecruiser or heavy cruiser in torpedo reach instead of the game's choice (often a destroyer, for which the torpedoes were then held as a small target)");
        }
        // 1.11.330 (#71, player report 28.10: AI destroyers in a good firing position sometimes did not launch): the game aims the
        // tubes of an AI ship at its own choice of target, often the nearest destroyer; the hopeless-shot rule then holds them as a
        // small target while a battleship is in reach. AI light ships now aim at the nearest capital ship in reach first.
        internal static MelonPreferences_Entry<bool> AiOn;
        static readonly List<string> AiOrder = new List<string> { "bb", "bc", "ca" };
        static bool AiEnabled { get { return AiOn != null && AiOn.Value; } }
        static bool IsAiLight(Ship s) { try { return s.player != null && !s.player.isMain && BattleAi.IsLight(s); } catch { return false; } }
        static bool Enabled { get { return On != null && On.Value; } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][TorpTarget] " + m); }

        static readonly string[] Classes = { "bb", "bc", "ca", "cl", "dd", "tb", "tr" };
        static readonly Dictionary<IntPtr, List<string>> divList = new Dictionary<IntPtr, List<string>>();
        static readonly Dictionary<IntPtr, List<string>> shipList = new Dictionary<IntPtr, List<string>>();
        static readonly Dictionary<IntPtr, Dictionary<IntPtr, IntPtr>> ours = new Dictionary<IntPtr, Dictionary<IntPtr, IntPtr>>();   // ship -> torpedo PartData -> target set by this module
        static readonly Dictionary<IntPtr, float> next = new Dictionary<IntPtr, float>();
        static IntPtr battlePtr; static int logs, errors;

        static bool InBattle { get { try { return GameManager.IsBattle; } catch { return false; } } }
        static void ResetIfNewBattle()
        {
            IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
            if (bp != battlePtr) { battlePtr = bp; divList.Clear(); shipList.Clear(); ours.Clear(); next.Clear(); logs = 0; }
        }
        static bool IsPlayer(Ship s) { try { return s.player != null && s.player.isMain && (!s.isAiControlled || (TestList != null && !string.IsNullOrWhiteSpace(TestList.Value))); } catch { return false; } }

        static List<string> ListFor(Ship s)
        {
            if (s == null) return null;
            if (shipList.TryGetValue(s.Pointer, out var l) && l.Count > 0) return l;
            try { var d = s.division; if (d != null && divList.TryGetValue(d.Pointer, out var dl) && dl.Count > 0) return dl; } catch { }
            string raw = TestList != null ? TestList.Value : "";
            if (raw != testRaw) { testRaw = raw; testList = null; if (!string.IsNullOrWhiteSpace(raw)) { testList = new List<string>(); foreach (var c in raw.ToLowerInvariant().Split(',', ';', ' ', '>')) if (Array.IndexOf(Classes, c.Trim()) >= 0 && !testList.Contains(c.Trim())) testList.Add(c.Trim()); if (testList.Count == 0) testList = null; else Log("test list for every player division: " + Label(testList)); } }
            return testList;
        }
        static string Label(List<string> l) { return l == null || l.Count == 0 ? "game's choice" : string.Join(" > ", l.ConvertAll(x => x.ToUpperInvariant())); }

        // class as the player knows it (contact report until identified); null = not seen
        static string ClassOf(Ship obs, Ship e)
        {
            try { return Fog.PType(obs.player, e); }   // null = not seen in this battle (the ship is not a candidate)
            catch { try { return e.shipType != null ? e.shipType.name : null; } catch { return null; } }
        }
        static bool Usable(Ship s, Ship e)
        {
            try { return e != null && !e.isDead && !e.isSinking && !e.IsSurrendered && e.IsVisibleFor(s); } catch { return false; }
        }
        // 1.11.255 (author 25.10, 12:36): a listed class the enemy does not have falls through to the next class down, so an order is
        // never left unexecuted (CA listed, the enemy has only CL -> CL). The listed classes come first, in the player's order; then,
        // for each of them, the smaller classes below it (BB > BC > CA > CL > DD > TB > TR).
        internal static List<string> Expand(List<string> order)
        {
            var r = new List<string>(); if (order == null) return r;
            foreach (var c in order) if (!r.Contains(c)) r.Add(c);
            foreach (var c in order)
            {
                int k = Array.IndexOf(Classes, c); if (k < 0) continue;
                for (int j = k + 1; j < Classes.Length; j++) if (!r.Contains(Classes[j])) r.Add(Classes[j]);
            }
            return r;
        }
        // nearest enemy of the most important listed class (within maxRange when > 0)
        static Ship Pick(Ship s, List<string> order, Il2CppSystem.Collections.Generic.List<Ship> enemies, float maxRange, out float bestD, out string cls, bool expand = true)
        {
            bestD = float.MaxValue; cls = null;
            if (order == null || enemies == null) return null;
            foreach (var c in expand ? Expand(order) : order)
            {
                Ship best = null; float bd = float.MaxValue;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (!Usable(s, e)) continue;
                    if (ClassOf(s, e) != c) continue;
                    float d = BattleAi.Dist(s, e);
                    if (maxRange > 0f && d > maxRange) continue;
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null) { bestD = bd; cls = c; return best; }
            }
            return null;
        }
        static Il2CppSystem.Collections.Generic.List<Ship> Enemies(Ship s)
        {
            try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || s.player == null) return null; return b.ShipsOf(b.OppositePlayer(s.player), true); } catch { return null; }
        }

        // the charge target of a player's division under the Torpedo attack order (Mod.cs TorpedoCharge); null = the mod's own rule
        internal static Ship ChargeTarget(Ship leader, Il2CppSystem.Collections.Generic.List<Ship> enemies, out float bestD)
        {
            bestD = float.MaxValue;
            if (!Enabled || leader == null || !IsPlayer(leader)) return null;
            try
            {
                ResetIfNewBattle();
                var order = ListFor(leader); if (order == null) return null;
                var t = Pick(leader, order, enemies, 0f, out bestD, out string cls);
                return t;
            }
            catch (Exception e) { if (errors++ < 5) Log("charge: " + e.Message); return null; }
        }

        // torpedo aims of the player's ships with a priority list
        internal static void Tick()
        {
            if ((!Enabled && !AiEnabled) || !InBattle) return;
            try
            {
                ResetIfNewBattle();
                var cb = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (cb == null) return;
                var ships = cb.Ships; if (ships == null) return;
                float now = Time.time;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    bool alive = false, player = false, ai = false;
                    try { alive = s.haveTorpedoes && !s.isDead && !s.isSinking; if (alive) { player = Enabled && IsPlayer(s); ai = !player && AiEnabled && IsAiLight(s); } } catch { }
                    if (!alive || (!player && !ai)) continue;
                    var order = player ? ListFor(s) : AiOrder;
                    if (order == null) { ReleaseAll(s); continue; }
                    if (next.TryGetValue(s.Pointer, out var nt) && now < nt) continue;
                    next[s.Pointer] = now + 1f + 0.05f * (i % 10);
                    var en = Enemies(s); if (en == null) continue;
                    Aim(s, s.enemies, order, en, player); Aim(s, s.enemiesLeftSide, order, en, player);
                }
            }
            catch (Exception e) { if (errors++ < 5) Log("tick: " + e.Message); }
        }
        static void Aim(Ship s, Il2CppSystem.Collections.Generic.Dictionary<PartData, Ship.Aim> dict, List<string> order, Il2CppSystem.Collections.Generic.List<Ship> en, bool player = true)
        {
            if (dict == null) return;
            if (!ours.TryGetValue(s.Pointer, out var mine)) { mine = new Dictionary<IntPtr, IntPtr>(); ours[s.Pointer] = mine; }
            var keys = new List<PartData>();
            foreach (var kv in dict) if (kv.Key != null && kv.Value != null && kv.Key.isTorpedo) keys.Add(kv.Key);
            var cache = s.weaponRangesCache;
            foreach (var pd in keys)
            {
                var aim = dict[pd]; if (aim == null) continue;
                var cur = aim.target;
                bool haveMine = mine.TryGetValue(pd.Pointer, out var mt);
                if (aim.manual && (!haveMine || cur == null || cur.Pointer != mt)) { if (haveMine) mine.Remove(pd.Pointer); continue; }   // the player's own target
                float range = 0f; if (cache != null) cache.TryGetValue(pd, out range);
                var pick = Pick(s, order, en, range, out float d, out string cls, player);
                if (pick == null) { if (haveMine) { mine.Remove(pd.Pointer); aim.manual = false; } continue; }   // nothing listed in reach: the game's choice
                if (cur != null && cur.Pointer == pick.Pointer) { if (!aim.manual) aim.manual = true; mine[pd.Pointer] = pick.Pointer; continue; }
                // 1.11.337 (test 28.10 09:05: the AI aims were set "instead of none" every second - the game clears an AI ship's
                // torpedo aim while it has no target of its own, and every new aim restarted the aiming at 0, so the tubes never
                // came to bear): an AI ship is retargeted only away from a target the game chose itself, and keeps its aiming progress
                if (!player && (cur == null || BattleAi.IsCapital(cur))) { if (haveMine && cur == null) mine.Remove(pd.Pointer); continue; }
                string was = cur != null ? cur.name : "none";
                aim.target = pick; aim.manual = true; if (player) aim.progress = 0f; mine[pd.Pointer] = pick.Pointer;
                if (logs++ < 60) Log(player ? $"{s.name}: torpedo tubes on {pick.name} ({cls}, {d:0} m, range {range:0} m) - priority {Label(order)}" : $"AI {s.name}: torpedo tubes on capital ship {pick.name} ({cls}, {d:0} m, range {range:0} m) instead of {was}");
            }
        }
        // 1.11.347 (#71, test 28.10 13:05: a flotilla charging the leading ship from ahead held its torpedoes 7 times because the
        // game kept its tubes on the LAST ship, which was running away - "run to the intercept point 9321 m > 8306 m"): the tubes of
        // an AI ship in a two-way attack are laid on the ship the division is attacking (at most every 5 s, aiming progress kept)
        static readonly Dictionary<IntPtr, float> aimAtNext = new Dictionary<IntPtr, float>();
        internal static void AimAt(Ship s, Ship target)
        {
            if (s == null || target == null) return;
            float now = Time.time;
            if (aimAtNext.TryGetValue(s.Pointer, out var t) && now < t) return;
            aimAtNext[s.Pointer] = now + 5f;
            if (aimAtNext.Count > 500) aimAtNext.Clear();
            try
            {
                int n = 0; string was = null;
                foreach (var dict in new[] { s.enemies, s.enemiesLeftSide })
                {
                    if (dict == null) continue;
                    var keys = new List<PartData>();
                    foreach (var kv in dict) if (kv.Key != null && kv.Value != null && kv.Key.isTorpedo) keys.Add(kv.Key);
                    foreach (var pd in keys)
                    {
                        var aim = dict[pd]; if (aim == null) continue;
                        var cur = aim.target; if (cur != null && cur.Pointer == target.Pointer) continue;
                        if (was == null) was = cur != null ? cur.name : "none";
                        aim.target = target; aim.manual = true; n++;
                    }
                }
                if (n > 0 && logs++ < 60) Log($"AI {s.name}: torpedo tubes on the attacked ship {target.name} instead of {was} (two-way attack)");
            }
            catch (Exception e) { if (errors++ < 5) Log("aim at: " + e.Message); }
        }

        static void ReleaseAll(Ship s)
        {
            if (!ours.TryGetValue(s.Pointer, out var mine) || mine.Count == 0) return;
            try
            {
                foreach (var dict in new[] { s.enemies, s.enemiesLeftSide })
                {
                    if (dict == null) continue;
                    foreach (var kv in dict) { var pd = kv.Key; var aim = kv.Value; if (pd == null || aim == null) continue; if (mine.TryGetValue(pd.Pointer, out var mt) && aim.target != null && aim.target.Pointer == mt) aim.manual = false; }
                }
            }
            catch { }
            mine.Clear();
        }

        // ------------------------------------------------------------------ panel
        static GUIStyle sT; static bool folded; static int gErr;
        static Ship Selected() { try { var s = BattleSel.Ship(); return s != null && IsPlayer(s) && !s.isDead && !s.isSinking && s.haveTorpedoes ? s : null; } catch { return null; } }
        internal static void OnGUI()
        {
            if (!Enabled || !InBattle || BattleScreen.Hidden) return;
            try
            {
                TorpedoSettings.BeginSmall();
                var s = Selected(); if (s == null) return;
                ResetIfNewBattle();
                if (sT == null) { sT = new GUIStyle(); sT.fontSize = 12; sT.richText = true; sT.normal.textColor = new Color(0.9f, 0.9f, 0.9f); }
                // 1.11.255 (author 25.10, 12:36): one compact row right below the torpedo setting row
                float w = 600f, h = 24f, x = (UiScale.W - w) / 2f, y = 54f + 48f + 26f;
                var order = ListFor(s);
                var sm = TorpedoSettings.Small();
                if (folded) { if (GUI.Button(new Rect(x + w - 200f, y, 200f, 18f), $"Torpedo targets: {Label(order)} ▾", sm)) folded = false; return; }
                GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
                string scope = shipList.ContainsKey(s.Pointer) ? "ship" : "division";
                GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), "<b>Torp. targets</b>", sT);
                const float bw = 36f; float bx = x + 98f;
                for (int i = 0; i < Classes.Length; i++)
                {
                    string c = Classes[i]; int idx = order != null ? order.IndexOf(c) : -1;
                    var r = new Rect(bx + i * (bw + 2f), y + 3f, bw, 18f);
                    var old = GUI.backgroundColor; if (idx >= 0) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                    if (GUI.Button(r, idx >= 0 ? $"{idx + 1}.{c.ToUpperInvariant()}" : c.ToUpperInvariant(), sm)) Toggle(s, c, Event.current != null && Event.current.shift);
                    GUI.backgroundColor = old;
                }
                float cx = bx + Classes.Length * (bw + 2f);
                if (GUI.Button(new Rect(cx, y + 3f, 40f, 18f), "Clear", sm)) Clear(s, Event.current != null && Event.current.shift);
                float tx = cx + 44f;
                GUI.Label(new Rect(tx, y + 4f, x + w - 24f - tx, 18f), $"<color=#C0C0C0>{scope}</color>", sT);
                if (GUI.Button(new Rect(x + w - 21f, y + 3f, 18f, 18f), "▴", sm)) folded = true;
            }
            catch (Exception e) { if (gErr++ < 5) MelonLogger.Warning("[DIP GG Fixes][TorpTarget] panel: " + e.Message); }
            finally { TorpedoSettings.EndSmall(); }
        }
        static List<string> Edit(Ship s, bool shipOnly)
        {
            var d = s.division;
            if (shipOnly || d == null) { if (!shipList.TryGetValue(s.Pointer, out var l)) { l = new List<string>(); var dl = ListFor(s); if (dl != null) l.AddRange(dl); shipList[s.Pointer] = l; } return l; }
            if (!divList.TryGetValue(d.Pointer, out var x)) { x = new List<string>(); divList[d.Pointer] = x; }
            try { if (d.ships != null) for (int i = 0; i < d.ships.Count; i++) { var o = d.ships[i]; if (o != null) shipList.Remove(o.Pointer); } } catch { }
            return x;
        }
        static void Toggle(Ship s, string c, bool shipOnly)
        {
            var l = Edit(s, shipOnly);
            if (l.Contains(c)) l.Remove(c); else l.Add(c);
            Log($"{s.name}: torpedo target priority for {(shipOnly || s.division == null ? "the ship" : "the division")}: {Label(l)}");
            next.Remove(s.Pointer);
            try { var d = s.division; if (d != null && d.ships != null) for (int i = 0; i < d.ships.Count; i++) { var o = d.ships[i]; if (o != null) next.Remove(o.Pointer); } } catch { }
        }
        static void Clear(Ship s, bool shipOnly)
        {
            var l = Edit(s, shipOnly); l.Clear();
            Log($"{s.name}: torpedo target priority cleared for {(shipOnly || s.division == null ? "the ship" : "the division")} - the game's choice");
        }
    }
}
