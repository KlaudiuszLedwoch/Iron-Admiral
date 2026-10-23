// 1.11.91 (prototype, off by default): campaign map without a left/right edge. The game already routes fleets across the
// map edge (Pathfinding.OtherSideOfMesh, split route lines; TAF wraps x < 0 by CampaignMap.mapWidth), only the camera
// stops at the edges. Here the camera wraps around horizontally and display-only copies of the map are drawn left and right.
// 1.11.133 (beta): the copies were drawn but stayed hidden (23.10: plain animated ocean beyond the date line). Rebuilt after
// the approach of UADVanillaPlus (map wrap): each copy gets its own instances of the map materials (same shader and
// properties), a render queue above the sea (>= 5000), and a small lift in height so the sea plane does not cover it;
// BorderLeft/BorderRight are switched off. Besides the map surface the copies now include the country colours (the
// borders manager's meshes, created a while after the map), the map grid and the area/province names. A side copy is only
// active while the view reaches that map edge. Port and fleet markers, routes and clicks still live on the real map only.
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ContinuousMap
    {
        internal static MelonPreferences_Entry<bool> On;
        const float MapLift = 0.052f, OverlayLift = 0.06f, LabelLift = 0.08f;
        const int MapQueue = 5000, OverlayQueue = 5100, LabelQueue = 5300;
        static readonly List<GameObject> all = new List<GameObject>();
        static readonly List<GameObject> lapLeft = new List<GameObject>(), lapRight = new List<GameObject>();
        static readonly List<Renderer> borders = new List<Renderer>();
        static IntPtr mapPtr; static bool built, overlays, leftOn, rightOn, logged; static int errs, nextOverlayFrame;
        static float x0; static bool wantWrap;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("ContinuousMap", false, null, "Campaign map without left/right edge: the camera wraps around and the map is drawn on both sides (test)");
        }
        static void Msg(string m) { MelonLogger.Msg("[DIP GG Fixes][Map] " + m); }
        static void Warn(string m) { if (errs++ < 8) MelonLogger.Warning("[DIP GG Fixes][Map] " + m); }

        static float Width()
        {
            try { if (CampaignMap.mapWidth > 0f) return CampaignMap.mapWidth; } catch { }
            try { return CampaignMap.Instance.MapRenderer.bounds.size.x; } catch { return 0f; }
        }

        static void Clear()
        {
            foreach (var g in all) { try { if (g != null) UnityEngine.Object.Destroy(g); } catch { } }
            all.Clear(); lapLeft.Clear(); lapRight.Clear(); borders.Clear();
            built = overlays = leftOn = rightOn = false;
        }

        static void Register(GameObject g, int lap)
        {
            all.Add(g); (lap < 0 ? lapLeft : lapRight).Add(g);
            g.SetActive(lap < 0 ? leftOn : rightOn);
        }

        static Material[] Clone(Material[] src, int minQueue)
        {
            var r = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) { var m = src[i]; if (m == null) continue; var c = new Material(m); c.renderQueue = Mathf.Max(m.renderQueue, minQueue); r[i] = c; }
            return r;
        }

        // a new object with the same mesh and copies of the materials, shifted by one map width and lifted a little
        static GameObject MeshCopy(Renderer src, int lap, string name, float lift, int queue)
        {
            var mf = src.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return null;
            var so = src.gameObject;
            var go = new GameObject(name); go.layer = so.layer;
            go.transform.SetParent(so.transform.parent, false);
            go.transform.localPosition = so.transform.localPosition; go.transform.localRotation = so.transform.localRotation; go.transform.localScale = so.transform.localScale;
            var p = go.transform.position; p.x += Width() * lap; p.y += lift; go.transform.position = p;
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.enabled = src.enabled;
            try { mr.sortingLayerID = src.sortingLayerID; mr.sortingOrder = src.sortingOrder; } catch { }
            mr.sharedMaterials = Clone(src.materials, queue);
            Register(go, lap);
            return go;
        }

        // Instantiate a whole layer (grid, names): only the text and the renderers stay active
        static void LayerCopy(GameObject src, int lap, string name, float lift, int queue)
        {
            var go = UnityEngine.Object.Instantiate(src, src.transform.parent);
            go.name = name;
            go.transform.localPosition = src.transform.localPosition; go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            var p = src.transform.position; p.x += Width() * lap; p.y += lift; go.transform.position = p;
            if (queue > 0)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue; var ms = r.materials;
                    foreach (var m in ms) if (m != null) m.renderQueue = Mathf.Max(m.renderQueue, queue);
                    r.materials = ms;
                }
            foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (b == null) continue; string t = ""; try { t = b.GetIl2CppType().FullName ?? ""; } catch { }
                if (t.Contains("TMPro") || t.Contains("TextMeshPro")) continue;
                b.enabled = false;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c != null) UnityEngine.Object.Destroy(c);
            Register(go, lap);
        }

        static void Build()
        {
            var map = CampaignMap.Instance; var src = map.MapRenderer;
            if (src.bounds.size.x <= 1f) return;
            if (src.GetComponent<MeshFilter>() == null) { Warn("map renderer has no mesh - continuous map not built"); return; }
            // the game's planes that cover the area beyond the left and right map edge
            var par = src.transform.parent;
            foreach (var n in new[] { "BorderLeft", "BorderRight" })
            {
                var t = par != null ? par.Find(n) : null; if (t == null) { Warn("border not found: " + n); continue; }
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) if (r != null) { borders.Add(r); r.enabled = false; }
            }
            MeshCopy(src, -1, "IronAdmiral_MapWrap_Left", MapLift, MapQueue);
            MeshCopy(src, 1, "IronAdmiral_MapWrap_Right", MapLift, MapQueue);
            try
            {
                var grid = map.transform.root.Find("WorldEx/MapVisualGrid");
                if (grid != null) { LayerCopy(grid.gameObject, -1, "IronAdmiral_MapWrap_GridL", 0f, 0); LayerCopy(grid.gameObject, 1, "IronAdmiral_MapWrap_GridR", 0f, 0); }
                else Warn("map grid not found");
            }
            catch (Exception e) { Warn("grid: " + e.Message); }
            try
            {
                if (map.LabelsAreaRoot != null) { LayerCopy(map.LabelsAreaRoot.gameObject, -1, "IronAdmiral_MapWrap_AreaL", LabelLift, LabelQueue); LayerCopy(map.LabelsAreaRoot.gameObject, 1, "IronAdmiral_MapWrap_AreaR", LabelLift, LabelQueue); }
                if (map.LabelsProvincesRoot != null) { LayerCopy(map.LabelsProvincesRoot.gameObject, -1, "IronAdmiral_MapWrap_ProvL", LabelLift, LabelQueue); LayerCopy(map.LabelsProvincesRoot.gameObject, 1, "IronAdmiral_MapWrap_ProvR", LabelLift, LabelQueue); }
            }
            catch (Exception e) { Warn("names: " + e.Message); }
            built = true;
            Msg($"continuous map built: width {Width():0.00}, map x {src.bounds.min.x:0.00}..{src.bounds.max.x:0.00}, {all.Count} copies, {borders.Count} edge renderers hidden");
        }

        // the country colours are separate meshes made a while after the map; copy them once their player materials exist
        static void Overlays()
        {
            if (overlays || Time.frameCount < nextOverlayFrame) return; nextOverlayFrame = Time.frameCount + 180;
            try
            {
                var bm = CampaignMap.Instance.bordersManager; if (bm == null || bm.Countries == null) return;
                bool ready = false;
                foreach (var c in bm.Countries)
                {
                    if (c == null || c.MeshObjects == null) continue;
                    for (int i = 0; i < c.MeshObjects.Count && !ready; i++)
                    {
                        var mr = c.MeshObjects[i]; if (mr == null) continue;
                        foreach (var m in mr.sharedMaterials) if (m != null && m.name.Replace(" (Instance)", "").StartsWith("player-", StringComparison.OrdinalIgnoreCase)) { ready = true; break; }
                    }
                    if (ready) break;
                }
                if (!ready) return;
                int n = 0;
                foreach (var c in bm.Countries)
                {
                    if (c == null || c.MeshObjects == null) continue;
                    for (int i = 0; i < c.MeshObjects.Count; i++)
                    {
                        var mr = c.MeshObjects[i]; if (mr == null) continue;
                        if (MeshCopy(mr, -1, "IronAdmiral_MapWrap_CountryL", OverlayLift, OverlayQueue) != null) n++;
                        if (MeshCopy(mr, 1, "IronAdmiral_MapWrap_CountryR", OverlayLift, OverlayQueue) != null) n++;
                    }
                }
                overlays = true; Msg($"continuous map: {n} country colour copies");
            }
            catch (Exception e) { overlays = true; Warn("country colours: " + e.Message); }
        }

        static void Sides(Cam cam)
        {
            bool l = false, r = false;
            try
            {
                var b = CampaignMap.Instance.MapRenderer.bounds; var c = cam.cameraComp;
                float half = c.orthographicSize * cam.screenSizeRation; float x = cam.transform.position.x;
                l = x - half <= b.min.x + 1.5f; r = x + half >= b.max.x - 1.5f;
            }
            catch { }
            if (l != leftOn) { leftOn = l; foreach (var g in lapLeft) if (g != null) g.SetActive(l); }
            if (r != rightOn) { rightOn = r; foreach (var g in lapRight) if (g != null) g.SetActive(r); }
        }

        // before the game's border check: remember where the camera wanted to go and whether to wrap it
        internal static void Before(Cam cam)
        {
            wantWrap = false;
            try
            {
                bool map = false; try { map = GameManager.IsWorldMap; } catch { }
                var m = CampaignMap.Instance;
                if (On == null || !On.Value || !map || m == null || m.MapRenderer == null)
                {
                    if (built && (On == null || !On.Value)) { foreach (var r in borders) if (r != null) r.enabled = true; Clear(); Msg("continuous map off - copies removed"); }
                    return;
                }
                if (m.Pointer != mapPtr) { mapPtr = m.Pointer; Clear(); }
                if (!built) Build();
                if (!built) return;
                Overlays();
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
                var b = CampaignMap.Instance.MapRenderer.bounds; float minX = b.min.x, w = b.max.x - b.min.x, x = x0;
                // no loops - a far-away or invalid camera position is left to the game
                if (w <= 1f || float.IsNaN(x) || float.IsInfinity(x) || Mathf.Abs(x) > 10f * w) { wantWrap = false; return; }
                x = minX + Mathf.Repeat(x - minX, w);
                var p = cam.transform.position; p.x = x; cam.transform.position = p;
                try { cam.newCameraPos = new Vector3(x, cam.newCameraPos.y, p.z); } catch { }
                Sides(cam);
                if (!logged) { logged = true; Msg("continuous map active"); }
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
