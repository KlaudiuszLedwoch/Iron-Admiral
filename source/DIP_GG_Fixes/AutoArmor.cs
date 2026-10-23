// 1.11.134 (beta): "Auto armour" button in the ship designer. It builds a coherent armour scheme for the current design and
// spends the displacement that is left on armour:
//  1. all zones are set to their minimum, turret armour to 0;
//  2. one "profile" thickness is searched (bisection) so that the scheme just fits the displacement: belt and conning tower
//     = profile, belt ends 38%, decks 12.5%, superstructure 12.5%, inner belt 22%, inner deck 6.5%; main turrets (10" and
//     above) face = calibre in inches, roof by period (to 1909 18% of calibre, from 1910 22%, from 1930 25%), barbette = half
//     the calibre; medium guns (up to 10") 4"/2"/2", light guns (up to 6") 2"/1"/1", smallest 1"; casemates no barbette;
//  3. weight that is still free is added in small steps by priority (belt and conning tower first, then ends and decks)
//     up to a few per cent above the profile ratios.
// The previous armour is restored if anything fails. Only for the player's own editable design.
// (Approach and ratios taken from UADVanillaPlus "Generate Armor", reimplemented in a shorter form.)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2Cpp;
using Il2CppTMPro;

namespace DipGgFixes
{
    internal static class AutoArmor
    {
        internal static MelonPreferences_Entry<bool> On;
        const string ButtonName = "IronAdmiral_AutoArmor";
        static readonly MethodInfo RefreshHull = AccessTools.Method(typeof(Ship), "RefreshHullStats", Type.EmptyTypes);
        static readonly MethodInfo RefreshGuns = AccessTools.Method(typeof(Ship), "RefreshGunsStats", Type.EmptyTypes);
        static readonly MethodInfo ConShipChanged = AccessTools.Method(typeof(Ui), "OnConShipChanged", new[] { typeof(bool) });
        static readonly MethodInfo RefreshParts = AccessTools.Method(typeof(Ui), "RefreshParts", Type.EmptyTypes);
        static bool busy, missingLogged; static int errors;

        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("AutoArmorButton", true, null, "Ship designer: 'Auto armour' button - a coherent armour scheme that uses the displacement left over");
        }
        static void Log(string m) { MelonLogger.Msg("[DIP GG Fixes][AutoArmor] " + m); }
        static T Safe<T>(Func<T> f, T d) { try { return f(); } catch { return d; } }

        class Target { public Ship.A Zone; public Ship.TurretArmor Row; public float Ratio, Cap, Min, Max, Fixed = -1f; public int Prio; }

