// 1.11.482 (author 30.10 22:00: stay on our own DIP/TAF base and take over what makes sense from DIP 1.3.6 HF1): the AI's war
// reparations. DIP 1.3.6 HF1 ships TweaksAndFixes 3.22.0 beta 4, whose changelog reads "AI no longer takes 10x-50x more war
// reparations than it should". This is that logic (TweaksAndFixes, NathanKell and DukeDagor, MIT licence - credited in NOTICE),
// rewritten for Iron Admiral on TweaksAndFixes 3.21.1: the winner takes, up to the value of the peace terms (funds), in this order -
// its own claimed home provinces, provinces it is invading, provinces it surrounds, provinces it borders (half of the neighbours or
// more), nearby islands, capital ships (most valuable first), colonies, then other ships; each item is then kept with a chance of
// take_preparation_chance (0.55), and the rest of the value is paid in money. Switch: AiReparationsLimit (off until tested).
using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class Reparations
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AiReparationsLimit", false, null, "Campaign: AI war reparations limited to the value of the peace terms - home claims, invaded, surrounded and bordering provinces, islands, capital ships, colonies, other ships, each kept with a 55% chance, the rest paid in money (TweaksAndFixes 3.22 logic; off until tested)");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][Reparations] " + m); }
        static float Inc(Province p) { try { return p.Income(); } catch { return 0f; } }
        static float Cost(Ship s) { try { return s.Cost(); } catch { return 0f; } }

        internal static bool Take(Player winner, Il2CppSystem.Collections.Generic.List<Ship> ships, Il2CppSystem.Collections.Generic.List<Province> provinces, float funds,
            out Il2CppSystem.Collections.Generic.List<Ship> takenShips, out Il2CppSystem.Collections.Generic.List<Province> takenProvinces, ref float result)
        {
            takenShips = new Il2CppSystem.Collections.Generic.List<Ship>();
            takenProvinces = new Il2CppSystem.Collections.Generic.List<Province>();
            if (On == null || !On.Value || winner == null) return true;
            try
            {
                var home = new List<Province>(); var surrounded = new List<Province>(); var invading = new List<Province>(); var bordering = new List<Province>();
                var islands = new List<Province>(); var colonies = new List<Province>(); var capital = new List<Ship>(); var other = new List<Ship>();
                for (int i = 0; provinces != null && i < provinces.Count; i++)
                {
                    var p = provinces[i]; if (p == null) continue;
                    bool isHome = false; try { isHome = p.IsHome; } catch { }
                    if (isHome) { bool claim = false; try { claim = p.ClaimPlayer == winner.data; } catch { } if (claim) home.Add(p); continue; }
                    // a province fought over by someone else than the winner is not handed over
                    bool fought = false, byWinner = false;
                    try
                    {
                        var en = ProvinceBattleManager.Battles.GetEnumerator();
                        while (en.MoveNext()) { var b = en.Current.Value; if (b != null && b.DefenderProvince == p) { fought = true; if (b.Attacker == winner) { byWinner = true; break; } } }
                    }
                    catch { }
                    if (fought && !byWinner) continue;
                    bool disc = false; try { disc = p.IsDiscounted(); } catch { }
                    if (disc) { invading.Add(p); continue; }
                    int nb = 0, own = 0;
                    try { var ns = p.NeighbourProvinces; nb = ns != null ? ns.Count : 0; for (int k = 0; k < nb; k++) if (ns[k] != null && ns[k].ControllerPlayer == winner) own++; } catch { }
                    if (nb == 0)
                    {
                        float near = float.MaxValue;
                        try { for (int k = 0; k < provinces.Count; k++) { var q = provinces[k]; if (q == null || q == p) continue; float d = Vector3.Distance(p.WorldCoord, q.WorldCoord); if (d < near) near = d; } } catch { }
                        if (near <= 10f) islands.Add(p); else colonies.Add(p);
                        continue;
                    }
                    if (own == nb) surrounded.Add(p); else if (own / (float)nb >= 0.49f) bordering.Add(p); else colonies.Add(p);
                }
                for (int i = 0; ships != null && i < ships.Count; i++)
                {
                    var s = ships[i]; if (s == null) continue;
                    bool busy = false; try { busy = s.isBuilding || s.isCommissioning || s.isRefit; } catch { }
                    if (busy) continue;
                    string t = ""; try { t = s.shipType.name; } catch { }
                    if (t == "bb" || t == "bc") capital.Add(s); else other.Add(s);
                }
                Comparison<Province> byInc = (a, b) => Inc(b).CompareTo(Inc(a)); Comparison<Ship> byCost = (a, b) => Cost(b).CompareTo(Cost(a));
                home.Sort(byInc); surrounded.Sort(byInc); bordering.Sort(byInc); islands.Sort(byInc); colonies.Sort(byInc); capital.Sort(byCost); other.Sort(byCost);
                float left = funds;
                var tp = new Il2CppSystem.Collections.Generic.List<Province>(); var ts = new Il2CppSystem.Collections.Generic.List<Ship>();
                void P(List<Province> l) { foreach (var p in l) { float v = Inc(p); if (v <= left) { left -= v; tp.Add(p); } } }
                void S(List<Ship> l) { foreach (var s in l) { float v = Cost(s); if (v <= left) { left -= v; ts.Add(s); } } }
                P(home); P(invading); P(surrounded); P(bordering); P(islands); S(capital); P(colonies); S(other);
                takenProvinces = tp; takenShips = ts;
                // each item kept with a chance; what is not taken is paid in money
                float chance = 0.55f; try { chance = G.GameData.parms.ContainsKey("take_preparation_chance") ? G.GameData.parms["take_preparation_chance"] : 0.55f; } catch { }
                left = funds;
                for (int i = takenProvinces.Count - 1; i >= 0; i--) { bool h = false; try { h = takenProvinces[i].IsHome; } catch { } if (!h && UnityEngine.Random.value > chance) takenProvinces.RemoveAt(i); else left -= Inc(takenProvinces[i]); }
                for (int i = takenShips.Count - 1; i >= 0; i--) { if (UnityEngine.Random.value > chance) takenShips.RemoveAt(i); else left -= Cost(takenShips[i]); }
                result = left;
                Log($"{winner.data.name}: terms worth {funds / 1e6f:0}M - {takenProvinces.Count} province(s), {takenShips.Count} ship(s), {left / 1e6f:0}M in money");
                return false;
            }
            catch (Exception e) { Log("failed, the game's own reparations used: " + e.Message); takenShips = new Il2CppSystem.Collections.Generic.List<Ship>(); takenProvinces = new Il2CppSystem.Collections.Generic.List<Province>(); return true; }
        }
    }

    internal static class Patch_AiTakeReparation
    {
        static HarmonyLib.Harmony H; static bool done;
        internal static void Install(HarmonyLib.Harmony h) { H = h; }
        internal static void Tick()
        {
            if (done || H == null || Reparations.On == null || !Reparations.On.Value) return;
            done = true;
            try { H.Patch(AccessTools.Method(typeof(WarPreparationWindow), "AiTakeReparation"), prefix: new HarmonyMethod(typeof(Patch_AiTakeReparation).GetMethod(nameof(Prefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))); MelonLogger.Msg("[DIP GG Fixes][Reparations] AI reparations limited (switch AiReparationsLimit)"); }
            catch (Exception e) { MelonLogger.Warning("[DIP GG Fixes][Reparations] not installed: " + e.Message); }
        }
        static bool Prefix(Player winner, Il2CppSystem.Collections.Generic.List<Ship> ships, Il2CppSystem.Collections.Generic.List<Province> provinces, float funds,
            out Il2CppSystem.Collections.Generic.List<Ship> takenShips, out Il2CppSystem.Collections.Generic.List<Province> takenProvinces, ref float __result)
        {
            return Reparations.Take(winner, ships, provinces, funds, out takenShips, out takenProvinces, ref __result);
        }
    }
}
