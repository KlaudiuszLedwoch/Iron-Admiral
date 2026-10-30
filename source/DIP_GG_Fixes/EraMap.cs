// 1.11.485 (author 30.10 22:53: "change the map to a map of the game's time, 1890, and then replace it with a map fitting the year";
// 22:55 answers: look and content of the period's atlases, every 10 years, public domain maps of the period).
//   Mods\IronAdmiral_Maps\map_<decade>.png (map_1890.png, map_1900.png ... map_1940.png) replace the texture of the campaign map
//   (CampaignMap.MapRenderer) - the map of the newest decade not after the campaign year. The maps are old atlases of the period
//   (public domain) warped to the game's map projection outside the game; credits in Mods\IronAdmiral_Maps\CREDITS.txt.
//   No map files - nothing changes. The switch off gives the game's own map back.
//   EraMapDump (once): writes the shader, the texture properties and the game's own map textures to UserData\IronAdmiral\mapdump -
//   the reference for fitting the old maps to the game's projection; read only, the map itself is not touched.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class EraMap
    {
        internal static MelonPreferences_Entry<bool> On, Dump;
        internal static MelonPreferences_Entry<string> Prop;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("EraMap", true, null, "Campaign: the campaign map looks like an atlas of the period - a map of each decade (1890, 1900 ... 1940) from Mods\\IronAdmiral_Maps replaces the game's map texture as the years pass (no map files = the game's own map)");
            Dump = cat.CreateEntry("EraMapDump", true, null, "Campaign (once): write the campaign map's shader, texture properties and textures to UserData\\IronAdmiral\\mapdump - the reference for fitting the period maps (read only); switches itself off after the dump");
            Prop = cat.CreateEntry("EraMapTexture", "auto", null, "Campaign: texture property of the map material that the period map replaces (auto = the material's main texture)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][EraMap] " + m); }
        static string MapDir() { return Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiral_Maps"); }

        static Material mat; static Texture orig; static string origProp; static int shownDecade = -1; static float nextCheck;
        static readonly Dictionary<int, Texture2D> loaded = new Dictionary<int, Texture2D>();
        static readonly HashSet<int> failed = new HashSet<int>();

        internal static void Tick()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 2f;
            Renderer r = null;
            try { if (GameManager.IsBattle) return; var m = CampaignMap.Instance; if (m == null) return; r = m.MapRenderer; } catch { return; }
            if (r == null) return;
            Material cur = null; try { cur = r.sharedMaterial; } catch { }
            if (cur == null) return;
            if (mat == null || mat.Pointer != cur.Pointer) { mat = cur; orig = null; origProp = null; shownDecade = -1; }
            if (Dump != null && Dump.Value) { try { DoDump(r); } catch (Exception e) { Log("dump: " + e.Message); } Dump.Value = false; }

            string prop = PropName(); if (prop == null) return;
            if (orig == null) { try { orig = mat.GetTexture(prop); origProp = prop; } catch { } }
            int dec = -1;
            if (On != null && On.Value) dec = Decade(Fog.Year());
            if (dec == shownDecade) return;
            shownDecade = dec;
            if (dec < 0) { if (orig != null) { mat.SetTexture(origProp, orig); Log("the game's own map back"); } return; }
            var t = Get(dec);
            if (t == null) { if (orig != null) mat.SetTexture(origProp, orig); return; }
            mat.SetTexture(prop, t);
            Log($"campaign year {Fog.Year()}: map of the {dec}s ({t.width}x{t.height}) on the campaign map");
        }

        static string PropName()
        {
            string p = Prop != null ? (Prop.Value ?? "").Trim() : "auto";
            if (p.Length > 0 && p != "auto") return mat.HasProperty(p) ? p : null;
            foreach (var n in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo" }) if (mat.HasProperty(n)) return n;
            return null;
        }
        // the newest decade with a map file not after the year; -1 = none
        static int Decade(int year)
        {
            int best = -1;
            try
            {
                if (!Directory.Exists(MapDir())) return -1;
                for (int d = 1880; d <= 1950; d += 10)
                    if (d <= year && !failed.Contains(d) && File.Exists(Path.Combine(MapDir(), $"map_{d}.png"))) best = d;
            }
            catch { }
            return best;
        }
        static System.Reflection.MethodInfo loadImage, encodePng;
        static void Find()
        {
            if (loadImage != null && encodePng != null) return;
            Type ct = null; foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { ct = a.GetType("UnityEngine.ImageConversion"); if (ct != null) break; }
            if (ct == null) return;
            foreach (var m in ct.GetMethods())
            {
                if (m.Name == "LoadImage" && m.GetParameters().Length == 2) loadImage = m;
                if (m.Name == "EncodeToPNG" && m.GetParameters().Length == 1) encodePng = m;
            }
        }
        static Texture2D Get(int dec)
        {
            if (loaded.TryGetValue(dec, out var t) && t != null) return t;
            try
            {
                Find(); if (loadImage == null) { Log("UnityEngine.ImageConversion.LoadImage not found - period maps off"); failed.Add(dec); return null; }
                var bytes = File.ReadAllBytes(Path.Combine(MapDir(), $"map_{dec}.png"));
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> arr = bytes;
                t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                loadImage.Invoke(null, new object[] { t, arr });
                if (t.width <= 2) { Log($"map_{dec}.png could not be read"); failed.Add(dec); return null; }
                t.wrapMode = TextureWrapMode.Clamp; t.anisoLevel = 8; t.filterMode = FilterMode.Trilinear;
                UnityEngine.Object.DontDestroyOnLoad(t);
                loaded[dec] = t;
                return t;
            }
            catch (Exception e) { Log($"map_{dec}.png: {e.Message}"); failed.Add(dec); return null; }
        }

        // the reference for fitting: shader, properties, textures as PNG (through a render texture - the game's textures are not readable)
        static void DoDump(Renderer r)
        {
            string dir = Path.Combine(MelonUtils.UserDataDirectory, "IronAdmiral", "mapdump"); Directory.CreateDirectory(dir);
            var lines = new List<string>();
            var b = r.bounds;
            lines.Add($"renderer {r.name} bounds center {b.center} size {b.size}");
            try { var mf = r.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) lines.Add($"mesh {mf.sharedMesh.name} vertices {mf.sharedMesh.vertexCount}"); } catch { }
            try { lines.Add($"km per unit {CampaignMap.UnityDistanceToWorldKmMultiplier}"); } catch { }
            var mats = r.sharedMaterials;
            Find();
            for (int mi = 0; mi < mats.Length; mi++)
            {
                var m = mats[mi]; if (m == null) continue;
                lines.Add($"material {mi}: {m.name} shader {m.shader?.name}");
                string[] names = null; try { names = m.GetTexturePropertyNames(); } catch { }
                if (names == null) continue;
                foreach (var n in names)
                {
                    Texture tx = null; try { tx = m.GetTexture(n); } catch { }
                    if (tx == null) { lines.Add($"  {n}: none"); continue; }
                    Vector2 so = Vector2.one, of = Vector2.zero; try { so = m.GetTextureScale(n); of = m.GetTextureOffset(n); } catch { }
                    lines.Add($"  {n}: {tx.name} {tx.width}x{tx.height} {tx.GetType().Name} scale {so} offset {of}");
                    if (encodePng == null || tx.width < 64 || tx.width > 8192 || tx.height > 8192) continue;
                    try
                    {
                        int w = tx.width, h = tx.height;
                        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                        var prev = RenderTexture.active;
                        Graphics.Blit(tx, rt); RenderTexture.active = rt;
                        var t2 = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        t2.ReadPixels(new Rect(0, 0, w, h), 0, 0); t2.Apply();
                        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                        var png = encodePng.Invoke(null, new object[] { t2 }) as Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>;
                        if (png != null) { byte[] outB = png; File.WriteAllBytes(Path.Combine(dir, $"m{mi}{n}_{tx.name}.png".Replace(" ", "_")), outB); }
                        UnityEngine.Object.Destroy(t2);
                    }
                    catch (Exception e) { lines.Add($"    (not written: {e.Message})"); }
                }
            }
            File.WriteAllLines(Path.Combine(dir, "map_material.txt"), lines);
            Log($"map dump written to {dir} ({lines.Count} lines)");
        }
    }
}
