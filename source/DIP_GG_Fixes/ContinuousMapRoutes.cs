// 1.11.310 (beta, #11): continuous map, stage 3 - fleet route lines beyond the map edge. The game draws every route
// (MapUI.routes: Route with RouteLine, AdditionalLine for the part across the edge, and the destination marker) on the real
// map only. Each route gets a display-only copy one map width to the left and to the right; the copies follow the original's
// line points, colours, materials and visibility every frame while that side of the map is in view, sit a little above the
// map copies (height and render queue between the country colours and the names) and take no clicks.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class ContinuousMapRoutes
    {
        const string Prefix = "IronAdmiral_WrapRoute_";
        const float Lift = 0.07f; const int Queue = 5200;
        class Copy { public GameObject G; public Transform[] ST, CT; public Renderer[] SR, CR; public LineRenderer[] SL, CL; }
        class Set { public Route Src; public Copy L, R; }
        static readonly Dictionary<IntPtr, Set> sets = new Dictionary<IntPtr, Set>();
        static readonly Dictionary<IntPtr, Material> mats = new Dictionary<IntPtr, Material>();
        static int errors, frameClean; static bool logged;
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Map] " + m); }

        static void Kill(Copy c) { try { if (c != null && c.G != null) UnityEngine.Object.Destroy(c.G); } catch { } }

        internal static void Clear()
        {
            foreach (var s in sets.Values) { Kill(s.L); Kill(s.R); }
            sets.Clear();
            foreach (var m in mats.Values) { try { if (m != null) UnityEngine.Object.Destroy(m); } catch { } }
            mats.Clear();
        }

        // called once per camera update while the continuous map is on
        internal static void Sync()
        {
            if (!ContinuousMap.Enabled) return;
            try
            {
                var map = CampaignMap.Instance; if (map == null) return;
                var ui = map.UIMap; if (ui == null) return;
                var list = ui.routes; if (list == null) return;
                float w = ContinuousMap.MapWidth; if (w <= 0f) return;
                bool l = ContinuousMap.LapActive(-1), r = ContinuousMap.LapActive(1);
                for (int i = 0; i < list.Count; i++)
                {
                    var src = list[i]; if (src == null) continue;
                    if (!sets.TryGetValue(src.Pointer, out var s) || s.Src == null || s.L == null || s.L.G == null || s.R == null || s.R.G == null)
                    {
                        if (!l && !r) continue;   // copies are made only when an edge is in view
                        if (s != null) { Kill(s.L); Kill(s.R); }
                        s = new Set { Src = src, L = Make(src, "L"), R = Make(src, "R") };
                        if (s.L == null || s.R == null) { Kill(s.L); Kill(s.R); continue; }
                        sets[src.Pointer] = s;
                        if (!logged) { logged = true; Log("continuous map: fleet routes copied beyond the map edge"); }
                    }
                    Place(s.L, src, -w, l); Place(s.R, src, w, r);
                }
                if (Time.frameCount - frameClean > 120) { frameClean = Time.frameCount; Clean(list); }
            }
            catch (Exception e) { if (errors++ < 5) Log("routes: " + e.Message); }
        }

        static void Clean(Il2CppSystem.Collections.Generic.List<Route> list)
        {
            var live = new HashSet<IntPtr>();
            for (int i = 0; i < list.Count; i++) { var x = list[i]; if (x != null) live.Add(x.Pointer); }
            var dead = new List<IntPtr>();
            foreach (var kv in sets)
            {
                var s = kv.Value; bool bad = s.Src == null || !live.Contains(kv.Key);
                // the game changed the route's children (pooled parts): make the copies again
                if (!bad) { try { bad = s.Src.GetComponentsInChildren<Transform>(true).Length != s.L.ST.Length; } catch { bad = true; } }
                if (bad) dead.Add(kv.Key);
            }
            foreach (var k in dead) { var s = sets[k]; Kill(s.L); Kill(s.R); sets.Remove(k); }
        }

        static Copy Make(Route src, string side)
        {
            if (src.transform.parent == null) return null;
            var g = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
            g.name = Prefix + side + "_" + src.gameObject.name;
            // a copy only draws: no game logic and no colliders
            foreach (var b in g.GetComponentsInChildren<MonoBehaviour>(true)) if (b != null) b.enabled = false;
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) if (c != null) UnityEngine.Object.Destroy(c);
            var cp = new Copy
            {
                G = g,
                ST = src.GetComponentsInChildren<Transform>(true), CT = g.GetComponentsInChildren<Transform>(true),
                SR = src.GetComponentsInChildren<Renderer>(true), CR = g.GetComponentsInChildren<Renderer>(true),
                SL = src.GetComponentsInChildren<LineRenderer>(true), CL = g.GetComponentsInChildren<LineRenderer>(true)
            };
            g.SetActive(false);
            return cp;
        }

        // a copy of the game's material drawn above the map copies (properties refreshed on every use - colours follow the game)
        static Material Mat(Material m)
        {
            if (m == null) return null;
            if (!mats.TryGetValue(m.Pointer, out var c) || c == null) { c = new Material(m); mats[m.Pointer] = c; }
            else c.CopyPropertiesFromMaterial(m);
            c.renderQueue = Mathf.Max(m.renderQueue, Queue);
            return c;
        }

        static void Place(Copy c, Route src, float dx, bool lap)
        {
            bool on = lap && src.gameObject.activeInHierarchy;
            if (c.G.activeSelf != on) c.G.SetActive(on);
            if (!on) return;
            var t = c.G.transform; var st = src.transform;
            var p = st.position; p.x += dx; p.y += Lift; t.position = p; t.rotation = st.rotation; t.localScale = st.localScale;
            int n = Math.Min(c.ST.Length, c.CT.Length);
            for (int i = 1; i < n; i++)
            {
                var a = c.ST[i]; var b = c.CT[i]; if (a == null || b == null) continue;
                if (b.gameObject.activeSelf != a.gameObject.activeSelf) b.gameObject.SetActive(a.gameObject.activeSelf);
                b.localPosition = a.localPosition; b.localRotation = a.localRotation; b.localScale = a.localScale;
            }
            n = Math.Min(c.SR.Length, c.CR.Length);
            for (int i = 0; i < n; i++)
            {
                var a = c.SR[i]; var b = c.CR[i]; if (a == null || b == null) continue;
                b.enabled = a.enabled; if (!a.enabled) continue;
                var sm = a.sharedMaterials; var cm = new Material[sm.Length];
                for (int j = 0; j < sm.Length; j++) cm[j] = Mat(sm[j]);
                b.sharedMaterials = cm;
            }
            n = Math.Min(c.SL.Length, c.CL.Length);
            for (int i = 0; i < n; i++)
            {
                var a = c.SL[i]; var b = c.CL[i]; if (a == null || b == null || !a.enabled) continue;
                b.useWorldSpace = a.useWorldSpace; b.loop = a.loop;
                b.widthMultiplier = a.widthMultiplier; b.startColor = a.startColor; b.endColor = a.endColor;
                int k = a.positionCount; b.positionCount = k;
                for (int j = 0; j < k; j++) { var q = a.GetPosition(j); if (a.useWorldSpace) { q.x += dx; q.y += Lift; } b.SetPosition(j, q); }
            }
        }
    }
}
