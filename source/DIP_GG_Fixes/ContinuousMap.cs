// 1.11.91 (prototype, off by default): campaign map without a left/right edge. The game already routes fleets across the
// map edge (Pathfinding.OtherSideOfMesh, split route lines; TAF wraps x < 0 by CampaignMap.mapWidth), only the camera
// stops at the edges. Here the camera wraps around horizontally and two display-only copies of the map plane are drawn
// left and right of the real one. Version 1: icons, labels and clicks stay on the real map only.
using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ContinuousMap
    {
        internal static MelonPreferences_Entry<bool> On;
        static GameObject left, right;
        static float minX, maxX, x0;
        static bool logged, wantWrap;
        static int errs;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ContinuousMap", false, null, "Campaign map without left/right edge: the camera wraps around and the map is drawn on both sides (prototype)");
        }

        static void Warn(string m) { if (errs++ < 5) MelonLogger.Warning("[DIP GG Fixes][Map] " + m); }

        static bool Bounds()
        {
            var m = CampaignMap.Instance; if (m == null || m.MapRenderer == null) return false;
            var b = m.MapRenderer.bounds; minX = b.min.x; maxX = b.max.x;
            return maxX - minX > 1f;
        }

        static GameObject Make(Renderer src, float dx)
        {
            var mf = src.GetComponent<MeshFilter>(); if (mf == null) { Warn("map renderer has no MeshFilter"); return null; }
            var go = new GameObject("IronAdmiral_MapWrap");
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localScale = src.transform.localScale;
            go.transform.rotation = src.transform.rotation;
            go.transform.position = src.transform.position + new Vector3(dx, 0f, 0f);
            go.layer = src.gameObject.layer;
            var f = go.AddComponent<MeshFilter>(); f.sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = src.sharedMaterials;
            return go;
        }

        // 1.11.101: the game covers the area left and right of the map with black planes ("Border", "BorderLeft", "BorderRight")
        // drawn above the map (y 1.1) - they hid the copies (22.10 test: black beyond the Pacific). They are hidden while the
        // continuous map is on and shown again when it is off.
        static readonly System.Collections.Generic.List<Renderer> sideBorders = new System.Collections.Generic.List<Renderer>();
        static void FindBorders()
        {
            if (sideBorders.Count > 0) return;
            try
            {
                var root = CampaignMap.Instance.MapRenderer.transform.root;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue; var n = r.gameObject.name; if (!n.StartsWith("Border")) continue;
                    var b = r.bounds;
                    if (b.min.x >= maxX - 1f || b.max.x <= minX + 1f) sideBorders.Add(r); // only the left/right ones, not top/bottom
                }
                MelonLogger.Msg($"[DIP GG Fixes][Map] {sideBorders.Count} side border plane(s) found");
            }
            catch (Exception e) { Warn("borders: " + e.Message); }
        }
        static void Show(bool v)
        {
            try { if (left != null) left.SetActive(v); if (right != null) right.SetActive(v); } catch { left = right = null; }
            try { foreach (var r in sideBorders) if (r != null) r.enabled = !v; } catch { sideBorders.Clear(); }
        }

        static void Ensure()
        {
            FindBorders();
            if (left != null && right != null) return;
            var src = CampaignMap.Instance.MapRenderer; float w = maxX - minX;
            left = Make(src, -w); right = Make(src, w);
            if (!logged)
            {
                logged = true;
                MelonLogger.Msg($"[DIP GG Fixes][Map] continuous map on: plane x {minX:0.00}..{maxX:0.00} (width {w:0.00}), CampaignMap.mapWidth {CampaignMap.mapWidth:0.00}");
                // 1.11.97: the copies stayed black in the first test (22.10) - list what draws the map
                try
                {
                    var root = src.transform.root;
                    var rs = root.GetComponentsInChildren<Renderer>(true);
                    int n = 0;
                    foreach (var r in rs)
                    {
                        if (r == null || n++ > 60) continue;
                        string sh = "?"; try { var m = r.sharedMaterial; sh = m != null ? m.name + "/" + (m.shader != null ? m.shader.name : "?") : "none"; } catch { }
                        var b = r.bounds;
                        MelonLogger.Msg($"[DIP GG Fixes][Map] renderer {r.gameObject.name} ({r.GetIl2CppType().Name}) enabled {r.enabled}/{r.gameObject.activeInHierarchy} layer {r.gameObject.layer} x {b.min.x:0.0}..{b.max.x:0.0} y {b.min.y:0.0}..{b.max.y:0.0} z {b.min.z:0.0}..{b.max.z:0.0} material {sh}{(r.Pointer == src.Pointer ? " <- MapRenderer" : "")}");
                    }
                    MelonLogger.Msg($"[DIP GG Fixes][Map] {rs.Length} renderers under {root.name}; copy left x {left?.GetComponent<Renderer>()?.bounds.min.x:0.0}..{left?.GetComponent<Renderer>()?.bounds.max.x:0.0}, right x {right?.GetComponent<Renderer>()?.bounds.min.x:0.0}..{right?.GetComponent<Renderer>()?.bounds.max.x:0.0}; camera {(Camera.main != null ? Camera.main.name + " cullingMask " + Camera.main.cullingMask + " far " + Camera.main.farClipPlane : "none")}");
                }
                catch (Exception e) { Warn("renderer list: " + e.Message); }
            }
        }

        // before the game's border check: remember where the camera wanted to go and whether to wrap it
        internal static void Before(Cam cam)
        {
            wantWrap = false;
            try
            {
                bool map = false; try { map = GameManager.IsWorldMap; } catch { }
                if (On == null || !On.Value || !map) { Show(false); return; }
                if (!Bounds()) return;
                Ensure(); Show(true);
                x0 = cam.transform.position.x; wantWrap = true;
            }
            catch (Exception e) { Warn("before: " + e.Message); }
        }

        // after the game's border check: keep its clamp on the vertical axis, put the horizontal position back, wrapped
        internal static void After(Cam cam)
        {
            if (!wantWrap) return;
            try
            {
                float w = maxX - minX, x = x0;
                while (x < minX) x += w;
                while (x >= maxX) x -= w;
                var p = cam.transform.position; p.x = x;
                cam.transform.position = p;
                try { cam.newCameraPos = new Vector3(x, cam.newCameraPos.y, p.z); } catch { }
            }
            catch (Exception e) { Warn("after: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Cam), "CheckCameraBorders")]
    internal static class Patch_ContinuousMapCam
    {
        static void Prefix(Cam __instance) { ContinuousMap.Before(__instance); }
        static void Postfix(Cam __instance) { ContinuousMap.After(__instance); }
    }
}