        // ---------------------------------------------------------------- button
        static Transform Deep(Transform t, string name)
        {
            if (t == null) return null;
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); if (c.name == name) return c; var d = Deep(c, name); if (d != null) return d; }
            return null;
        }

        internal static void Ensure(Ui ui)
        {
            if (On == null || !On.Value || ui == null) return;
            try
            {
                var src = Deep(ui.transform, "RandomShip"); if (src == null) { if (!missingLogged) { missingLogged = true; Log("designer button template (RandomShip) not found - no Auto armour button"); } return; }
                var b = Deep(ui.transform, ButtonName);
                if (b == null)
                {
                    var go = UnityEngine.Object.Instantiate(src.gameObject, src.parent); go.name = ButtonName;
                    go.transform.SetSiblingIndex(Math.Min(src.GetSiblingIndex() + 1, src.parent.childCount - 1));
                    var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
                    if (btn != null) { btn.onClick.RemoveAllListeners(); btn.onClick.AddListener((Action)Run); }
                    var tmp = go.GetComponentInChildren<TMP_Text>(true);
                    if (tmp != null) { var loc = tmp.GetComponent<LocalizeText>(); if (loc != null) UnityEngine.Object.Destroy(loc); tmp.text = "Auto\narmour"; tmp.enableAutoSizing = true; tmp.fontSizeMin = 10f; }
                    else { var tx = go.GetComponentInChildren<Text>(true); if (tx != null) { var loc = tx.GetComponent<LocalizeText>(); if (loc != null) UnityEngine.Object.Destroy(loc); tx.text = "Auto\narmour"; tx.resizeTextForBestFit = true; } }
                    var oe = go.GetComponent<OnEnter>(); if (oe != null) UnityEngine.Object.Destroy(oe);
                    var ol = go.GetComponent<OnLeave>(); if (ol != null) UnityEngine.Object.Destroy(ol);
                    go.AddComponent<OnEnter>().action = (Action)(() => { try { G.ui.ShowTooltip("Builds a balanced armour scheme for this design and puts the displacement that is left into armour.", go); } catch { } });
                    go.AddComponent<OnLeave>().action = (Action)(() => { try { G.ui.HideTooltip(); } catch { } });
                    b = go.transform; Log("Auto armour button added to the designer");
                }
                bool edit = Safe(() => ui.allowEdit, false) && Safe(() => ui.mainShip != null, false);
                if (b.gameObject.activeSelf != src.gameObject.activeSelf) b.gameObject.SetActive(src.gameObject.activeSelf);
                var bb = b.GetComponent<Button>() ?? b.GetComponentInChildren<Button>(true); if (bb != null) bb.interactable = edit && !busy;
            }
            catch (Exception e) { if (errors++ < 5) Log("button: " + e.Message); }
        }

        // ---------------------------------------------------------------- armour
        static void Recalc(Ship s)
        {
            try { RefreshHull?.Invoke(s, Array.Empty<object>()); } catch { }
            try { RefreshGuns?.Invoke(s, Array.Empty<object>()); } catch { }
            try { s.CalcWeightAndCost(true, true); } catch { }
        }
        static float Weight(Ship s) { return Safe(() => s.Weight(true, true), Safe(() => s.Weight(), 0f)); }
        static float Round(float v) { return Safe(() => G.settings.RoundToArmorStep(v), v); }
        static float In(float inch) { return inch * 25.4f; }

        static float Get(Ship s, Target t)
        {
            if (t.Row != null) return t.Zone == Ship.A.TurretSide ? t.Row.sideTurretArmor : t.Zone == Ship.A.TurretTop ? t.Row.topTurretArmor : t.Row.barbetteArmor;
            float v = 0f; try { if (s.armor != null && s.armor.TryGetValue(t.Zone, out v)) return v; } catch { }
            return 0f;
        }
        static void Set(Ship s, Target t, float v)
        {
            v = Mathf.Clamp(Round(Mathf.Clamp(v, t.Min, t.Max)), t.Min, t.Max);
            try
            {
                if (t.Row != null) { if (t.Zone == Ship.A.TurretSide) t.Row.sideTurretArmor = v; else if (t.Zone == Ship.A.TurretTop) t.Row.topTurretArmor = v; else t.Row.barbetteArmor = v; }
                else s.SetArmor(t.Zone, v, false);
            }
            catch { }
        }

        static void Hull(Ship s, List<Target> l, Ship.A z, float ratio, float cap, int prio)
        {
            float mn = Safe(() => s.MinArmorForZone(z), float.NaN), mx = Safe(() => s.MaxArmorForZone(z), float.NaN);
            if (float.IsNaN(mn) || float.IsNaN(mx)) return; if (mx < mn) { var x = mx; mx = mn; mn = x; }
            if (mx <= 0f) return;
            l.Add(new Target { Zone = z, Ratio = ratio, Cap = cap, Min = mn, Max = mx, Prio = prio });
        }

        static int Year(Ship s)
        {
            int y = Safe(() => (s.isRefitDesign ? s.dateCreatedRefit : s.dateCreated).AsDate().Year, 0);
            if (y <= 0) y = Safe(() => CampaignController.Instance.CurrentDate.AsDate().Year, 1915);
            return y;
        }

        static List<Target> Targets(Ship s)
        {
            var l = new List<Target>();
            Hull(s, l, Ship.A.Belt, 1f, 1.05f, 3); Hull(s, l, Ship.A.ConningTower, 1f, 1.05f, 2);
            Hull(s, l, Ship.A.BeltBow, 0.38f, 0.42f, 4); Hull(s, l, Ship.A.BeltStern, 0.38f, 0.42f, 4);
            Hull(s, l, Ship.A.Deck, 0.125f, 0.18f, 5); Hull(s, l, Ship.A.DeckBow, 0.125f, 0.15f, 7); Hull(s, l, Ship.A.DeckStern, 0.125f, 0.15f, 7);
            Hull(s, l, Ship.A.Superstructure, 0.125f, 0.15f, 9);
            Hull(s, l, Ship.A.InnerDeck_1st, 0.065f, 0.08f, 8); Hull(s, l, Ship.A.InnerBelt_1st, 0.22f, 0.3f, 8);
            int year = Year(s);
            try
            {
                var rows = s.shipTurretArmor;
                if (rows != null) for (int i = 0; i < rows.Count; i++)
                    {
                        var r = rows[i]; if (r == null || r.turretPartData == null) continue;
                        var pd = r.turretPartData; bool cas = r.isCasemateGun;
                        float cal = Safe(() => pd.GetCaliberInch(s), Safe(() => pd.caliber, 0f));
                        float side, top, bar;
                        if (cal <= 4.05f) { side = In(1f); top = In(1f); bar = cas ? 0f : In(1f); }
                        else if (cal <= 6.05f) { side = In(2f); top = In(1f); bar = cas ? 0f : In(1f); }
                        else if (cal < 10f) { side = In(4f); top = In(2f); bar = cas ? 0f : In(2f); }
                        else { float roof = year >= 1930 ? Math.Max(4f, cal * 0.25f) : year >= 1910 ? Math.Max(3f, cal * 0.22f) : Math.Max(2f, cal * 0.18f); side = In(cal); top = In(roof); bar = In(cal * 0.5f); }
                        foreach (var z in new[] { Ship.A.TurretSide, Ship.A.TurretTop, Ship.A.Barbette })
                        {
                            float want = z == Ship.A.TurretSide ? side : z == Ship.A.TurretTop ? top : bar;
                            float mx = Safe(() => s.MaxArmorForZone(z, pd), want); if (float.IsNaN(mx) || mx < 0f) mx = want;
                            float v = Mathf.Clamp(want, 0f, mx);
                            // main turrets are part of the search (priority right after the belt), smaller guns get their fixed band
                            l.Add(new Target { Zone = z, Row = r, Min = 0f, Max = mx, Fixed = v, Prio = cal >= 10f ? (z == Ship.A.TurretSide ? 1 : z == Ship.A.Barbette ? 6 : 7) : 20 });
                        }
                    }
            }
            catch { }
            return l.OrderBy(t => t.Prio).ToList();
        }

        static void Apply(Ship s, List<Target> l, float profile, float k)
        {
            foreach (var t in l) Set(s, t, t.Fixed >= 0f ? t.Fixed * Mathf.Clamp01(k) : profile * t.Ratio);
            Recalc(s);
        }

        static void Run()
        {
            if (busy) return;
            var ui = Safe(() => G.ui, null); var s = Safe(() => ui.mainShip, null);
            if (ui == null || s == null || !Safe(() => ui.allowEdit, false)) { Log("skipped - no editable design in the designer"); return; }
            busy = true;
            var hullBefore = new Dictionary<Ship.A, float>(); var rowsBefore = new List<(Ship.TurretArmor r, float a, float b, float c)>();
            try
            {
                try { var en = s.armor.GetEnumerator(); while (en.MoveNext()) hullBefore[en.Current.Key] = en.Current.Value; } catch { }
                try { var rs = s.shipTurretArmor; for (int i = 0; i < rs.Count; i++) { var r = rs[i]; if (r != null) rowsBefore.Add((r, r.sideTurretArmor, r.topTurretArmor, r.barbetteArmor)); } } catch { }
                var l = Targets(s);
                if (l.Count == 0) { Log("no armour zones found on this design"); return; }
                float tonnage = Safe(() => s.Tonnage(), 0f), target = tonnage - 1f;
                // minimum everywhere
                foreach (var t in l) Set(s, t, t.Min);
                Recalc(s);
                float w0 = Weight(s);
                if (tonnage <= 0f || w0 > target) { Log($"no displacement left for armour ({w0:0} t of {tonnage:0} t at minimum armour) - minimum armour set"); Refresh(ui, s); return; }
                // turrets of small guns: as much of their band as fits (k), then the profile
                float k = 1f;
                Apply(s, l, 0f, k);
                if (Weight(s) > target) { float lo = 0f, hi = 1f; for (int i = 0; i < 12; i++) { float m = (lo + hi) / 2f; Apply(s, l, 0f, m); if (Weight(s) <= target) lo = m; else hi = m; } k = lo; }
                float high = 0f; foreach (var t in l) if (t.Fixed < 0f && t.Ratio > 0f) high = Math.Max(high, t.Max / t.Ratio);
                float best = 0f, a = 0f, b2 = Math.Max(high, 1f);
                for (int i = 0; i < 28; i++) { float m = (a + b2) / 2f; Apply(s, l, m, k); if (Weight(s) <= target + 0.001f) { best = m; a = m; } else b2 = m; }
                Apply(s, l, best, k);
                // fine fill: small steps by priority up to the cap ratios (main turrets up to the profile)
                float step = Math.Max(Round(1f), 0.5f); int accepted = 0, tries = 0; bool any;
                do
                {
                    any = false;
                    foreach (var t in l)
                    {
                        if (tries > 700) break;
                        float cap = t.Fixed >= 0f ? (t.Prio <= 7 ? Math.Max(t.Fixed, best) : t.Fixed) : best * t.Cap;
                        float cur = Get(s, t); if (cur >= Math.Min(cap, t.Max) - 0.01f) continue;
                        tries++; Set(s, t, Math.Min(cur + step, cap)); Recalc(s);
                        if (Weight(s) <= target) { accepted++; any = true; } else { Set(s, t, cur); Recalc(s); }
                    }
                } while (any && tries <= 700);
                float w = Weight(s);
                Log($"design {s.name}: profile {best:0} mm (belt), small-gun turret bands x{k:0.00}, {accepted} fine steps, weight {w:0} of {tonnage:0} t ({tonnage - w:0} t left)");
                Refresh(ui, s);
            }
            catch (Exception e)
            {
                Log("failed, previous armour restored: " + e.Message);
                try { foreach (var kv in hullBefore) s.SetArmor(kv.Key, kv.Value, false); foreach (var x in rowsBefore) { x.r.sideTurretArmor = x.a; x.r.topTurretArmor = x.b; x.r.barbetteArmor = x.c; } Recalc(s); Refresh(ui, s); } catch { }
            }
            finally { busy = false; }
        }

        static void Refresh(Ui ui, Ship s)
        {
            try { RefreshHull?.Invoke(s, Array.Empty<object>()); } catch { }
            try { ConShipChanged?.Invoke(ui, new object[] { false }); } catch { }
            try { ui.RefreshConstructorInfo(); } catch { }
            try { RefreshParts?.Invoke(ui, Array.Empty<object>()); } catch { }
        }
    }

    [HarmonyPatch]
    internal static class Patch_AutoArmorButton
    {
        static MethodBase TargetMethod() { return SafePatches.Find(typeof(Ui), "RefreshConstructorInfo", Type.EmptyTypes); }
        static bool Prepare() { return TargetMethod() != null; }
        static void Postfix(Ui __instance) { AutoArmor.Ensure(__instance); }
    }
}
