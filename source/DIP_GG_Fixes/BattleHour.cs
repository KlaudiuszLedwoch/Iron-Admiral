// 1.11.334 (#57, author 26.10, 20:43-20:45; decisions 28.10): the hour of a campaign battle is settled realistically. The side whose
// force is faster - the speed it can keep together, i.e. its slowest ship, at least BattleHourSpeedKn (3 kn) faster - AND better
// informed about the other (contact report quality: era optics and signalling, recon technology, intelligence budget, own scouts;
// at least BattleHourReconMargin better) chooses when to fight. The player chooses in a small window over the pre-battle screen
// (dawn, day, dusk, night - with what each means for the force); the AI chooses by its doctrine and the balance of forces
// (a night-seeking doctrine, or a weaker battle line with torpedo craft -> night; a stronger line -> dawn, a full day of light;
// a doctrine avoiding night -> day). With a large reconnaissance edge (BattleHourAmbushMargin) a dusk or night battle starts as an
// ambush: the chooser's ships open at about BattleHourAmbushKm (10 km) from the other force (Savo Island 1942, Coronel 1914).
// Neither side able to choose: the game's own hour stays. The mod's night rules apply as they are.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class BattleHour
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static MelonPreferences_Entry<float> SpeedKn, ReconMargin, AmbushMargin, AmbushKm;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BattleHourChoice", true, null, "Campaign battles: the faster and better informed side chooses the hour of the battle (you choose in a window over the battle screen, the AI by doctrine and balance of forces); with a large reconnaissance edge a dusk or night battle starts as an ambush at about 10 km");
            SpeedKn = cat.CreateEntry("BattleHourSpeedKn", 3f, null, "Campaign battles: knots by which a force (its slowest ship) must be faster to choose the hour of the battle");
            ReconMargin = cat.CreateEntry("BattleHourReconMargin", 0.15f, null, "Campaign battles: how much better (0-1) the contact report quality of the faster side must be to choose the hour");
            AmbushMargin = cat.CreateEntry("BattleHourAmbushMargin", 0.35f, null, "Campaign battles: reconnaissance edge (0-1) from which a dusk or night battle chosen by the faster side starts as an ambush");
            AmbushKm = cat.CreateEntry("BattleHourAmbushKm", 10f, null, "Campaign battles: opening distance (km) of an ambush");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][BattleHour] " + m); }

        static readonly string[] Names = { "Dawn", "Day", "Dusk", "Night" };
        static readonly DayCycleAndWeather.TimesOfDay[] Tod = { DayCycleAndWeather.TimesOfDay.Morning, DayCycleAndWeather.TimesOfDay.Day, DayCycleAndWeather.TimesOfDay.Evening, DayCycleAndWeather.TimesOfDay.Night };
        static readonly string[] Notes =
        {
            "a full day of light ahead: a long gun battle, the heavier battle line counts most",
            "good light from the start: gunnery decides, torpedo craft are seen early",
            "the light fails during the battle: it runs on into the night, flotillas get their chance",
            "darkness: your destroyers and torpedoes count more, the enemy's heavy guns count less",
        };
        class Decision { public string id; public bool playerChooses, aiChose; public int hour = -1; public bool ambush; public string why = ""; public bool applied; public string chooser = ""; public float edge; }
        static readonly Dictionary<string, Decision> decisions = new Dictionary<string, Decision>();
        static BattlePopup popup; static Decision current; static int errors;

        static float Kn(Ship s) { return BattleAi.Speed(s) * 1.94384f; }
        static float Sustained(Il2CppSystem.Collections.Generic.List<Ship> l)
        {
            float min = float.MaxValue;
            for (int i = 0; l != null && i < l.Count; i++) { var s = l[i]; if (s == null) continue; float v = Kn(s); if (v > 0.5f && v < min) min = v; }
            return min == float.MaxValue ? 0f : min;
        }
        static string Tn(Ship s) { try { return s.shipType != null ? s.shipType.name : "?"; } catch { return "?"; } }
        static float Recon(Player p, Il2CppSystem.Collections.Generic.List<Ship> own)
        {
            int light = 0; for (int i = 0; own != null && i < own.Count; i++) { var t = Tn(own[i]); if (t == "cl" || t == "dd" || t == "ca") light++; }
            float q = (0.25f + Fog.Era(Fog.Year()) * 0.5f) * Fog.TechF(p) * Fog.BudgetF(p) + 0.12f * Mathf.Min(4, light);
            return Mathf.Clamp01(q);
        }
        static float Line(Il2CppSystem.Collections.Generic.List<Ship> l) { float t = 0f; for (int i = 0; l != null && i < l.Count; i++) { var s = l[i]; if (s == null) continue; var n = Tn(s); if (n == "bb" || n == "bc" || n == "ca") { try { t += s.Tonnage(); } catch { } } } return t; }
        static int Flotilla(Il2CppSystem.Collections.Generic.List<Ship> l) { int n = 0; for (int i = 0; l != null && i < l.Count; i++) { var s = l[i]; if (s == null) continue; var t = Tn(s); bool torp = false; try { torp = s.haveTorpedoes; } catch { } if ((t == "dd" || t == "tb" || t == "cl") && torp) n++; } return n; }

        // the pre-battle screen opened (Fog.Post_BattlePopup)
        internal static void Popup(BattlePopup ui, CampaignBattle battle)
        {
            if (On == null || !On.Value || ui == null || battle == null) return;
            try
            {
                string id = battle.Id.ToString();
                popup = ui;
                if (decisions.TryGetValue(id, out current)) return;
                var d = new Decision { id = id }; decisions[id] = d; current = d;
                var att = battle.AttackerShips; var def = battle.DefenderShips;
                if (att == null || def == null || att.Count == 0 || def.Count == 0) return;
                Player pa = att[0] != null ? att[0].player : null, pd = def[0] != null ? def[0].player : null; if (pa == null || pd == null) return;
                float va = Sustained(att), vd = Sustained(def), ra = Recon(pa, att), rd = Recon(pd, def);
                int side = 0;
                if (va >= vd + SpeedKn.Value && ra >= rd + ReconMargin.Value) side = 1; else if (vd >= va + SpeedKn.Value && rd >= ra + ReconMargin.Value) side = -1;
                string an = pa.data != null ? pa.data.nameUi : "?", dn = pd.data != null ? pd.data.nameUi : "?";
                string facts = $"{an} {va:0.0} kn, reports {ra:0.00}; {dn} {vd:0.0} kn, reports {rd:0.00}";
                if (side == 0) { Log($"battle {id}: neither side can choose the hour ({facts}) - the game's hour stays"); return; }
                var chooser = side > 0 ? pa : pd; var cl = side > 0 ? att : def; var other = side > 0 ? def : att;
                d.chooser = chooser.data != null ? chooser.data.nameUi : "?"; d.edge = side > 0 ? ra - rd : rd - ra;
                bool main = false; try { main = chooser.isMain; } catch { }
                if (main) { d.playerChooses = true; Log($"battle {id}: the player's force is faster and better informed ({facts}) - the player chooses the hour"); return; }
                // AI: doctrine and balance of forces
                var doc = Doctrines.For(chooser); int night = doc != null ? doc.night : 0;
                float myLine = Line(cl), foeLine = Line(other); int myFlot = Flotilla(cl);
                int h; string why;
                if (night > 0) { h = 3; why = "its doctrine seeks a night action"; }
                else if (night < 0) { h = foeLine > myLine * 1.2f ? 2 : 1; why = "its doctrine avoids night action"; }
                else if (myLine < foeLine * 0.8f && myFlot >= 2) { h = 3; why = "its battle line is weaker, its torpedo craft strong"; }
                else if (myLine > foeLine * 1.2f) { h = 0; why = "its battle line is stronger - a full day of light"; }
                else { h = 1; why = "no reason to wait"; }
                d.hour = h; d.aiChose = true; d.why = why;
                d.ambush = h >= 2 && d.edge >= AmbushMargin.Value;
                Log($"battle {id}: {d.chooser} is faster and better informed ({facts}) and chose {Names[h]} - {why}{(d.ambush ? $"; ambush at about {AmbushKm.Value:0} km" : "")}");
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][BattleHour] popup: " + e.Message); }
        }

        static GUIStyle sT, sN;
        internal static void OnGUI()
        {
            if (current == null || popup == null) return;
            bool show = false; try { show = popup.gameObject.activeInHierarchy && !GameManager.IsBattle; } catch { }
            if (!show) return;
            if (!current.playerChooses && !current.aiChose) return;
            try
            {
                if (sT == null) { sT = new GUIStyle(); sT.fontSize = 14; sT.richText = true; sT.normal.textColor = Color.white; sN = new GUIStyle(); sN.fontSize = 11; sN.wordWrap = true; sN.normal.textColor = new Color(0.85f, 0.85f, 0.85f); }
                float w = 520f, h = current.playerChooses ? 196f : 64f; float x = (UiScale.W - w) / 2f, y = 12f;
                GUI.Box(new Rect(x, y, w, h), ""); GUI.Box(new Rect(x, y, w, h), "");
                if (current.aiChose)
                {
                    GUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 20f), $"<b>Hour of battle:</b> {Names[current.hour]}{(current.ambush ? " - ambush" : "")}", sT);
                    GUI.Label(new Rect(x + 10f, y + 28f, w - 20f, 32f), $"{current.chooser} is faster and better informed and chose the hour: {current.why}.", sN);
                    return;
                }
                string sel = current.hour >= 0 ? Names[current.hour] + (current.ambush ? " - ambush" : "") : "the game's hour";
                GUI.Label(new Rect(x + 10f, y + 6f, w - 20f, 20f), $"<b>Your force is faster and better informed - choose the hour</b> (now: {sel})", sT);
                for (int i = 0; i < 4; i++)
                {
                    var r = new Rect(x + 10f, y + 30f + i * 36f, 90f, 30f);
                    var old = GUI.backgroundColor; if (current.hour == i) GUI.backgroundColor = new Color(0.35f, 1f, 0.45f);
                    if (GUI.Button(r, Names[i])) { current.hour = i; current.ambush = i >= 2 && current.edge >= AmbushMargin.Value; Log($"battle {current.id}: the player chose {Names[i]}{(current.ambush ? " (ambush)" : "")}"); }
                    GUI.backgroundColor = old;
                    GUI.Label(new Rect(x + 108f, y + 32f + i * 36f, w - 118f, 30f), Notes[i] + (i >= 2 && current.edge >= AmbushMargin.Value ? " - your scouting is good enough for an ambush at about " + AmbushKm.Value.ToString("0") + " km" : ""), sN);
                }
                if (GUI.Button(new Rect(x + w - 130f, y + h - 28f, 120f, 22f), "Leave to chance")) { current.hour = -1; current.ambush = false; }
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][BattleHour] window: " + e.Message); }
        }

        // in battle: set the chosen hour and the ambush distance once the ships are there
        static float since = -1f;
        internal static void Tick()
        {
            if (On == null || !On.Value) return;
            try
            {
                bool battle = false; try { battle = GameManager.IsBattle; } catch { }
                if (!battle) { since = -1f; return; }
                var b = BattleManager.Instance != null ? BattleManager.Instance.CurrentBattle : null; if (b == null) return;
                Decision d; if (!decisions.TryGetValue(b.Id.ToString(), out d) || d.applied || d.hour < 0) return;
                if (since < 0f) since = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - since < 2f) return;
                d.applied = true;
                try { var dc = DayCycleAndWeather.Instance(); if (dc != null) dc.SetDaytime(Tod[d.hour], true); Log($"battle {d.id}: hour set to {Names[d.hour]} ({Tod[d.hour]})"); }
                catch (Exception e) { Log("setting the hour failed: " + e.Message); }
                if (d.ambush) Ambush(b, d);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][BattleHour] tick: " + e.Message); }
        }

        // the chooser's ships are moved so that their nearest ship opens at about AmbushKm from the other force, same formation
        static void Ambush(CampaignBattle b, Decision d)
        {
            try
            {
                var ships = b.Ships; if (ships == null) return;
                var mine = new List<Ship>(); var them = new List<Ship>();
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null || s.player == null) continue;
                    string n = s.player.data != null ? s.player.data.nameUi : "?";
                    if (n == d.chooser) mine.Add(s); else them.Add(s);
                }
                if (mine.Count == 0 || them.Count == 0) return;
                Vector3 cm = Vector3.zero, ct = Vector3.zero; foreach (var s in mine) cm += s.transform.position; foreach (var s in them) ct += s.transform.position; cm /= mine.Count; ct /= them.Count;
                var dir = cm - ct; dir.y = 0f; float dist = dir.magnitude; if (dist < 1f) return;
                float want = AmbushKm.Value * 1000f;
                if (dist <= want) { Log($"battle {d.id}: ambush - the forces are already {dist / 1000f:0.0} km apart"); return; }
                var shift = -dir.normalized * (dist - want);
                foreach (var s in mine)
                {
                    var np = s.transform.position + shift; np.y = s.transform.position.y;
                    s.transform.position = np;
                    try { var rb = s.GetComponent<Rigidbody>(); if (rb != null) { rb.position = np; rb.velocity = Vector3.zero; } } catch { }
                }
                Log($"battle {d.id}: ambush - {d.chooser}'s {mine.Count} ship(s) moved from {dist / 1000f:0.0} km to about {want / 1000f:0} km of the other force");
            }
            catch (Exception e) { Log("ambush failed: " + e.Message); }
        }
    }
}
