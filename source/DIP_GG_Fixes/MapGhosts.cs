// 1.11.234 (author's report 24.10, screenshot): ship parts - turrets, a hull - drawn in green at map scale on the campaign map
// (near Rosyth, Scapa Flow, the Bay of Biscay, Lisbon, Tunis), parts of ships that are not there. Parts the game keeps for
// reuse (Part.partsReuse) or builds for the AI generator / design clones live in the campaign scene; one that stays active
// without its ship, or outside its ship's hierarchy, is drawn where it is. On the campaign map every active Part is checked
// every 30 s: no ship, or not under its ship's transform, or its ship's object inactive -> the part is logged (name, ship,
// position, scale, visual mode, material) and its renderers are switched off. Battle and the designer are left alone.
// Switch: MapGhostParts (F10, Campaign).
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace DipGgFixes
{
    internal static class MapGhosts
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("MapGhostParts", true, null, "Campaign map: ship parts left in the scene without their ship (green turrets and hulls drawn at map scale) are logged and hidden");
        }
        static float next; static int errors, logged, hidden; static readonly HashSet<IntPtr> seen = new HashSet<IntPtr>();
        static HashSet<IntPtr> pending = new HashSet<IntPtr>();   // seen as a ghost once; hidden only when still a ghost on the next check (a part being placed in the designer is not touched)
        // 1.11.486 (stable fix of 1.2.75; beta 1.11.471): the check ran in the ship designer too and switched off the part previews of
        // the designer (parts without a ship under "Preview") - parts and groups could not be chosen from the bottom menu and a design
        // could be saved as a bare hull. Now nothing is checked in the designer or while loading, parts under a "Preview" object are never
        // touched, and renderers switched off on such parts before are switched back on.
        static readonly HashSet<IntPtr> hiddenParts = new HashSet<IntPtr>();
        static bool UnderPreview(Transform t) { for (var x = t; x != null; x = x.parent) { string n = ""; try { n = x.name; } catch { } if (n.IndexOf("Preview", StringComparison.OrdinalIgnoreCase) >= 0) return true; } return false; }
        static int restored;
        static void Restore()
        {
            if (hiddenParts.Count == 0) return;
            var objs = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Part>()); if (objs == null) return;
            for (int i = 0; i < objs.Length; i++)
            {
                var p = objs[i] != null ? objs[i].TryCast<Part>() : null; if (p == null || !hiddenParts.Contains(p.Pointer)) continue;
                Transform t = null; try { t = p.transform; } catch { } if (t == null || !UnderPreview(t)) continue;
                try { var rs = t.GetComponentsInChildren<Renderer>(true); if (rs != null) for (int k = 0; k < rs.Length; k++) if (rs[k] != null && !rs[k].enabled) { rs[k].enabled = true; restored++; } } catch { }
                hiddenParts.Remove(p.Pointer);
            }
            MelonLogger.Msg($"[DIP GG Fixes][MapGhosts] designer: renderers of part previews switched back on ({restored} so far)");
        }

        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            if (Time.unscaledTime < next) return; next = Time.unscaledTime + 30f;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (battle) return;
                bool designer = false; try { designer = GameManager.IsConstructor || GameManager.IsSharedDesignConstructor || GameManager.IsLoadingAny; } catch { }
                if (designer) { pending.Clear(); try { Restore(); } catch { } return; }   // 1.11.486
                bool campaign = false; try { campaign = CampaignController.Instance != null && CampaignController.Instance.CampaignData != null; } catch { }
                if (!campaign) return;
                var objs = UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<Part>()); if (objs == null) return;
                int checkedN = 0, ghosts = 0; var now = new HashSet<IntPtr>();
                for (int i = 0; i < objs.Length; i++)
                {
                    var p = objs[i] != null ? objs[i].TryCast<Part>() : null; if (p == null) continue;
                    checkedN++;
                    Ship ship = null; try { ship = p.ship; } catch { }
                    Transform t = null; try { t = p.transform; } catch { }
                    if (t == null) continue;
                    if (UnderPreview(t)) continue;   // 1.11.486: the designer's part previews
                    string why = null;
                    if (ship == null) why = "no ship";
                    else
                    {
                        GameObject sgo = null; try { sgo = ship.gameObject; } catch { }
                        if (sgo == null) why = "ship object gone";
                        else if (!sgo.activeInHierarchy) why = "ship object inactive";
                        else if (!t.IsChildOf(sgo.transform)) why = "outside its ship's hierarchy";
                    }
                    if (why == null) continue;
                    now.Add(p.Pointer);
                    if (!pending.Contains(p.Pointer)) continue;   // first sighting: wait for the next check
                    ghosts++;
                    if (seen.Add(p.Pointer) && logged++ < 20)
                    {
                        string mat = "?", mode = "?", shown = "?", parent = "none", dn = "?", sn = ship != null ? ship.name : "-";
                        try { dn = p.data != null ? p.data.name : "?"; } catch { }
                        try { mode = p.visualMode.ToString(); } catch { }
                        try { shown = p.isShown.ToString(); } catch { }
                        try { if (t.parent != null) parent = t.parent.name; } catch { }
                        try { var vr = p.visualRenderers; if (vr != null && vr.Count > 0 && vr[0] != null && vr[0].sharedMaterial != null) mat = vr[0].sharedMaterial.name; } catch { }
                        MelonLogger.Msg($"[DIP GG Fixes][MapGhosts] {dn} ({why}; ship {sn}): pos {t.position}, scale {t.lossyScale}, parent {parent}, visual mode {mode}, shown {shown}, material {mat} - renderers switched off");
                    }
                    hiddenParts.Add(p.Pointer);
                    try { var vr = p.visualRenderers; if (vr != null) for (int k = 0; k < vr.Count; k++) if (vr[k] != null && vr[k].enabled) { vr[k].enabled = false; hidden++; } } catch { }
                    try { var rs = t.GetComponentsInChildren<Renderer>(true); if (rs != null) for (int k = 0; k < rs.Length; k++) if (rs[k] != null && rs[k].enabled) { rs[k].enabled = false; hidden++; } } catch { }
                }
                pending = now;
                if (ghosts > 0 && logged <= 20) MelonLogger.Msg($"[DIP GG Fixes][MapGhosts] {ghosts} ghost part(s) of {checkedN} active parts on the map, {hidden} renderer(s) switched off so far");
            }
            catch (Exception e) { if (errors++ < 3) MelonLogger.Warning("[DIP GG Fixes][MapGhosts] " + e.Message); }
        }
    }
}
