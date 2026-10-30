// 1.11.259 (#37, author's decisions 25.10, 12:33-12:36): gun orders for the player's divisions, built like the torpedo target
// priorities. Two compact strips below the torpedo strips: main battery and secondary battery.
//   - Class priorities: ship classes clicked in order of importance (BB, BC, CA, CL, DD, TB, TR); a class the enemy does not have
//     passes to the next class down (TorpedoPriority.Expand), so an order is never left unexecuted. Nearest enemy of the most
//     important class within the battery's range.
//   - Default when nothing is clicked: threat. Main battery: the enemy whose guns threaten the ship most (main calibre squared over
//     the range); secondary battery: torpedo craft (TB, DD, and cruisers carrying torpedoes) inside 1.5x their torpedo range,
//     nearest first; when there is none, the game's own choice.
//   - Split fire (main battery): the guns bearing to starboard and to port may take different targets (the game keeps one aim per
//     side); while the two sides fire at different ships the main battery's hit chance is multiplied by GunSplitAccuracy (0.7) -
//     two fall-of-shot patterns to spot at once (assumption of the mod; the author's decision 25.10: split allowed with a penalty).
//   - A target the player gives himself is kept; AI ships keep their own rules. Shift = this ship only (as for torpedoes).
// Switches: GunOrders, GunSplitAccuracy (F10, Battle AI).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class GunOrders
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> SplitAcc;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("GunOrders", true, null, "Battle: gun orders for the player's divisions - target priorities by ship class for the main and the secondary battery (default: the greatest threat), split fire of the main battery (strips below the torpedo strips)");
            SplitAcc = cat.CreateEntry("GunSplitAccuracy", 0.7f, null, "Battle: hit chance of the main battery while it fires at two targets at once (split fire), as a fraction of the normal one");
        }
        static bool Enabled { get { return On != null && On.Value; } }
        static bool InBattle { get { try { return GameManager.IsBattle; } catch { return false; } } }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][GunOrders] " + m); }
        static readonly string[] Classes = { "bb", "bc", "ca", "cl", "dd", "tb", "tr" };

        class Ord { public List<string> Main = new List<string>(), Sec = new List<string>(); public bool Split; public int Aim; }
        internal static readonly string[] AimNames = { "Auto", "Waterline", "Upper works" };
        // 0 = by shell type (AP waterline, HE upper works), 1 = waterline for every shell, 2 = upper works for every shell; AI ships 0
        internal static int AimModeFor(Ship s) { try { if (!Enabled || s == null || !IsPlayer(s)) return 0; var o = OrdFor(s); return o != null ? o.Aim : 0; } catch { return 0; } }
        static void SetAim(Ship s, int mode, bool shipOnly) { var o = Edit(s, shipOnly); o.Aim = mode; Touch(s); Log($"{s.name}: aim {AimNames[mode]} ({(shipOnly ? "ship" : "division")})"); }
        static readonly Dictionary<IntPtr, Ord> divOrd = new Dictionary<IntPtr, Ord>(), shipOrd = new Dictionary<IntPtr, Ord>();
        static readonly Dictionary<IntPtr, Dictionary<IntPtr, IntPtr>> ours = new Dictionary<IntPtr, Dictionary<IntPtr, IntPtr>>(); // ship -> aim -> target
        static readonly Dictionary<IntPtr, float> next = new Dictionary<IntPtr, float>();
        static readonly HashSet<IntPtr> splitNow = new HashSet<IntPtr>();   // ships whose two sides fire at different targets
        static IntPtr battlePtr; static int logs, errors;
        static void ResetIfNewBattle()
        {
            IntPtr bp = IntPtr.Zero; try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b != null) bp = b.Pointer; } catch { }
            if (bp != battlePtr) { battlePtr = bp; divOrd.Clear(); shipOrd.Clear(); ours.Clear(); next.Clear(); splitNow.Clear(); logs = 0; }
        }
        static bool IsPlayer(Ship s) { try { return s != null && s.player != null && s.player.isMain && !s.isAiControlled; } catch { return false; } }
        static Ord OrdFor(Ship s)
        {
            if (shipOrd.TryGetValue(s.Pointer, out var o)) return o;
            try { var d = s.division; if (d != null && divOrd.TryGetValue(d.Pointer, out var x)) return x; } catch { }
            return null;
        }
        static string ClassOf(Ship obs, Ship e) { try { return Fog.PType(obs.player, e); } catch { return null; } }
        static bool Usable(Ship s, Ship e) { try { return e != null && !e.isDead && !e.isSinking && !e.IsSurrendered && e.IsVisibleFor(s); } catch { return false; } }
        static Il2CppSystem.Collections.Generic.List<Ship> Enemies(Ship s)
        {
            try { var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null || s.player == null) return null; return b.ShipsOf(b.OppositePlayer(s.player), true); } catch { return null; }
        }
        // +1 starboard, -1 port
        static int SideOf(Ship s, Ship e) { try { var l = s.transform.InverseTransformPoint(e.transform.position); return l.x >= 0f ? 1 : -1; } catch { return 0; } }

        static Ship ByClass(Ship s, List<string> order, Il2CppSystem.Collections.Generic.List<Ship> en, float range, int side, out string cls)
        {
            cls = null;
            foreach (var c in TorpedoPriority.Expand(order))
            {
                Ship best = null; float bd = float.MaxValue;
                for (int i = 0; i < en.Count; i++)
                {
                    var e = en[i]; if (!Usable(s, e) || ClassOf(s, e) != c || (noTr && IsTr(e))) continue;
                    if (side != 0 && SideOf(s, e) != side) continue;
                    float d = BattleAi.Dist(s, e); if (range > 0f && d > range) continue;
                    if (d < bd) { bd = d; best = e; }
                }
                if (best != null) { cls = c; return best; }
            }
            return null;
        }
        static Ship Threat(Ship s, bool main, Il2CppSystem.Collections.Generic.List<Ship> en, float range, int side, out string why)
        {
            why = null; Ship best = null; float bs = 0f;
            for (int i = 0; i < en.Count; i++)
            {
                var e = en[i]; if (!Usable(s, e) || (noTr && IsTr(e))) continue;
                if (side != 0 && SideOf(s, e) != side) continue;
                float d = BattleAi.Dist(s, e); if (range > 0f && d > range) continue;
                float score;
                if (main) { float cal = 0f; try { cal = BattleAi.MainCaliber(e); } catch { } if (cal <= 0f) cal = 100f; score = cal * cal / Math.Max(2000f, d); }
                else
                {
                    string c = ClassOf(s, e); bool torp = false; try { torp = e.haveTorpedoes; } catch { }
                    if (!(c == "tb" || c == "dd" || (c == "cl" && torp) || (c == "ca" && torp))) continue;
                    float tr = 0f; try { tr = BattleAi.TorpedoRange(e); } catch { }
                    if (tr > 0f && d > tr * 1.5f) continue;
                    score = 1e7f / Math.Max(500f, d);
                }
                if (score > bs) { bs = score; best = e; why = main ? "greatest gun threat" : "torpedo craft closing"; }
            }
            return best;
        }

        // Called from the mod's target selection (Mod.cs SmartTargets / Apply) for each gun group of a player's ship, so the gun
        // orders and the mod's own rules (player's orders hold, escort first, aim locks) work on the same aims instead of fighting.
        // Returns null = no gun order applies here: the mod's own choice stands.
        internal static Ship Choose(Ship s, PartData pd, bool main, int side, Il2CppSystem.Collections.Generic.List<Ship> en, float range, Ship cur, bool trOk)
        {
            if (!Enabled || s == null || pd == null || en == null || !IsPlayer(s)) return null;
            try
            {
                ResetIfNewBattle();
                var o = OrdFor(s);
                var list = o != null ? (main ? o.Main : o.Sec) : null;
                int sd = main && o != null && o.Split ? side : 0;
                noTr = !trOk;
                Ship pick; string why;
                if (list != null && list.Count > 0) { pick = ByClass(s, list, en, range, sd, out string cls); why = cls != null ? "class " + cls.ToUpperInvariant() : null; }
                else pick = Threat(s, main, en, range, sd, out why);
                if (pick == null && sd != 0) { if (list != null && list.Count > 0) { pick = ByClass(s, list, en, range, 0, out string c2); why = c2 != null ? "class " + c2.ToUpperInvariant() : null; } else pick = Threat(s, main, en, range, 0, out why); }
                noTr = false;
                if (pick != null && cur != null && cur.Pointer != pick.Pointer && Usable(s, cur) && (!IsTr(cur) || trOk))
                {
                    // hysteresis: keep the current target while it is still a valid choice of the same kind
                    bool keep;
                    if (list == null || list.Count == 0) { float dc = BattleAi.Dist(s, cur), dp = BattleAi.Dist(s, pick); keep = (range <= 0f || dc <= range) && dp > dc * 0.75f && (!main || Same(s, cur, pick)) && (main || IsTorpCraft(s, cur)); }
                    else { keep = ClassOf(s, cur) == ClassOf(s, pick) && (range <= 0f || BattleAi.Dist(s, cur) <= range) && (sd == 0 || SideOf(s, cur) == sd); }
                    if (keep) pick = cur;
                }
                if (main)
                {
                    if (!sides.TryGetValue(s.Pointer, out var sp)) { sp = new IntPtr[2]; sides[s.Pointer] = sp; }
                    sp[side > 0 ? 0 : 1] = pick != null ? pick.Pointer : IntPtr.Zero;
                    bool split = o != null && o.Split && sp[0] != IntPtr.Zero && sp[1] != IntPtr.Zero && sp[0] != sp[1];
                    if (split) splitNow.Add(s.Pointer); else splitNow.Remove(s.Pointer);
                }
                if (pick != null && (cur == null || cur.Pointer != pick.Pointer) && logs++ < 80)
                    Log($"{s.name}: {(main ? "main" : "secondary")} battery ({(side > 0 ? "starboard" : "port")} aim) on {pick.name} - {why ?? "priority"}{(o != null && o.Split && main ? ", split fire" : "")}");
                return pick;
            }
            catch (Exception e) { noTr = false; if (errors++ < 5) Log("choose: " + e.Message); return null; }
        }
        static bool noTr;
        static readonly Dictionary<IntPtr, IntPtr[]> sides = new Dictionary<IntPtr, IntPtr[]>();
        static bool IsTr(Ship e) { try { var t = e != null ? e.shipType : null; return t != null && t.name == "tr"; } catch { return false; } }
        static bool Same(Ship s, Ship a, Ship b) { float ca = 0f, cb = 0f; try { ca = BattleAi.MainCaliber(a); cb = BattleAi.MainCaliber(b); } catch { } return cb <= ca * 1.15f; }
        static bool IsTorpCraft(Ship s, Ship e) { string c = ClassOf(s, e); bool torp = false; try { torp = e.haveTorpedoes; } catch { } return c == "tb" || c == "dd" || ((c == "cl" || c == "ca") && torp); }

        internal static float SplitFactor(Ship s, PartData pd)
        {
            try
            {
                if (!Enabled || s == null || pd == null || !splitNow.Contains(s.Pointer)) return 1f;
                if (!s.IsMainCal(pd)) return 1f;
                return Mathf.Clamp(SplitAcc != null ? SplitAcc.Value : 0.7f, 0.2f, 1f);
            }
            catch { return 1f; }
        }

        // ------------------------------------------------------------------ panel: two compact rows below the torpedo strips
        static GUIStyle sT; static bool folded; static int gErr;
        static Ship Selected() { try { var s = BattleSel.Ship(); return s != null && IsPlayer(s) && !s.isDead && !s.isSinking ? s : null; } catch { return null; } }
        internal static void OnGUI()
        {
            if (!Enabled || !InBattle || BattleScreen.Hidden) return;
            if (BattleAi.CameraBusy()) return; // 1.11.439 (author 30.10 16:00: "when the camera moves, the whole orders list does not disappear"): the fire settings rows hide with the order buttons
            try
            {
                TorpedoSettings.BeginSmall();
                var s = Selected(); if (s == null) return;
                ResetIfNewBattle();
                if (sT == null) { sT = new GUIStyle(); sT.fontSize = 12; sT.richText = true; sT.normal.textColor = new Color(0.9f, 0.9f, 0.9f); }
                bool torp = false; try { torp = s.haveTorpedoes; } catch { }
                float w = 600f, h = 24f, x = (UiScale.W - w) / 2f, y = 54f + 48f + (torp ? 52f : 0f);
                var sm = TorpedoSettings.Small();
                var o = OrdFor(s);
                if (folded) { if (GUI.Button(new Rect(x + w - 200f, y, 200f, 18f), $"Gun orders: {Lab(o != null ? o.Main : null)} / {Lab(o != null ? o.Sec : null)} â–ľ", sm)) folded = false; return; }
                Row(s, o, true, x, y, w, h, sm);
                Row(s, o, false, x, y + 26f, w, h, sm);
                AimRow(s, o, x, y + 52f, w, h, sm);
                if (GUI.Button(new Rect(x + w - 21f, y + 3f, 18f, 18f), "â–´", sm)) folded = true;
            }
            catch (Exception e) { if (gErr++ < 5) MelonLogger.Warning("[DIP GG Fixes][GunOrders] panel: " + e.Message); }
            finally { TorpedoSettings.EndSmall(); }
        }
        static string Lab(List<string> l) { return l == null || l.Count == 0 ? "threat" : string.Join(">", l.ConvertAll(c => c.ToUpperInvariant())); }
        static void Row(Ship s, Ord o, bool main, float x, float y, float w, float h, GUIStyle sm)
        {
            GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), main ? "<b>Main guns</b>" : "<b>Secondary</b>", sT);
            var list = o != null ? (main ? o.Main : o.Sec) : null;
            const float bw = 36f; float bx = x + 98f;
            for (int i = 0; i < Classes.Length; i++)
            {
                string c = Classes[i]; int idx = list != null ? list.IndexOf(c) : -1;
                var old = GUI.backgroundColor; if (idx >= 0) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                if (GUI.Button(new Rect(bx + i * (bw + 2f), y + 3f, bw, 18f), idx >= 0 ? $"{idx + 1}.{c.ToUpperInvariant()}" : c.ToUpperInvariant(), sm)) Toggle(s, main, c, Shift());
                GUI.backgroundColor = old;
            }
            float cx = bx + Classes.Length * (bw + 2f);
            var o2 = GUI.backgroundColor; if (list == null || list.Count == 0) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
            if (GUI.Button(new Rect(cx, y + 3f, 50f, 18f), "Threat", sm)) Clear(s, main, Shift());
            GUI.backgroundColor = o2; cx += 52f;
            if (main)
            {
                bool sp = o != null && o.Split;
                var o3 = GUI.backgroundColor; if (sp) GUI.backgroundColor = new Color(1f, 0.85f, 0.35f);
                if (GUI.Button(new Rect(cx, y + 3f, 40f, 18f), "Split", sm)) SetSplit(s, !sp, Shift());
                GUI.backgroundColor = o3; cx += 42f;
            }
            string scope = shipOrd.ContainsKey(s.Pointer) ? "ship" : "division";
            GUI.Label(new Rect(cx + 2f, y + 4f, x + w - 26f - cx, 18f), $"<color=#C0C0C0>{scope}</color>", sT);
        }
        static void AimRow(Ship s, Ord o, float x, float y, float w, float h, GUIStyle sm)
        {
            GUI.Box(new Rect(x, y, w, h), ""); BattleSel.Panel(new Rect(x, y, w, h));
            GUI.Label(new Rect(x + 6f, y + 4f, 92f, 18f), "<b>Aim</b>", sT);
            int cur = o != null ? o.Aim : 0; float bx = x + 98f; float[] bw = { 50f, 74f, 90f };
            for (int i = 0; i < AimNames.Length; i++)
            {
                var old = GUI.backgroundColor; if (i == cur) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                if (GUI.Button(new Rect(bx, y + 3f, bw[i], 18f), AimNames[i], sm)) SetAim(s, i, Shift());
                GUI.backgroundColor = old; bx += bw[i] + 2f;
            }
            string scope = shipOrd.ContainsKey(s.Pointer) ? "ship" : "division";
            GUI.Label(new Rect(bx + 2f, y + 4f, x + w - 26f - bx, 18f), $"<color=#C0C0C0>{scope}; AP waterline / HE upper works when Auto</color>", sT);
        }
        static bool Shift() { return Event.current != null && Event.current.shift; }
        static Ord Edit(Ship s, bool shipOnly)
        {
            var d = s.division;
            if (shipOnly || d == null)
            {
                if (!shipOrd.TryGetValue(s.Pointer, out var so)) { so = new Ord(); var dv = OrdFor(s); if (dv != null) { so.Main.AddRange(dv.Main); so.Sec.AddRange(dv.Sec); so.Split = dv.Split; so.Aim = dv.Aim; } shipOrd[s.Pointer] = so; }
                return so;
            }
            if (!divOrd.TryGetValue(d.Pointer, out var x)) { x = new Ord(); divOrd[d.Pointer] = x; }
            try { if (d.ships != null) for (int i = 0; i < d.ships.Count; i++) { var o = d.ships[i]; if (o != null) shipOrd.Remove(o.Pointer); } } catch { }
            return x;
        }
        static void Touch(Ship s) { next.Remove(s.Pointer); try { var d = s.division; if (d != null && d.ships != null) for (int i = 0; i < d.ships.Count; i++) { var o = d.ships[i]; if (o != null) next.Remove(o.Pointer); } } catch { } }
        static void Toggle(Ship s, bool main, string c, bool shipOnly)
        {
            var o = Edit(s, shipOnly); var l = main ? o.Main : o.Sec;
            if (l.Contains(c)) l.Remove(c); else l.Add(c);
            Log($"{s.name}: {(main ? "main" : "secondary")} battery priority for {(shipOnly || s.division == null ? "the ship" : "the division")}: {Lab(l)}");
            Touch(s);
        }
        static void Clear(Ship s, bool main, bool shipOnly)
        {
            var o = Edit(s, shipOnly); (main ? o.Main : o.Sec).Clear();
            Log($"{s.name}: {(main ? "main" : "secondary")} battery for {(shipOnly || s.division == null ? "the ship" : "the division")}: greatest threat");
            Touch(s);
        }
        static void SetSplit(Ship s, bool on, bool shipOnly)
        {
            var o = Edit(s, shipOnly); o.Split = on;
            Log($"{s.name}: main battery split fire {(on ? "on" : "off")} for {(shipOnly || s.division == null ? "the ship" : "the division")}");
            Touch(s);
        }
    }

    // the split-fire penalty on the main battery's hit chance (after the fire-control era factor)
    [HarmonyPatch(typeof(GunData), "HitChanceMult")]
    internal static class Patch_HitChanceMult_GunSplit
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(Ship __0, PartData __1, ref float __result) { try { __result *= GunOrders.SplitFactor(__0, __1); } catch { } }
    }
}