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
                // 1.11.280 (author 25.10, 18:52 and 19:32): the voices of the crews - blood, fear, terror, prayers and curses, in the
                // language of the war films of the period; the heavier the blow, the darker the voice
                bool grave = damage >= 400f || fire || flood || hitType == "normal" && heavy;
                if (vPlayer)
                    text = grave ? Pick(
                        $"{vName}, damage control: \"{Cap(cal)} hit in {place}! {what} Stretcher bearers aft - there's blood on every ladder!\"",
                        $"{vName}: a {cal} shell strikes {place}. {Cap(what)} The screaming hasn't stopped.",
                        $"Report from {vName}: \"We've been hit - {place}, {cal}. {what} God have mercy, the men in there...\"",
                        $"{vName}, bridge: \"Jesus Christ - {place}, {cal}! {what} Get those men out of there!\"",
                        $"{vName}: \"Hit {place} - {cal}! {what} Half the party's down, sir - we need every man we can get!\"",
                        $"{vName}, first lieutenant: \"{Cap(place)} - {cal}. {what} Mother of God, the deck's running red.\"")
                    : Pick(
                        $"{vName}, damage control: \"{Cap(cal)} hit in {place}! {what}\"",
                        $"{vName}: a {cal} shell strikes {place}. {Cap(what)}",
                        $"Report from {vName}: \"We've been hit - {place}, {cal}. {what}\"",
                        $"{vName}: \"{Cap(cal)} in {place} - {what} Steady, lads, she can take it.\"",
                        $"{vName}, bridge: \"That one was close - {place}, {cal}. {what} Keep her firing!\"");
                else
                    text = grave ? Pick(
                        $"Gunnery officer: \"Hit on {vName} - {place}! {what} That's blood in the water, lads - keep it up!\"",
                        $"Spotting top: \"A {cal} shell has struck {vName} in {place}. {what} God help the poor devils aboard.\"",
                        $"Lookout: \"Hit, hit! {Cap(vName)}, {place}. {what} She's a slaughterhouse!\"",
                        $"Turret captain: \"{Cap(vName)} - {place}! {what} Did you see that? Did you see it?\"",
                        $"Gunnery officer: \"{Cap(cal)} into {vName}, {place} - {what} No prayers for them today. Reload!\"")
                    : Pick(
                        $"Gunnery officer: \"Hit on {vName} - {place}! {what}\"",
                        $"Spotting top: \"A {cal} shell has struck {vName} in {place}. {what}\"",
                        $"Lookout: \"Hit, hit! {Cap(vName)}, {place}. {what}\"",
                        $"Rangefinder: \"On target - {vName}, {place}. {what} Same again, lads!\"");
                Dispatch.SayHit("hit|" + Dispatch.Ship(victim.name) + "|" + (n++), text);
            }
            catch (Exception e) { if (errors++ < 5) MelonLogger.Warning("[DIP GG Fixes][Dispatch] hit: " + e.Message); }
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1); }
        static string Effect(string hitType, string place, bool wet, bool fire, bool flood, string mods)
        {
            var parts = new List<string>();
            bool turret = place.Contains("turret"), upper = place.Contains("upper"), mach = place.Contains("machinery"), mag = place.Contains("magazine");
            if (hitType == "over") parts.Add(Pick(
                "It went clean through without bursting - holes on both sides and splinters everywhere.",
                "Straight in and out the other side - the fuze never worked, but it tore everything in its path.",
                "Through and through! No burst, just two holes and wrecked fittings.",
                "It passed right through - a dud, thank God, but it took two men with it.",
                "No explosion - it went out the far side, and what it met on the way isn't men any more."));
            else if (hitType == "armor") parts.Add(Pick(
                "The armour held - the shell broke up on the plate.",
                "It rang off the armour - dented, but not through.",
                "Stopped by the belt - the plate is cracked but holding.",
                "The plate held, but the shock threw men off their feet and the paint's burning.",
                "Broke up on the armour - splinters have cut down everyone on the deck above.",
                "Held! Sweet Mary, it held - rivets flying like bullets, but it held."));
            else if (hitType == "normal") parts.Add(
                mag ? Pick("It burst in the magazine spaces - flash! Flood the magazines, flood them NOW!", "God in heaven - the magazine! If that flash reaches the cordite we're all dead men!", "A burst beside the shell rooms - the handling party is gone. Flood it, for Christ's sake!")
                : mach ? Pick("It burst among the boilers - steam everywhere, the stokers are being boiled alive!", "The engine room - a burst in the engine room! Scalded men crawling out, we're losing steam!", "Exploded in the machinery spaces. No one who was in there is coming out.")
                : turret ? Pick("It burst inside the turret - the gun crew are dead to a man.", "The turret - it's a charnel house in there, the flash took them all!", "A burst in the gunhouse - blood running out of the trunk, the guns are silent.")
                : upper ? Pick("It burst in the superstructure - the bridge is a wreck, bodies everywhere!", "Exploded among the upper works - signalmen, lookouts, all gone, and the fires are starting.", "A burst below the bridge - the captain's down, the navigator's down, somebody take the wheel!")
                : Pick("It burst inside!", "The shell went through the armour and exploded below decks!", "A full penetration - a heavy explosion inside!", "It burst between decks - men torn to pieces, the mess deck is a butcher's shop.", "Through the armour and burst inside - God forgive us, the screams."));
            else if (hitType == "ricochet") parts.Add(Pick("It glanced off and skipped away.", "Ricochet - it bounced off howling and went into the sea.", "Glanced off! Thank God - it skipped away over the water."));
            if (!string.IsNullOrEmpty(mods)) parts.Add(Pick($"Damage to {mods}.", $"{Cap(mods)} - knocked out, and the men with it.", $"We've lost {mods}."));
            if (fire) parts.Add(Pick("Fire! Fire parties forward!", "She's burning - smoke pouring from the hit.", "A fire has broken out.", "Fire! The flames are in the ready-use ammunition - get the hoses on it or we're finished!", "She's ablaze - men are jumping from the burning deck!"));
            if (flood || (wet && hitType != "armor")) parts.Add(Pick("Water is coming in fast!", "She's taking water - pumps!", "Flooding below the waterline!", "Water pouring in - shut the watertight doors, leave them, LEAVE THEM!", "Flooding fast - the men below never had a chance."));
            return string.Join(" ", parts);
        }
    }
}