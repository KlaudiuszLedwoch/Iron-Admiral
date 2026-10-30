// 1.11.258 (author 25.10, 14:07): the battle dispatches tell where a shell struck and what it did - "to make it more dramatic".
// Notable hits only (heavy shells, fires, flooding, damaged machinery or guns, shells passing through or stopped by the armour),
// at most one dispatch per ship every 25 s and one in 6 s overall, so the dispatch log does not drown in hits. The place comes from
// the damage path (OverPen.Deal: part and hull section), fire / flooding / damaged modules from the same call. Enemy ships are named
// only when the player's side has seen them (fog of war). Switch: BattleHitDispatches (F10, Battle).
// 1.11.283: the texts come from the language files (DispatchLang.cs) - the struck ship's people speak when the victim is the
// player's, otherwise the firing ship's gunnery officers, each in their navy's language.
using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DipGgFixes
{
    internal static class HitDispatch
    {
        internal static MelonPreferences_Entry<bool> On;
        internal static void CreatePrefs(MelonPreferences_Category cat)
        {
            On = cat.CreateEntry("BattleHitDispatches", true, null, "Battle dispatches also tell where notable shell hits struck and what they did (fires, flooding, machinery, turrets, shells passing through or stopped by the armour); at most one per ship every 25 s");
        }
        static readonly Dictionary<IntPtr, float> lastShip = new Dictionary<IntPtr, float>();
        static float lastAny = -100f; static int n, errors;

        static string Calibre(string lang, float mm)
        {
            float inch = mm / 25.4f;
            return inch >= 4f ? (DLang.T(lang, "cal.inch", "n", $"{Math.Round(inch):0}") ?? $"{Math.Round(inch):0}-inch") : (DLang.T(lang, "cal.mm", "n", $"{mm:0}") ?? $"{mm:0} mm");
        }
        // the kind of place (used to choose the effect) and its id in the language files
        static string PlaceKind(string loc, out bool wet)
        {
            wet = loc != null && loc.Contains("waterline");
            if (loc == null) return "hull";
            if (loc.StartsWith("upper works")) return "upper";
            if (loc.StartsWith("turret")) return "turret";
            if (loc.StartsWith("hull ends")) return "ends";
            if (loc.StartsWith("machinery")) { string l = loc.ToLowerInvariant(); return l.Contains("ammo") || l.Contains("magazine") ? "magazine" : "machinery"; }
            if (loc.StartsWith("citadel")) return "citadel";
            return "hull";
        }

        // called from PhysPen.Taken (Ui.RegisterTakenDamage) right after the damage path of the same hit
        internal static void Hit(Ship victim, Ship shooter, float calMm, string hitType, float damage)
        {
            if (On == null || !On.Value || victim == null || Dispatch.On == null || !Dispatch.On.Value) return;
            try
            {
                if (OverPen.LastFrame != Time.frameCount) return;
                string loc = OverPen.LastLoc; bool fire = OverPen.LastFire, flood = OverPen.LastFlood; string mods = OverPen.LastModules;
                bool heavy = calMm >= 200f;
                bool notable = fire || flood || !string.IsNullOrEmpty(mods)
                               || (heavy && (hitType == "normal" || hitType == "over" || hitType == "armor"))
                               || (calMm >= 150f && hitType == "normal" && loc != null && (loc.StartsWith("machinery") || loc.StartsWith("turret")));
                if (!notable) return;
                try { bool w0; Highlights.Hit(victim, calMm, PlaceKind(loc, out w0), fire, flood); } catch { }   // 1.11.422
                float now = Time.realtimeSinceStartup;
                if (now - lastAny < 6f) return;
                if (lastShip.TryGetValue(victim.Pointer, out var t) && now - t < 25f) return;
                bool vPlayer = false; try { vPlayer = victim.player != null && victim.player.isMain; } catch { }
                // who speaks: the people of the struck ship when she is the player's, otherwise the gunnery officers of the firing ship
                string lang = vPlayer ? DLang.For(victim) : DLang.For(shooter);
                string vName = Dispatch.Ship(victim.name);
                if (!vPlayer)
                {
                    bool seen = false;
                    try { var obs = shooter != null ? shooter.player : null; seen = obs != null && Fog.PType(obs, victim) != null; } catch { seen = true; }
                    if (!seen) vName = DLang.W(lang, "enemy.ship", "the enemy ship");
                }
                lastAny = now; lastShip[victim.Pointer] = now;
                bool wet; string kind = PlaceKind(loc, out wet);
                string place = DLang.W(lang, "place." + kind, "the hull");
                string cal = Calibre(lang, calMm);
                string what = Effect(lang, hitType, kind, wet, fire, flood, mods);
                // 1.11.280 (author 25.10, 18:52 and 19:32): the voices of the crews - blood, fear, terror, prayers and curses, in the
                // language of the war films of the period; the heavier the blow, the darker the voice
                bool grave = damage >= 400f || fire || flood || hitType == "normal" && heavy;
                string id = (vPlayer ? "hit.own." : "hit.enemy.") + (grave ? "grave" : "light");
                string text = DLang.T(lang, id, "v", vName, "cal", cal, "place", place, "what", what);
                if (text == null) return;
                Dispatch.SayHit("hit|" + Dispatch.Ship(victim.name) + "|" + (n++), text);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Dispatch] hit: " + e.Message); }
        }
        static string Effect(string lang, string hitType, string kind, bool wet, bool fire, bool flood, string mods)
        {
            var parts = new List<string>();
            void Add(string id, params string[] kv) { var s = DLang.T(lang, id, kv); if (!string.IsNullOrEmpty(s)) parts.Add(s); }
            if (hitType == "over") Add("eff.over");
            else if (hitType == "armor") Add("eff.armor");
            else if (hitType == "normal")
                Add(kind == "magazine" ? "eff.magazine" : kind == "machinery" ? "eff.machinery" : kind == "turret" ? "eff.turret" : kind == "upper" ? "eff.upper" : "eff.burst");
            else if (hitType == "ricochet") Add("eff.ricochet");
            if (!string.IsNullOrEmpty(mods)) Add("eff.mods", "mods", mods);
            if (fire) Add("eff.fire");
            if (flood || (wet && hitType != "armor")) Add("eff.flood");
            return string.Join(" ", parts);
        }
    }
}
