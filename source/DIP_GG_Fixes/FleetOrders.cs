// 1.11.107 (P2, first version, off by default): standing orders for the player's task forces on the campaign map.
// The game moves a task force to the point the player chose and generates battles itself; an order is a layer above that:
// once per turn (CampaignController.ProcessTaskForces prefix) the mod sets the force's destination according to its order.
//   Readiness   - the force stays where it is (its station) and goes for a visible enemy force inside the reaction radius;
//                 with no enemy in reach it returns to the station.
//   Shadow      - the force follows a chosen enemy force while it is visible (fog of war), keeping a distance; when contact is
//                 lost it goes to the last known position and then holds.
//   Escort      - the force follows a chosen friendly force (same destination).
// Window: F6 (OrdersKey). Orders are stored per campaign in UserData\IronAdmiral_Economy\orders_<fingerprint>.txt.
// Log lines: "[Orders]".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class FleetOrders
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<string> Key;
        internal static MelonPreferences_Entry<float> DefaultRadiusKm;
        static KeyCode key = KeyCode.F6;
        enum Kind { None, Readiness, Shadow, Escort }
        class Order { public Kind K; public string Target; public float RadiusKm; public Vector3 Station; public bool HasStation; public Vector3 LastSeen; public bool HasLastSeen; }
        static readonly Dictionary<string, Order> orders = new Dictionary<string, Order>();
        static string loadedFor, lastDay; static bool visible; static int page; static int errors;
        static GUIStyle sTxt, sBtn, sTitle, sDim;
        static readonly List<KeyValuePair<Rect, Action>> clicks = new List<KeyValuePair<Rect, Action>>();
        static readonly List<KeyValuePair<Rect, Action>> next = new List<KeyValuePair<Rect, Action>>();

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("FleetOrders", false, null, "Campaign (experimental): standing orders for your task forces - readiness (react to visible enemies in a radius), shadow an enemy force, escort a friendly force; window on the key below");
            Key = cat.CreateEntry("OrdersKey", "F6", null, "Key that opens the task force orders window");
            DefaultRadiusKm = cat.CreateEntry("OrdersReactionRadiusKm", 600f, null, "Default reaction radius of a readiness order (km)");
            KeyCode k; if (Enum.TryParse(Key.Value, true, out k)) key = k;
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Orders] " + m); }
        static float Kpu() { try { if (CampaignMap.UnityDistanceToWorldKmMultiplier > 0f) return CampaignMap.UnityDistanceToWorldKmMultiplier; } catch { } return 1f; }

        // ------------------------------------------------------------------ data
        static Player Main() { try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p != null && p.isMain) return p; } } catch { } return null; }
        static List<CampaignController.TaskForce> All()
        {
            var l = new List<CampaignController.TaskForce>();
            try { var a = CampaignController.Instance.CampaignData.TaskForces; for (int i = 0; a != null && i < a.Count; i++) if (a[i] != null) l.Add(a[i]); } catch { }
            return l;
        }
        static string Id(CampaignController.TaskForce tf) { try { return tf.Id.ToString(); } catch { return null; } }
        static bool Own(CampaignController.TaskForce tf, Player main) { try { return tf.Controller != null && tf.Controller.Pointer == main.Pointer; } catch { return false; } }
        static int Count(CampaignController.TaskForce tf) { try { return tf.Vessels != null ? tf.Vessels.Count : 0; } catch { return 0; } }
        static string Name(CampaignController.TaskForce tf)
        {
            string n = "?"; try { var v = tf.Vessels; if (v != null && v.Count > 0) { var s = v[0].TryCast<Ship>(); if (s != null) n = Dispatch.Ship(s.name); } } catch { }
            string who = "?"; try { who = tf.Controller.data.nameUi; } catch { }
            return $"{who}: {n}{(Count(tf) > 1 ? " +" + (Count(tf) - 1) : "")}";
        }
        // an enemy force counts as seen when the fog of war lets the player locate it (or the fog is off)
        static bool Seen(Player main, CampaignController.TaskForce tf)
        {
            try { if (!Fog.MapActive) return true; return Fog.Q(main, tf) >= 0.35f; } catch { return true; }
        }
        static bool Busy(CampaignController.TaskForce tf) { try { var g = tf.BusyForEvent.ToString(); return !string.IsNullOrEmpty(g) && g != "00000000-0000-0000-0000-000000000000"; } catch { return false; } }
        static bool Enemy(Player main, CampaignController.TaskForce tf)
        {
            try { var c = tf.Controller; if (c == null || c.Pointer == main.Pointer) return false; foreach (var kv in CampaignController.Instance.CampaignData.Relations) { var r = kv.Value; if (r != null && r.isWar && r.AnyOnePlayerIs(main) && r.AnyOnePlayerIs(c)) return true; } return false; } catch { return false; }
        }

        // ------------------------------------------------------------------ persistence
        static string FilePath() { return Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral_Economy", "orders_" + Economy.Fingerprint() + ".txt"); }
        static void EnsureLoaded()
        {
            string fp = null; try { fp = Economy.Fingerprint(); } catch { }
            if (fp == null || fp == loadedFor) return;
            loadedFor = fp; orders.Clear();
            try
            {
                var f = FilePath(); if (!File.Exists(f)) return;
                foreach (var l in File.ReadAllLines(f))
                {
                    var c = l.Split('|'); if (c.Length < 9) continue;
                    if (!Enum.TryParse(c[1], out Kind k)) continue;
                    var o = new Order { K = k, Target = c[2], RadiusKm = P(c[3]), HasStation = c[4] == "1", Station = new Vector3(P(c[5]), P(c[6]), P(c[7])) };
                    orders[c[0]] = o;
                }
                Log($"{orders.Count} order(s) loaded");
            }
            catch (Exception e) { Log("load: " + e.Message); }
        }
        static float P(string s) { float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v); return v; }
        static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
        static void Save()
        {
            try
            {
                var f = FilePath(); Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllLines(f, orders.Where(kv => kv.Value.K != Kind.None).Select(kv => $"{kv.Key}|{kv.Value.K}|{kv.Value.Target}|{F(kv.Value.RadiusKm)}|{(kv.Value.HasStation ? 1 : 0)}|{F(kv.Value.Station.x)}|{F(kv.Value.Station.y)}|{F(kv.Value.Station.z)}|0"));
            }
            catch (Exception e) { Log("save: " + e.Message); }
        }

        // ------------------------------------------------------------------ turn
        internal static void BeforeTurn()
        {
            if (On == null || !On.Value) return;
            // ProcessTaskForces may run more than once per turn: apply orders once per campaign date
            string day = null; try { day = CampaignController.Instance.CurrentDate.AsDate().ToString("yyyy-MM-dd"); } catch { }
            if (day != null && day == lastDay) return; lastDay = day;
            try
            {
                EnsureLoaded(); if (orders.Count == 0) return;
                var main = Main(); if (main == null) return;
                var all = All(); var byId = new Dictionary<string, CampaignController.TaskForce>();
                foreach (var t in all) { var i = Id(t); if (i != null) byId[i] = t; }
                float kpu = Kpu();
                foreach (var kv in orders.ToList())
                {
                    var o = kv.Value; if (o.K == Kind.None) continue;
                    if (!byId.TryGetValue(kv.Key, out var tf) || !Own(tf, main)) { orders.Remove(kv.Key); Log($"force {kv.Key} gone - order removed"); continue; }
                    if (tf.GroupMovingToBattle || Busy(tf)) continue;
                    Vector3 pos = tf.WorldPos, dest = pos; string why = null;
                    if (o.K == Kind.Readiness)
                    {
                        if (!o.HasStation) { o.Station = pos; o.HasStation = true; }
                        CampaignController.TaskForce best = null; float bd = float.MaxValue;
                        foreach (var e in all)
                        {
                            if (!Enemy(main, e) || !Seen(main, e)) continue;
                            float dKm = Vector3.Distance(e.WorldPos, o.Station) * kpu; if (dKm > o.RadiusKm) continue;
                            if (dKm < bd) { bd = dKm; best = e; }
                        }
                        if (best != null) { dest = best.WorldPos; why = $"intercepts {Name(best)} ({bd:0} km from station)"; }
                        else { dest = o.Station; why = Vector3.Distance(pos, o.Station) * kpu > 30f ? "returns to station" : null; }
                    }
                    else if (o.K == Kind.Shadow || o.K == Kind.Escort)
                    {
                        byId.TryGetValue(o.Target ?? "", out var tg);
                        bool ok = tg != null && (o.K == Kind.Escort ? Own(tg, main) : (Enemy(main, tg) && Seen(main, tg)));
                        if (ok)
                        {
                            var tp = tg.WorldPos; o.LastSeen = tp; o.HasLastSeen = true;
                            if (o.K == Kind.Escort) { dest = tp; try { if (tg.IsMoving()) dest = tg.ToPosition; } catch { } why = $"escorts {Name(tg)}"; }
                            else { var dir = (pos - tp); dir.y = 0f; float keep = 150f / kpu; dest = dir.sqrMagnitude > 1e-4f ? tp + dir.normalized * keep : tp; why = $"shadows {Name(tg)}"; }
                        }
                        else if (o.HasLastSeen) { dest = o.LastSeen; o.HasLastSeen = false; why = o.K == Kind.Shadow ? "contact lost - to the last known position" : "escorted force gone"; }
                        if (tg == null && o.K == Kind.Escort) { o.K = Kind.None; }
                    }
                    if (why == null) continue;
                    if (Vector3.Distance(dest, pos) * kpu < 20f) continue;
                    try
                    {
                        tf.ToPosition = dest; tf.recalculatePathRequest = 1; tf.RecalculatePath();
                        Log($"{Name(tf)}: {o.K} - {why}; destination set ({Vector3.Distance(dest, pos) * kpu:0} km)");
                    }
                    catch (Exception e) { if (errors++ < 10) Log($"{Name(tf)}: moving failed - {e.Message}"); }
                }
                Save();
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] turn: " + e.Message); }
        }

        // ------------------------------------------------------------------ window
        internal static void Tick()
        {
            if (On == null || !On.Value) { visible = false; return; }
            try
            {
                bool map = false; try { map = GameManager.IsCampaign && GameManager.IsWorldMap && !GameManager.IsLoadingAny; } catch { }
                if (!map) { visible = false; return; }
                if (Input.GetKeyDown(key)) { visible = !visible; if (visible) { EnsureLoaded(); Log("orders window opened"); } }
                if (visible && Input.GetMouseButtonDown(0))
                {
                    var p = UiScale.Mouse();
                    foreach (var kv in clicks.ToList()) if (kv.Key.Contains(p)) { try { kv.Value(); } catch (Exception e) { Log("click: " + e.Message); } break; }
                }
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] " + e.Message); }
        }
        static void Styles()
        {
            if (sTxt != null) return;
            sTitle = new GUIStyle(); sTitle.fontSize = 16; sTitle.richText = true; sTitle.normal.textColor = Color.white;
            sTxt = new GUIStyle(); sTxt.fontSize = 13; sTxt.richText = true; sTxt.normal.textColor = Color.white; sTxt.clipping = TextClipping.Clip;
            sDim = new GUIStyle(); sDim.fontSize = 12; sDim.richText = true; sDim.wordWrap = true; sDim.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
            sBtn = new GUIStyle(); sBtn.fontSize = 12; sBtn.alignment = TextAnchor.MiddleCenter; sBtn.richText = true; sBtn.normal.textColor = Color.white;
        }
        static void Button(Rect r, string label, Action a) { GUI.Box(r, ""); GUI.Label(r, label, sBtn); next.Add(new KeyValuePair<Rect, Action>(r, a)); }
        static Order Get(string id) { if (!orders.TryGetValue(id, out var o)) { o = new Order { K = Kind.None, RadiusKm = DefaultRadiusKm.Value }; orders[id] = o; } return o; }

        internal static void OnGUI()
        {
            if (On == null || !On.Value || !visible) return;
            try
            {
                var main = Main(); if (main == null) return;
                Styles(); next.Clear();
                var all = All();
                var own = all.Where(t => Own(t, main)).ToList();
                var enemies = all.Where(t => Enemy(main, t) && Seen(main, t)).ToList();
                float w = Math.Min(900f, UiScale.W - 40f), h = Math.Min(620f, UiScale.H - 120f);
                var r = new Rect((UiScale.W - w) / 2f, 80f, w, h);
                GUI.Box(r, ""); GUI.Box(r, ""); GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 120f, 24f), $"<b>Task force orders</b> (experimental)   ({Key.Value} closes)", sTitle);
                Button(new Rect(r.xMax - 40f, r.y + 8f, 30f, 26f), "X", () => visible = false);
                GUI.Label(new Rect(r.x + 14f, r.y + 36f, w - 28f, 36f), "Readiness: hold the station and go for a visible enemy in the radius. Shadow: follow a visible enemy force. Escort: follow one of your forces. Orders are applied at the start of each turn; your own move orders still work but may be replaced by a standing order.", sDim);
                float y = r.y + 80f, rowH = 30f; int per = Mathf.Max(1, (int)((r.yMax - 50f - y) / rowH));
                int pages = Mathf.Max(1, (own.Count + per - 1) / per); page = Mathf.Clamp(page, 0, pages - 1);
                if (own.Count == 0) GUI.Label(new Rect(r.x + 14f, y, w - 28f, 24f), "You have no task forces at sea.", sTxt);
                foreach (var tf in own.Skip(page * per).Take(per))
                {
                    var id = Id(tf); if (id == null) continue; var o = Get(id);
                    GUI.Label(new Rect(r.x + 14f, y + 5f, 300f, 22f), Name(tf) + $" <color=#AAAAAA>({Count(tf)} ships)</color>", sTxt);
                    float bx = r.x + 320f;
                    foreach (Kind k in Enum.GetValues(typeof(Kind)))
                    {
                        var kk = k; bool cur = o.K == k;
                        Button(new Rect(bx, y + 2f, 82f, 24f), (cur ? "<color=#FFD95A><b>" : "") + k + (cur ? "</b></color>" : ""), () =>
                        {
                            o.K = kk; o.HasLastSeen = false;
                            if (kk == Kind.Readiness) { o.Station = tf.WorldPos; o.HasStation = true; }
                            if (kk == Kind.Shadow) o.Target = enemies.Count > 0 ? Id(enemies[0]) : null;
                            if (kk == Kind.Escort) { var f = own.FirstOrDefault(x => x.Pointer != tf.Pointer); o.Target = f != null ? Id(f) : null; }
                            Save(); Log($"{Name(tf)}: order {kk}{(o.Target != null && kk >= Kind.Shadow ? " target " + o.Target : "")}");
                        });
                        bx += 86f;
                    }
                    if (o.K == Kind.Readiness)
                    {
                        Button(new Rect(bx, y + 2f, 26f, 24f), "-", () => { o.RadiusKm = Mathf.Max(100f, o.RadiusKm - 100f); Save(); });
                        GUI.Label(new Rect(bx + 30f, y + 5f, 70f, 22f), $"{o.RadiusKm:0} km", sTxt);
                        Button(new Rect(bx + 100f, y + 2f, 26f, 24f), "+", () => { o.RadiusKm = Mathf.Min(3000f, o.RadiusKm + 100f); Save(); });
                    }
                    else if (o.K == Kind.Shadow || o.K == Kind.Escort)
                    {
                        var cands = o.K == Kind.Shadow ? enemies : own.Where(x => x.Pointer != tf.Pointer).ToList();
                        int ci = cands.FindIndex(x => Id(x) == o.Target);
                        string lab = ci >= 0 ? Name(cands[ci]) : (o.Target != null ? "(not in sight)" : "(none)");
                        Button(new Rect(bx, y + 2f, 230f, 24f), lab + "  >", () => { if (cands.Count == 0) return; int n = (ci + 1) % cands.Count; o.Target = Id(cands[n]); Save(); });
                    }
                    y += rowH;
                }
                if (pages > 1)
                {
                    Button(new Rect(r.x + 14f, r.yMax - 36f, 30f, 24f), "<", () => page--);
                    GUI.Label(new Rect(r.x + 50f, r.yMax - 33f, 100f, 22f), $"page {page + 1}/{pages}", sTxt);
                    Button(new Rect(r.x + 150f, r.yMax - 36f, 30f, 24f), ">", () => page++);
                }
                clicks.Clear(); clicks.AddRange(next);
            }
            catch (Exception e) { if (errors++ < 10) MelonLogger.Warning("[DIP GG Fixes][Orders] window: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CampaignController), "ProcessTaskForces")]
    internal static class Patch_FleetOrders
    {
        private static void Prefix() { FleetOrders.BeforeTurn(); }
    }
}
