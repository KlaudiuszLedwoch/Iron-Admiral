// 1.11.144 (beta): continuous map, stage 2 - markers and clicks beyond the map edge. Ports, task forces and events (the game's
// map markers under MapUI.PortsRoot / ShipsRoot / EventsRoot) get a copy one map width to the left and to the right. The copies
// follow the original's position and look (sprites, colours, texts, lines) and pass clicks and hover to the original, so a
// port or a fleet seen beyond the date line can be opened and selected there. Map clicks on the side copies of the map are
// moved back onto the real map (CampaignMap.OnClickDetected), so orders given there reach the right place.
// Not yet: route lines beyond the edge (the route is drawn on the real map).
// (Approach taken from UADVanillaPlus, map wrap; reimplemented.)
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class ContinuousMapMarkers
    {
        const string Prefix = "IronAdmiral_WrapMarker_";
        class Vis
        {
            public Graphic[] SG, CG; public TMP_Text[] ST, CT; public LineRenderer[] SL, CL;
            public Vis(GameObject s, GameObject c)
            {
                SG = s.GetComponentsInChildren<Graphic>(true); CG = c.GetComponentsInChildren<Graphic>(true);
                ST = s.GetComponentsInChildren<TMP_Text>(true); CT = c.GetComponentsInChildren<TMP_Text>(true);
                SL = s.GetComponentsInChildren<LineRenderer>(true); CL = c.GetComponentsInChildren<LineRenderer>(true);
            }
        }
        class Set { public CampaignMapElement Src; public GameObject L, R; public Vis VL, VR; }
        static readonly Dictionary<IntPtr, Set> sets = new Dictionary<IntPtr, Set>();
        static int errors, frameClean; static bool logged;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Map] " + m); }
        static T Safe<T>(Func<T> f, T d) { try { return f(); } catch { return d; } }

        internal static void Clear()
        {
            foreach (var s in sets.Values) { try { if (s.L != null) UnityEngine.Object.Destroy(s.L); if (s.R != null) UnityEngine.Object.Destroy(s.R); } catch { } }
            sets.Clear();
        }

        static bool IsClone(GameObject g) { return g != null && g.name.StartsWith(Prefix, StringComparison.Ordinal); }
        static bool Under(Transform c, Transform p) { for (var t = c; t != null; t = t.parent) if (t == p) return true; return false; }

        // kind: 1 port, 2 task force, 3 event, 0 not copied
        static int Kind(CampaignMapElement e, MapUI ui)
        {
            if (Safe(() => e.TryCast<PortUI>() != null, false)) return Under(e.transform, ui.PortsRoot) ? 1 : 0;
            if (Safe(() => e.TryCast<ShipUI>() != null, false)) return Under(e.transform, ui.ShipsRoot) ? 2 : 0;
            if (Safe(() => e.TryCast<EventUI>() != null, false)) return Under(e.transform, ui.EventsRoot) ? 3 : 0;
            return 0;
        }

        // the game positioned a marker (CampaignMapElement / ShipUI.UpdatePositionScale): position its copies
        internal static void Sync(CampaignMapElement src, float scale)
        {
            if (!ContinuousMap.Enabled || src == null || IsClone(src.gameObject)) return;
            try
            {
                var map = CampaignMap.Instance; var ui = map != null ? map.UIMap : null; if (ui == null || ui.UICanvas == null) return;
                float w = ContinuousMap.MapWidth; if (w <= 0f) return;
                if (!sets.TryGetValue(src.Pointer, out var s) || s.L == null || s.R == null || s.Src == null)
                {
                    int kind = Kind(src, ui); if (kind == 0) return;
                    if (s != null) { if (s.L != null) UnityEngine.Object.Destroy(s.L); if (s.R != null) UnityEngine.Object.Destroy(s.R); }
                    s = new Set { Src = src, L = Make(src, kind, -w, "L"), R = Make(src, kind, w, "R") };
                    if (s.L == null || s.R == null) return;
                    s.VL = new Vis(src.gameObject, s.L); s.VR = new Vis(src.gameObject, s.R);
                    sets[src.Pointer] = s;
                    if (!logged) { logged = true; Log("continuous map: port, fleet and event markers copied beyond the map edge"); }
                }
                var wp = src.WorldPos;
                Place(s.L, s.VL, src, ui, wp, -w, scale, ContinuousMap.LapActive(-1));
                Place(s.R, s.VR, src, ui, wp, w, scale, ContinuousMap.LapActive(1));
                if (Time.frameCount - frameClean > 300) { frameClean = Time.frameCount; Clean(); }
            }
            catch (Exception e) { if (errors++ < 5) Log("markers: " + e.Message); }
        }

        static void Clean()
        {
            var dead = new List<IntPtr>();
            foreach (var kv in sets) if (kv.Value.Src == null || kv.Value.L == null || kv.Value.R == null) dead.Add(kv.Key);
            foreach (var k in dead) { var s = sets[k]; try { if (s.L != null) UnityEngine.Object.Destroy(s.L); if (s.R != null) UnityEngine.Object.Destroy(s.R); } catch { } sets.Remove(k); }
        }

        static void Place(GameObject c, Vis v, CampaignMapElement src, MapUI ui, Vector3 wp, float dx, float scale, bool lap)
        {
            if (c == null) return;
            bool on = lap && src.gameObject.activeSelf;
            if (c.activeSelf != on) c.SetActive(on);
            if (!on) return;
            wp.x += dx;
            var pos = ui.WorldToUISpace(ui.UICanvas, wp);
            var cs = Safe(() => c.GetComponent<ShipUI>(), null);
            if (cs != null) { try { var ss = src.TryCast<ShipUI>(); if (ss != null) cs.offset = ss.offset; } catch { } cs.UpdatePositionScale(pos, scale); }
            else { var ce = c.GetComponent<CampaignMapElement>(); if (ce != null) ce.UpdatePositionScale(pos, scale); }
            Look(v, dx);
        }

        static void Look(Vis v, float dx)
        {
            int n = Math.Min(v.SG.Length, v.CG.Length);
            for (int i = 0; i < n; i++)
            {
                var a = v.SG[i]; var b = v.CG[i]; if (a == null || b == null) continue;
                b.enabled = a.enabled; b.color = a.color;
                var ia = Safe(() => a.TryCast<Image>(), null); var ib = Safe(() => b.TryCast<Image>(), null);
                if (ia != null && ib != null) { ib.sprite = ia.sprite; ib.overrideSprite = ia.overrideSprite; ib.fillAmount = ia.fillAmount; }
                if (a.gameObject.activeSelf != b.gameObject.activeSelf) b.gameObject.SetActive(a.gameObject.activeSelf);
            }
            n = Math.Min(v.ST.Length, v.CT.Length);
            for (int i = 0; i < n; i++) { var a = v.ST[i]; var b = v.CT[i]; if (a == null || b == null) continue; b.enabled = a.enabled; if (b.text != a.text) b.text = a.text; b.color = a.color; }
            n = Math.Min(v.SL.Length, v.CL.Length);
            for (int i = 0; i < n; i++)
            {
                var a = v.SL[i]; var b = v.CL[i]; if (a == null || b == null) continue;
                b.enabled = a.enabled; b.positionCount = a.positionCount;
                for (int j = 0; j < a.positionCount; j++) { var p = a.GetPosition(j); if (a.useWorldSpace) p.x += dx; b.SetPosition(j, p); }
            }
        }

        static GameObject Make(CampaignMapElement src, int kind, float dx, string side)
        {
            if (src.transform.parent == null) return null;
            var g = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
            g.name = Prefix + side + "_" + src.gameObject.name;
            g.transform.SetSiblingIndex(src.transform.GetSiblingIndex());
            // a copy only draws: no game logic, no raycasts, no colliders - except its own button, which forwards to the original
            foreach (var gr in g.GetComponentsInChildren<Graphic>(true)) if (gr != null) gr.raycastTarget = false;
            foreach (var cg in g.GetComponentsInChildren<CanvasGroup>(true)) if (cg != null) { cg.interactable = false; cg.blocksRaycasts = false; }
            foreach (var b in g.GetComponentsInChildren<Behaviour>(true))
            {
                if (b == null) continue;
                if (Safe(() => b.TryCast<Graphic>() != null || b.TryCast<CanvasGroup>() != null, false)) continue;
                string t = Safe(() => b.GetIl2CppType().FullName ?? "", "");
                if (t.Contains("TMPro") || t.Contains("TextMeshPro") || t.Contains("Shadow") || t.Contains("Outline")) continue;
                b.enabled = false;
            }
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) if (c != null) UnityEngine.Object.Destroy(c);
            try
            {
                Button sb = null, cb = null;
                if (kind == 1) { sb = Safe(() => src.TryCast<PortUI>().PortButton, null); cb = Safe(() => g.GetComponent<PortUI>().PortButton, null); }
                else if (kind == 2) { sb = Safe(() => src.TryCast<ShipUI>().Btn, null); cb = Safe(() => g.GetComponent<ShipUI>().Btn, null); }
                else if (kind == 3) { sb = Safe(() => src.TryCast<EventUI>().Btn, null); cb = Safe(() => g.GetComponent<EventUI>().Btn, null); }
                if (cb == null) cb = g.GetComponentInChildren<Button>(true);
                if (sb != null && cb != null) Proxy(src, kind, sb, cb, g);
            }
            catch (Exception e) { if (errors++ < 5) Log("marker click: " + e.Message); }
            return g;
        }

        static void Proxy(CampaignMapElement src, int kind, Button sb, Button cb, GameObject g)
        {
            foreach (var cg in g.GetComponentsInChildren<CanvasGroup>(true)) if (cg != null) { cg.interactable = true; cg.blocksRaycasts = true; }
            cb.enabled = true; cb.interactable = true;
            if (cb.targetGraphic != null) cb.targetGraphic.raycastTarget = true;
            foreach (var gr in cb.GetComponentsInChildren<Graphic>(true)) if (gr != null) gr.raycastTarget = true;
            foreach (var x in cb.GetComponents<OnClickH>()) UnityEngine.Object.Destroy(x);
            foreach (var x in cb.GetComponents<OnEnter>()) UnityEngine.Object.Destroy(x);
            foreach (var x in cb.GetComponents<OnLeave>()) UnityEngine.Object.Destroy(x);
            cb.onClick.RemoveAllListeners();
            cb.onClick.AddListener((Action)(() => Click(src, kind, sb, null)));
            cb.gameObject.AddComponent<OnClickH>().action = (Action<PointerEventData>)(ev => Click(src, kind, sb, ev));
            cb.gameObject.AddComponent<OnEnter>().action = (Action)(() => { try { sb.GetComponent<OnEnter>()?.action?.Invoke(); } catch { } });
            cb.gameObject.AddComponent<OnLeave>().action = (Action)(() => { try { sb.GetComponent<OnLeave>()?.action?.Invoke(); } catch { } });
        }

        static void Click(CampaignMapElement src, int kind, Button sb, PointerEventData ev)
        {
            try
            {
                var h = sb.GetComponent<OnClickH>();
                if (h != null && h.action != null && ev != null) { h.action.Invoke(ev); return; }
                if (kind == 2 && ev != null) { try { src.TryCast<ShipUI>().OnPointerClick(ev); } catch { } }
                sb.onClick.Invoke();
            }
            catch (Exception e) { if (errors++ < 5) Log("marker click: " + e.Message); }
        }

        // a click on a side copy of the map: the point is moved onto the real map
        internal static void NormalizeClick(ref Vector3 p)
        {
            if (!ContinuousMap.Enabled) return;
            try
            {
                var b = CampaignMap.Instance.MapRenderer.bounds; float w = ContinuousMap.MapWidth; if (w <= 0f) return;
                if (p.x > b.max.x) p.x -= w; else if (p.x < b.min.x) p.x += w;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CampaignMapElement), "UpdatePositionScale")]
    internal static class Patch_WrapElementPosition
    {
        static void Postfix(CampaignMapElement __instance, float __1) { ContinuousMapMarkers.Sync(__instance, __1); }
    }

    [HarmonyPatch(typeof(ShipUI), "UpdatePositionScale")]
    internal static class Patch_WrapShipPosition
    {
        static void Postfix(ShipUI __instance, float __1) { ContinuousMapMarkers.Sync(__instance, __1); }
    }

    [HarmonyPatch(typeof(CampaignMap), "OnClickDetected")]
    internal static class Patch_WrapClick
    {
        static void Prefix(ref Vector3 __0) { ContinuousMapMarkers.NormalizeClick(ref __0); }
    }
}
