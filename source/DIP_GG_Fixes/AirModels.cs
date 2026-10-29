// 1.11.383 (#85, author 29.10 16:16-16:21: Sketchfab models of the period, "simplify them so they do not kill the game", "for now
// just use this model" - the Sopwith Camel (Red Baron Arcade) by NCTKM, CC BY 4.0): aircraft models read from
// Mods\IronAdmiral_Models\<name>.iam (+ PNG textures). The .iam files are made outside the game from the glTF originals by
// pm\models\glb2ia.py: node transforms baked, tiny parts dropped, every part simplified (fast quadric reduction), textures down to
// 512 px, double-sided materials given back faces. Layout: 'IAM1', materials (rgba sRGB, texture file), meshes (material, vertices,
// normals, uv, triangle indices), glTF coordinates in metres - converted here to Unity (x mirrored, winding reversed).
// Credits and licences: Mods\IronAdmiral_Models\CREDITS.txt.
using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace DipGgFixes
{
    internal static class AirModels
    {
        class Model { public List<(Mesh mesh, Material mat)> Parts = new List<(Mesh, Material)>(); public Bounds B; }
        static readonly Dictionary<string, Model> cache = new Dictionary<string, Model>();
        static readonly HashSet<string> failed = new HashSet<string>();
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AirModels] " + m); }
        static string Dir() { return Path.Combine(MelonUtils.GameDirectory, "Mods", "IronAdmiral_Models"); }

        static System.Reflection.MethodInfo loadImage;
        static Texture2D LoadTex(string file)
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (loadImage == null)
                {
                    Type ct = null; foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { ct = a.GetType("UnityEngine.ImageConversion"); if (ct != null) break; }
                    if (ct != null) foreach (var m in ct.GetMethods()) if (m.Name == "LoadImage" && m.GetParameters().Length == 2) { loadImage = m; break; }
                }
                if (loadImage == null) { Log("UnityEngine.ImageConversion.LoadImage not found - textures off"); return null; }
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(bytes.Length);
                for (int i = 0; i < bytes.Length; i++) arr[i] = bytes[i];
                loadImage.Invoke(null, new object[] { t, arr });
                t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4;
                return t;
            }
            catch (Exception e) { Log($"texture {Path.GetFileName(file)}: {e.Message}"); return null; }
        }

        static Model Load(string name)
        {
            if (cache.TryGetValue(name, out var m)) return m;
            if (failed.Contains(name)) return null;
            var path = Path.Combine(Dir(), name + ".iam");
            try
            {
                if (!File.Exists(path)) { failed.Add(name); Log("no model file " + path + " - the aircraft are drawn from simple solids"); return null; }
                using (var br = new BinaryReader(File.OpenRead(path)))
                {
                    if (new string(br.ReadChars(4)) != "IAM1") throw new Exception("not an IAM1 file");
                    int nm = br.ReadInt32(); var mats = new List<Material>();
                    var shader = Shader.Find("Standard");
                    for (int i = 0; i < nm; i++)
                    {
                        var col = new Color(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        int ln = br.ReadInt32(); var tex = ln > 0 ? System.Text.Encoding.UTF8.GetString(br.ReadBytes(ln)) : "";
                        var mat = new Material(shader); mat.color = col;
                        try { mat.SetFloat("_Glossiness", 0.2f); mat.SetFloat("_Metallic", 0f); } catch { }
                        if (tex != "") { var t = LoadTex(Path.Combine(Dir(), tex)); if (t != null) mat.mainTexture = t; }
                        mats.Add(mat);
                    }
                    int nme = br.ReadInt32(); m = new Model(); bool first = true; int tris = 0;
                    for (int k = 0; k < nme; k++)
                    {
                        int mi = br.ReadInt32(), vc = br.ReadInt32();
                        var v = new Vector3[vc]; var n = new Vector3[vc]; var uv = new Vector2[vc];
                        for (int i = 0; i < vc; i++) v[i] = new Vector3(-br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        for (int i = 0; i < vc; i++) n[i] = new Vector3(-br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        for (int i = 0; i < vc; i++) uv[i] = new Vector2(br.ReadSingle(), 1f - br.ReadSingle());   // glTF uv origin top-left
                        int ic = br.ReadInt32(); var idx = new int[ic];
                        for (int i = 0; i < ic; i += 3) { int a = br.ReadInt32(), b = br.ReadInt32(), c = br.ReadInt32(); idx[i] = a; idx[i + 1] = c; idx[i + 2] = b; }   // mirrored: reverse winding
                        var mesh = new Mesh(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        mesh.vertices = v; mesh.normals = n; mesh.uv = uv; mesh.triangles = idx; mesh.RecalculateBounds();
                        m.Parts.Add((mesh, mi >= 0 && mi < mats.Count ? mats[mi] : mats.Count > 0 ? mats[0] : new Material(shader)));
                        if (first) { m.B = mesh.bounds; first = false; } else m.B.Encapsulate(mesh.bounds);
                        tris += ic / 3;
                    }
                    cache[name] = m;
                    Log($"{name}: {m.Parts.Count} part(s), {tris} triangles, {nm} material(s), size {m.B.size.x:0.0} x {m.B.size.y:0.0} x {m.B.size.z:0.0} m");
                    return m;
                }
            }
            catch (Exception e) { failed.Add(name); Log($"{name}: {e.Message} - the aircraft are drawn from simple solids"); return null; }
        }

        // a new aircraft of model 'name' under 'parent', its centre at the parent's origin, nose along +z; null = no model
        internal static GameObject Spawn(string name, float scale)
        {
            var m = Load(name); if (m == null) return null;
            var root = new GameObject("IA_AirModel_" + name);
            var body = new GameObject("body"); body.transform.SetParent(root.transform, false);
            body.transform.localScale = Vector3.one * scale; body.transform.localPosition = -m.B.center * scale;
            foreach (var (mesh, mat) in m.Parts)
            {
                var g = new GameObject("part"); g.transform.SetParent(body.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            }
            return root;
        }
        internal static float Length(string name) { var m = Load(name); return m != null ? m.B.size.z : 0f; }
    }
}
