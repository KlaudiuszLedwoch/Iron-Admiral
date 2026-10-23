// 1.11.120 (beta): on the campaign map every port with ships lying in it shows how many ships and how much tonnage are there.
// Own and allied ports are shown exactly; foreign ports follow the fog of war (Fog.PortQ): unknown ("?") when intelligence is
// poor, an estimate ("~") when it is partial, exact when it is good. The estimate is fixed for the month, so it does not
// flicker. Switch: MapPortForces (F10).
// 1.11.120 (beta): AlwaysShowSplashes - shell splashes in battle are drawn at any distance and size (the game skips effects it
// judges too small or too far away: Effect.VisibilityCheck); costs some frame rate in big battles.
// 1.11.131 (beta): the figures are written into the game's own port name label (PortUI.PortName) instead of being drawn on top
// of the screen: they keep the size of the port name, never flicker with the cursor, stay under the game's windows and the mod's
// windows, and follow the map wherever the game shows the port. (Earlier versions drew them with OnGUI, which always lies above
// every window; 1.11.126-130 tried to keep them clear of the interface.)
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class PortForces
    {
        internal static MelonPreferences_Entry<bool> On, Splashes;
        const string Mark = " <size=80%><color=#";
        class Entry { public int Ships; public float Tons; public string Text; public string Col; }
        class Label { public TMP_Text T; public string Base; public string Applied; }
        static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>();
        static readonly Dictionary<string, Label> labels = new Dictionary<string, Label>();
        static IntPtr cachedRoot; static float next, nextCache; static int errors; static bool shown;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("MapPortForces", true, null, "Campaign map: number of ships and tonnage in each port, next to the port name; foreign ports as far as intelligence reaches (fog of war)");
            Splashes = cat.CreateEntry("AlwaysShowSplashes", false, null, "Battle: shell splashes are shown at any distance and size (the game hides small or distant ones); costs some frame rate in large battles");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Ports] " + m); }
        internal static void OnGUI() { } // kept for the registration in Mod.cs (nothing is drawn with OnGUI any more)

        static bool InPort(Ship s)
        {
            try
            {
                if (s == null || s.isSunk || s.isDesign || s.PortLocation == null) return false;
                var stt = s.status;
                return stt == VesselEntity.Status.Normal || stt == VesselEntity.Status.Repairing || stt == VesselEntity.Status.Mothballed || stt == VesselEntity.Status.LowCrew || stt == VesselEntity.Status.Refit;
            }
            catch { return false; }
        }

        static void Recompute()
        {
            byId.Clear();
            Player main = null; var players = new List<Player>();
            try { var ps = CampaignController.Instance.CampaignData.Players; for (int i = 0; i < ps.Count; i++) { var p = ps[i]; if (p == null) continue; players.Add(p); if (p.isMain) main = p; } } catch { return; }
            if (main == null) return;
            var ports = new Dictionary<string, PortElement>(); var owner = new Dictionary<string, Player>();
            foreach (var p in players)
            {
                try
                {
                    var en = p.fleet.GetEnumerator(); var un = en.Cast<Il2CppSystem.Collections.IEnumerator>();
                    while (un.MoveNext())
                    {
                        var s = en.Current; if (!InPort(s)) continue;
                        var pe = s.PortLocation; string id = null; try { id = pe.Id; } catch { }
                        if (string.IsNullOrEmpty(id)) continue;
                        if (!byId.TryGetValue(id, out var e)) { e = new Entry(); byId[id] = e; ports[id] = pe; owner[id] = p; }
                        e.Ships++; try { e.Tons += s.Tonnage(); } catch { }
                    }
                }
                catch { }
            }
            int month = Fog.Month();
            foreach (var kv in byId)
            {
                var e = kv.Value; var p = owner[kv.Key]; var pe = ports[kv.Key];
                bool own = p.Pointer == main.Pointer; bool friendly = own; try { if (!friendly) friendly = Fog.Friendly(main, p); } catch { }
                string kt(float t) => t >= 1000f ? $"{t / 1000f:0.#} kt" : $"{t:0} t";
                if (friendly) { e.Text = $"{e.Ships} | {kt(e.Tons)}"; e.Col = own ? "1E6B1E" : "1F4F8F"; }
                else
                {
                    string pid = ""; try { pid = pe.ProvinceId; } catch { }
                    Vector3 pos = Vector3.zero; try { pos = pe.WorldCoord; } catch { }
                    float q = Fog.PortQ(main, pos, pid);
                    e.Col = "9A3412";
                    if (q < Fog.HiddenLimit) e.Text = "?";
                    else if (q < Fog.ExactLimit)
                    {
                        // error up to +-50% at the lower threshold, fixed for the month
                        float span = (Fog.ExactLimit - q) / (Fog.ExactLimit - Fog.HiddenLimit) * 0.5f;
                        float rs = (Fog.Roll($"pship|{month}|{pid}") * 2f - 1f) * span, rt = (Fog.Roll($"pton|{month}|{pid}") * 2f - 1f) * span;
                        int n = Mathf.Max(1, Mathf.RoundToInt(e.Ships * (1f + rs)));
                        float t = Mathf.Max(500f, e.Tons * (1f + rt)); t = t >= 10000f ? Mathf.Round(t / 5000f) * 5000f : Mathf.Round(t / 1000f) * 1000f;
                        e.Text = $"~{n} | ~{kt(t)}";
                    }
                    else e.Text = $"{e.Ships} | {kt(e.Tons)}";
                }
            }
        }

        static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            int i = s.IndexOf(Mark, StringComparison.Ordinal); return i >= 0 ? s.Substring(0, i) : s;
        }

        static void Cache(MapUI ui)
        {
            var root = ui.PortsRoot; IntPtr rp = root != null ? root.Pointer : IntPtr.Zero;
            if (rp == cachedRoot && labels.Count > 0 && Time.realtimeSinceStartup < nextCache) return;
            cachedRoot = rp; nextCache = Time.realtimeSinceStartup + 30f;
            var keep = new Dictionary<string, Label>(labels); labels.Clear();
            if (root == null) return;
            var arr = root.GetComponentsInChildren<PortUI>(true);
            foreach (var pu in arr)
            {
                try
                {
                    if (pu == null || pu.PortName == null || string.IsNullOrEmpty(pu.Id)) continue;
                    Label l; if (!keep.TryGetValue(pu.Id, out l) || l.T == null || l.T.Pointer != pu.PortName.Pointer) l = new Label { T = pu.PortName, Base = Strip(pu.PortName.text) };
                    labels[pu.Id] = l;
                }
                catch { }
            }
        }

        // MapUI.LateUpdate postfix: refresh every 2 s
        internal static void Tick(MapUI ui)
        {
            if (On == null) return;
            if (Time.realtimeSinceStartup < next) return; next = Time.realtimeSinceStartup + 2f;
            try
            {
                bool map = false; try { map = GameManager.IsCampaign && !GameManager.IsLoadingAny; } catch { }
                if (!map || ui == null) return;
                if (!On.Value) { if (shown) { Cache(ui); Apply(false); shown = false; } return; }
                Recompute(); Cache(ui); Apply(true); shown = true;
            }
            catch (Exception ex) { if (errors++ < 5) Log("port labels: " + ex.Message); }
        }

        static int applied;
        static void Apply(bool show)
        {
            int n = 0;
            foreach (var kv in labels)
            {
                var l = kv.Value; if (l.T == null) continue;
                string cur = l.T.text ?? "";
                if (cur != l.Applied) l.Base = Strip(cur); // the game renamed the port (new owner) - take its text as the base
                string want = l.Base;
                if (show && byId.TryGetValue(kv.Key, out var e) && e.Text != null) { want = l.Base + Mark + e.Col + ">" + e.Text + "</color></size>"; n++; }
                if (cur != want) { l.T.richText = true; l.T.text = want; }
                l.Applied = want;
            }
            if (show && applied++ == 0) Log($"port labels: {n} of {labels.Count} ports show ships in port");
        }

        // ---------------------------------------------------------------- splashes
        static int forced;
        internal static void Visibility(ref bool __result)
        {
            if (__result || Splashes == null || !Splashes.Value) return;
            __result = true; if (forced++ == 0) Log("AlwaysShowSplashes: an effect the game would hide is shown");
        }
    }

    [HarmonyPatch(typeof(MapUI), "LateUpdate")]
    internal static class Patch_PortForcesLabels
    {
        private static void Postfix(MapUI __instance) { PortForces.Tick(__instance); }
    }

    [HarmonyPatch(typeof(Effect), "VisibilityCheck")]
    internal static class Patch_AlwaysSplashes
    {
        private static void Postfix(ref bool __result) { PortForces.Visibility(ref __result); }
    }
}
