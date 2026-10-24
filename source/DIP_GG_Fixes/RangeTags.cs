// 1.11.206 (author, 24.10): a small range tag under every identified enemy ship in battle - the distance from the ship you have selected
// (or from your flagship) in km, so the class labels on the horizon read "CL 6.5 km" at a glance. Unidentified contacts (fog of war)
// get no tag. Switch: EnemyRangeTags (F10, Battle).
using System;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class RangeTags
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("EnemyRangeTags", true, null, "Battle: a small tag under every identified enemy ship with its distance (km) from the ship you have selected");
        }
        static GUIStyle sTag; static int errors; static float next; static Ship from, lastOwn; static IntPtr battlePtr; static Il2CppSystem.Collections.Generic.List<Ship> enemies;

        internal static void OnGUI()
        {
            if (On == null || !On.Value || !GameManager.IsBattle) return;
            try
            {
                var cam = Camera.main; if (cam == null) return;
                if (sTag == null) { sTag = new GUIStyle(); sTag.fontSize = 12; sTag.alignment = TextAnchor.UpperCenter; sTag.normal.textColor = new Color(1f, 0.85f, 0.6f); }
                // the reference ship and the enemy list are refreshed twice a second, the positions every frame
                if (Time.unscaledTime > next)
                {
                    next = Time.unscaledTime + 0.5f; from = null; enemies = null;
                    var ui = G.ui; Ship sel = ui != null ? ui.selectedShipMain : null;
                    var battle = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (battle == null) return;
                    // 1.11.238 (author's report 24.10: "6.7 km" under a cruiser 0.4 km away): the remembered reference ship must belong to
                    //           this battle - after a new battle starts the old ship object still answers, from where it last stood
                    if (battle.Pointer != battlePtr) { battlePtr = battle.Pointer; lastOwn = null; }
                    Player me = null;
                    var players = CampaignController.Instance != null && CampaignController.Instance.CampaignData != null ? CampaignController.Instance.CampaignData.Players : null;
                    // 1.11.217 (author, 24.10): measured from the division you have selected (its leader); when an enemy ship is selected the
                    //           last own division stays the reference; nothing selected yet: your first division
                    if (sel != null && sel.player != null && sel.player.isMain && !sel.isDead && !sel.isSinking)
                    {
                        me = sel.player; from = sel; try { var ld = sel.division != null ? sel.division.leader : null; if (ld != null && !ld.isDead && !ld.isSinking) from = ld; } catch { }
                        lastOwn = from;
                    }
                    else if (lastOwn != null && !lastOwn.isDead && !lastOwn.isSinking && lastOwn.player != null) { from = lastOwn; me = lastOwn.player; }
                    else
                    {
                        if (players != null) for (int i = 0; i < players.Count; i++) if (players[i] != null && players[i].isMain) { me = players[i]; break; }
                        if (me != null) { var mine = battle.ShipsOf(me, true); if (mine != null) for (int i = 0; i < mine.Count; i++) { var s = mine[i]; if (s != null && !s.isDead && !s.isSinking) { from = s; break; } } }
                    }
                    if (me == null || from == null) return;
                    var op = battle.OppositePlayer(me); if (op == null) return;
                    enemies = battle.ShipsOf(op, true);
                }
                if (from == null || enemies == null) return;
                Vector3 fp = from.transform.position; fp.y = 0f;
                float W = UiScale.W, H = UiScale.H;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i]; if (e == null) continue;
                    bool gone = true; try { gone = e.isDead || e.isSinking; } catch { }
                    if (gone) continue;
                    bool vis = false; try { vis = e.IsVisibleFor(from); } catch { }
                    if (!vis) continue;
                    try { if (Fog.PlayerLabel(e) != null) continue; } catch { }   // unidentified contact - no range
                    Vector3 wp; try { wp = e.transform.position; } catch { continue; }
                    var p2 = wp; p2.y = 0f; float km = (p2 - fp).magnitude / 1000f;
                    var sp = cam.WorldToScreenPoint(wp + Vector3.up * 18f); if (sp.z <= 0f) continue;
                    var r = new Rect(sp.x / UiScale.F - 40f, H - sp.y / UiScale.F + 2f, 80f, 16f);
                    if (r.yMax < 0 || r.y > H || r.xMax < 0 || r.x > W) continue;
                    GUI.Label(r, km >= 10f ? $"{km:0} km" : $"{km:0.0} km", sTag);
                }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Battle] range tags: " + e.Message); }
        }
    }
}
