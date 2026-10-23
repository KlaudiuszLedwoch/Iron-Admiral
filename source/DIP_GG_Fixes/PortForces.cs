// 1.11.120 (beta): on the campaign map every port with ships lying in it shows how many ships and how much tonnage are there.
// Own and allied ports are shown exactly; foreign ports follow the fog of war (Fog.PortQ): unknown ("?") when intelligence is
// poor, an estimate ("~") when it is partial, exact when it is good. The estimate is fixed for the month, so it does not
// flicker. Switch: MapPortForces (F10).
// 1.11.120 (beta): AlwaysShowSplashes - shell splashes in battle are drawn at any distance and size (the game skips effects it
// judges too small or too far away: Effect.VisibilityCheck); costs some frame rate in big battles.
// 1.11.126 (beta): labels sit under the port name on a dark background, never cover the game's panels, windows or tooltips,
// and a label that would overlap another one is left out (zoom in to see it).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class PortForces
    {
        internal static MelonPreferences_Entry<bool> On, Splashes;
        class Entry { public IntPtr Key; public Vector3 Pos; public int Ships; public float Tons; public string Text; public Color Col; public float W; }
        static readonly List<Entry> entries = new List<Entry>();
        static float next, nextCover; static int errors; static GUIStyle st;
        static readonly Dictionary<IntPtr, bool> covered = new Dictionary<IntPtr, bool>();
        static readonly HashSet<string> seenCover = new HashSet<string>();
        static UnityEngine.EventSystems.PointerEventData ped;
        static Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> hits;

        // true when a screen-space interface element (a window, panel or tooltip) lies at this screen point (pixels, origin bottom left);
        // the map's own world-space elements (port flags and names) do not count
        static bool UnderUi(UnityEngine.EventSystems.EventSystem es, float px, float py)
        {
            if (es == null) return false;
            try
            {
                if (ped == null) ped = new UnityEngine.EventSystems.PointerEventData(es);
                if (hits == null) hits = new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
                ped.position = new Vector2(px, py); hits.Clear(); es.RaycastAll(ped, hits);
                for (int i = 0; i < hits.Count; i++)
                {
                    var go = hits[i].gameObject; if (go == null) continue;
                    var cv = go.GetComponentInParent<Canvas>();
                    if (cv != null && cv.rootCanvas != null && cv.rootCanvas.renderMode == RenderMode.WorldSpace) continue;
                    // 1.11.129: log of 2026-10-23 - the map's own markers (port names, flags, province names) are screen-space interface
                    // too and hid labels of neighbouring ports; anything under a map marker does not count as a window
                    if (MapMarker(go.transform)) continue;
                    if (seenCover.Count < 15 && seenCover.Add(go.name)) Log("label hidden under interface element: " + Path(go.transform));
                    return true;
                }
            }
            catch (Exception ex) { if (errors++ < 5) Log("interface check: " + ex.Message); }
            return false;
        }

        static readonly Dictionary<IntPtr, bool> markerCache = new Dictionary<IntPtr, bool>();
        static bool MapMarker(Transform t)
        {
            if (t == null) return false;
            bool r; if (markerCache.TryGetValue(t.Pointer, out r)) return r;
            r = false;
            for (var p = t; p != null; p = p.parent)
            {
                string n = p.name;
                if (n == "Port Name" || n == "Province" || n == "Flag" || n == "Lable")
                { r = true; break; }
                try { if (p.GetComponent<PortElement>() != null) { r = true; break; } } catch { }
            }
            if (markerCache.Count > 5000) markerCache.Clear();
            markerCache[t.Pointer] = r; return r;
        }
        static string Path(Transform t)
        {
            var s = new List<string>(); for (var p = t; p != null && s.Count < 6; p = p.parent) s.Insert(0, p.name);
            return string.Join("/", s);
        }

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("MapPortForces", true, null, "Campaign map: number of ships and tonnage in each port; foreign ports as far as intelligence reaches (fog of war)");
            Splashes = cat.CreateEntry("AlwaysShowSplashes", false, null, "Battle: shell splashes are shown at any distance and size (the game hides small or distant ones); costs some frame rate in large battles");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Ports] " + m); }

        static bool InPort(Ship s)
        {
            try
            {
                if (s == null || s.isSunk || s.PortLocation == null) return false;
                var stt = s.status;
                return stt == VesselEntity.Status.Normal || stt == VesselEntity.Status.Repairing || stt == VesselEntity.Status.Mothballed || stt == VesselEntity.Status.LowCrew || stt == VesselEntity.Status.Refit;
            }
            catch { return false; }
        }

        static void Recompute()
        {
            entries.Clear();
            Player main = null; var players = new List<Player>();
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p == null) continue; players.Add(p); if (p.isMain) main = p; } } catch { return; }
            if (main == null) return;
            var byPort = new Dictionary<IntPtr, Entry>(); var ports = new Dictionary<IntPtr, PortElement>(); var owner = new Dictionary<IntPtr, Player>();
            foreach (var p in players)
            {
                try
                {
                    var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var s = en.Current; if (!InPort(s)) continue;
                        var pe = s.PortLocation; var key = pe.Pointer;
                        if (!byPort.TryGetValue(key, out var e)) { e = new Entry { Key = key, Pos = pe.WorldCoord }; byPort[key] = e; ports[key] = pe; owner[key] = p; }
                        e.Ships++; try { e.Tons += s.Tonnage(); } catch { }
                    }
                }
                catch { }
            }
            int month = Fog.Month();
            foreach (var kv in byPort)
            {
                var e = kv.Value; var p = owner[kv.Key]; var pe = ports[kv.Key];
                bool friendly = p.Pointer == main.Pointer; try { if (!friendly) friendly = Fog.Friendly(main, p); } catch { }
                string kt(float t) => t >= 1000f ? $"{t / 1000f:0.#} kt" : $"{t:0} t";
                if (friendly) { e.Text = $"{e.Ships} | {kt(e.Tons)}"; e.Col = p.Pointer == main.Pointer ? new Color(0.75f, 1f, 0.75f) : new Color(0.75f, 0.9f, 1f); }
                else
                {
                    string id = ""; try { id = pe.ProvinceId; } catch { }
                    float q = Fog.PortQ(main, e.Pos, id);
                    e.Col = new Color(1f, 0.8f, 0.7f);
                    if (q < Fog.HiddenLimit) e.Text = "?";
                    else if (q < Fog.ExactLimit)
                    {
                        // error up to +-50% at the lower threshold, fixed for the month
                        float span = (Fog.ExactLimit - q) / (Fog.ExactLimit - Fog.HiddenLimit) * 0.5f;
                        float rs = (Fog.Roll($"pship|{month}|{id}") * 2f - 1f) * span, rt = (Fog.Roll($"pton|{month}|{id}") * 2f - 1f) * span;
                        int n = Mathf.Max(1, Mathf.RoundToInt(e.Ships * (1f + rs)));
                        float t = Mathf.Max(500f, e.Tons * (1f + rt)); t = t >= 10000f ? Mathf.Round(t / 5000f) * 5000f : Mathf.Round(t / 1000f) * 1000f;
                        e.Text = $"~{n} | ~{kt(t)}";
                    }
                    else e.Text = $"{e.Ships} | {kt(e.Tons)}";
                }
                entries.Add(e);
            }
        }

        internal static void OnGUI()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool map = false; try { map = GameManager.IsCampaign && GameManager.IsWorldMap && !GameManager.IsLoadingAny; } catch { }
                if (!map) return;
                if (Time.unscaledTime >= next) { next = Time.unscaledTime + 2f; Recompute(); }
                if (entries.Count == 0) return;
                var cam = Camera.main; if (cam == null) return;
                // 1.11.126: nothing while the pointer is over the game's interface (port window, tooltips, panels)
                UnityEngine.EventSystems.EventSystem es = null;
                try { es = UnityEngine.EventSystems.EventSystem.current; if (es != null && es.IsPointerOverGameObject()) return; } catch { }
                // labels under an open window (move ships, port, fleet...) are left out: the screen-space interface at the label
                // is checked every 0.3 s (OnGUI draws above the game's interface, so it has to stay away from it)
                bool recheck = Time.unscaledTime >= nextCover; if (recheck) nextCover = Time.unscaledTime + 0.5f;
                if (st == null) { st = new GUIStyle(); st.fontSize = 12; st.alignment = TextAnchor.MiddleCenter; st.richText = false; }
                float k = UiScale.F; if (k <= 0f) k = 1f;
                float W = UiScale.W, H = UiScale.H;
                // the game's fixed panels (fractions of the screen): top bar, minimap, right info panel, log, next-turn box
                var blocked = new[] {
                    new Rect(0f, 0f, W, H * 0.085f), new Rect(0f, 0f, W * 0.2f, H * 0.31f), new Rect(W * 0.815f, 0f, W * 0.185f, H * 0.66f),
                    new Rect(0f, H * 0.69f, W * 0.2f, H * 0.31f), new Rect(W * 0.815f, H * 0.85f, W * 0.185f, H * 0.15f) };
                var drawn = new List<Rect>();
                foreach (var e in entries)
                {
                    var sp = cam.WorldToScreenPoint(e.Pos); if (sp.z <= 0f) continue;
                    // below the port name (the game writes the name under the flag)
                    float x = sp.x / k, y = (Screen.height - sp.y) / k + 22f;
                    if (e.W <= 0f) e.W = Mathf.Max(48f, st.CalcSize(new GUIContent(e.Text)).x + 8f);
                    float w = e.W;
                    var r = new Rect(x - w / 2f, y, w, 16f);
                    if (r.xMax < 0f || r.yMax < 0f || r.x > W || r.y > H) continue;
                    bool skip = false;
                    foreach (var b in blocked) if (b.Overlaps(r)) { skip = true; break; }
                    // declutter: a label that would cover another one is left out (zoom in to see it)
                    if (!skip) foreach (var o in drawn) if (o.Overlaps(r)) { skip = true; break; }
                    if (!skip)
                    {
                        if (recheck || !covered.ContainsKey(e.Key)) covered[e.Key] = UnderUi(es, sp.x, sp.y - 22f * k - 8f * k);
                        skip = covered[e.Key];
                    }
                    if (skip) continue;
                    drawn.Add(r);
                    var old = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
                    st.normal.textColor = e.Col; GUI.Label(r, e.Text, st);
                }
            }
            catch (Exception ex) { if (errors++ < 5) Log("map labels: " + ex.Message); }
        }

        // ---------------------------------------------------------------- splashes
        static int forced;
        internal static void Visibility(ref bool __result)
        {
            if (__result || Splashes == null || !Splashes.Value) return;
            __result = true; if (forced++ == 0) Log("AlwaysShowSplashes: an effect the game would hide is shown");
        }
    }

    [HarmonyPatch(typeof(Effect), "VisibilityCheck")]
    internal static class Patch_AlwaysSplashes
    {
        private static void Postfix(ref bool __result) { PortForces.Visibility(ref __result); }
    }
}
