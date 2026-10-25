// 1.11.258 (author 25.10, 14:07): the battle dispatches tell where a shell struck and what it did - "to make it more dramatic".
// Notable hits only (heavy shells, fires, flooding, damaged machinery or guns, shells passing through or stopped by the armour),
// at most one dispatch per ship every 25 s and one in 6 s overall, so the dispatch log does not drown in hits. The place comes from
// the damage path (OverPen.Deal: part and hull section), fire / flooding / damaged modules from the same call. Enemy ships are named
// only when the player's side has seen them (fog of war). Switch: BattleHitDispatches (F10, Battle).
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
        static float lastAny = -100f; static int n, errors; static int battleId;
        static readonly System.Random rng = new System.Random();
        static string Pick(params string[] v) { return v[rng.Next(v.Length)]; }

        static string Calibre(float mm)
        {
            float inch = mm / 25.4f;
            return inch >= 4f ? $"{Math.Round(inch):0}-inch" : $"{mm:0} mm";
        }
        static string Place(string loc, out bool wet)
        {
            wet = loc != null && loc.Contains("waterline");
            if (loc == null) return "the hull";
            if (loc.StartsWith("upper works")) return "the upper works";
            if (loc.StartsWith("turret")) return "a turret";
            if (loc.StartsWith("hull ends")) return "the hull forward or aft";
            if (loc.StartsWith("machinery")) { string l = loc.ToLowerInvariant(); return l.Contains("ammo") || l.Contains("magazine") ? "the magazine spaces" : "the machinery spaces"; }
            if (loc.StartsWith("citadel")) return "amidships";
            return "the hull";
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
                float now = Time.realtimeSinceStartup;
                if (now - lastAny < 6f) return;
                if (lastShip.TryGetValue(victim.Pointer, out var t) && now - t < 25f) return;
                bool vPlayer = false; try { vPlayer = victim.player != null && victim.player.isMain; } catch { }
                string vName = Dispatch.Ship(victim.name);
                if (!vPlayer)
                {
                    bool seen = false;
                    try { var obs = shooter != null ? shooter.player : null; seen = obs != null && Fog.PType(obs, victim) != null; } catch { seen = true; }
                    if (!seen) vName = "the enemy ship";
                }
                lastAny = now; lastShip[victim.Pointer] = now;
                bool wet; string place = Place(loc, out wet);
                string cal = Calibre(calMm);
                string what = Effect(hitType, place, wet, fire, flood, mods);
                string text;
                if (vPlayer)
                    text = Pick(
                        $"{vName}, damage control: \"{Cap(cal)} hit in {place}! {what}\"",
                        $"{vName}: a {cal} shell strikes {place}. {Cap(what)}",
                        $"Report from {vName}: \"We've been hit - {place}, {cal}. {what}\"");
                else
                    text = Pick(
                        $"Gunnery officer: \"Hit on {vName} - {place}! {what}\"",
                        $"Spotting top: \"A {cal} shell has struck {vName} in {place}. {what}\"",
                        $"Lookout: \"Hit, hit! {Cap(vName)}, {place}. {what}\"");
                Dispatch.SayHit("hit|" + Dispatch.Ship(victim.name) + "|" + (n++), text);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Dispatch] hit: " + e.Message); }
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }
        static string Effect(string hitType, string place, bool wet, bool fire, bool flood, string mods)
        {
            var parts = new List<string>();
            if (hitType == "over") parts.Add(Pick("It went clean through without bursting - holes on both sides and splinters everywhere.", "Straight in and out the other side - the fuze never worked, but it tore everything in its path.", "Through and through! No burst, just two holes and wrecked fittings."));
            else if (hitType == "armor") parts.Add(Pick("The armour held - the shell broke up on the plate.", "It rang off the armour - dented, but not through.", "Stopped by the belt - the plate is cracked but holding."));
            else if (hitType == "normal") parts.Add(Pick("It burst inside!", "The shell went through the armour and exploded below decks!", "A full penetration - a heavy explosion inside!"));
            else if (hitType == "ricochet") parts.Add("It glanced off and skipped away.");
            if (!string.IsNullOrEmpty(mods)) parts.Add($"Damage to {mods}.");
            if (fire) parts.Add(Pick("Fire! Fire parties forward!", "She's burning - smoke pouring from the hit.", "A fire has broken out."));
            if (flood || (wet && hitType != "armor")) parts.Add(Pick("Water is coming in fast!", "She's taking water - pumps!", "Flooding below the waterline!"));
            return string.Join(" ", parts);
        }
    }
}
